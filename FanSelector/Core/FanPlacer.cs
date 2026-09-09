using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace FanSelector.Core
{
    internal class PlacementResult
    {
        public bool Placed { get; set; }
        public bool Cancelled { get; set; }

        /// <summary>Something to tell the user. Null when there is nothing to say.</summary>
        public string Message { get; set; }

        public static PlacementResult Cancel() { return new PlacementResult { Cancelled = true }; }
        public static PlacementResult Fail(string message) { return new PlacementResult { Message = message }; }
    }

    internal static class FanPlacer
    {
        /// <summary>
        /// Ask for a point and put the chosen fan type there, loading that one type
        /// from the family file first when the project does not have it yet.
        ///
        /// The point is picked BEFORE the transaction opens. Picking is a modal
        /// user interaction and has no business running inside an open
        /// transaction — and pressing Esc during the pick is a cancellation, not
        /// an error.
        /// </summary>
        /// <param name="requestedAirFlow">
        /// What the user searched for, in internal units. Not the catalogue's own
        /// figure: it is the duty the system has to carry, and it is what a
        /// generated stub's terminal is given.
        /// </param>
        public static PlacementResult Place(UIDocument uidoc, FanCandidate candidate,
                                            FamilyMapping mapping, FanSettings settings,
                                            double requestedAirFlow)
        {
            if (uidoc == null || candidate == null) return PlacementResult.Fail("Nothing to place.");

            Document doc = uidoc.Document;

            XYZ point;
            try
            {
                point = uidoc.Selection.PickPoint("Click the insertion point for " + candidate.TypeName);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return PlacementResult.Cancel();
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Raised in views that cannot host a point pick, e.g. a schedule.
                return PlacementResult.Fail(
                    "This view does not accept a point. Switch to a plan, section or 3D view and try again.");
            }

            Level level = ResolveLevel(doc, point);
            if (level == null)
                return PlacementResult.Fail("The project has no levels, so there is nothing to host the fan on.");

            using (Transaction transaction = new Transaction(doc, "Place fan"))
            {
                transaction.Start();
                try
                {
                    string problem;
                    string typeNote;
                    FamilySymbol symbol = Resolve(doc, candidate, mapping, out typeNote, out problem);
                    if (symbol == null)
                    {
                        transaction.RollBack();
                        return PlacementResult.Fail(problem);
                    }

                    if (!symbol.IsActive)
                    {
                        symbol.Activate();
                        doc.Regenerate();
                    }

                    XYZ target = new XYZ(point.X, point.Y, point.Z + settings.MountingOffsetFt);
                    FamilyInstance instance = doc.Create.NewFamilyInstance(
                        target, symbol, level, StructuralType.NonStructural);

                    string writeNote = WriteFigures(instance, mapping, candidate);

                    string stubNote = mapping.AddDuctStub
                        ? DuctStub.Build(doc, instance, mapping, settings, requestedAirFlow)
                        : null;

                    transaction.Commit();
                    return new PlacementResult
                    {
                        Placed = true,
                        Message = Join(Join(typeNote, writeNote), stubNote)
                    };
                }
                catch (Exception exception)
                {
                    transaction.RollBack();
                    return PlacementResult.Fail("The fan could not be placed: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// The type to place, created from its catalogue line when the project does
        /// not have it.
        ///
        /// It cannot be loaded from the .rfa: the types a type catalogue defines
        /// exist ONLY in the .csv — the family file itself has none of them, which
        /// is the whole point of a catalogue — so LoadFamilySymbol has nothing to
        /// find. What Revit does when it applies a catalogue is what is done here:
        /// take a type of the family and set every parameter the catalogue names.
        /// </summary>
        private static FamilySymbol Resolve(Document doc, FanCandidate candidate,
                                            FamilyMapping mapping, out string note, out string problem)
        {
            note = null;
            problem = null;
            if (candidate.Symbol != null) return candidate.Symbol;

            List<FamilySymbol> existing = ParameterScanner.SymbolsOf(doc, mapping.FamilyName);

            // Between searching and inserting, somebody may have loaded it.
            FamilySymbol already = existing.FirstOrDefault(
                s => string.Equals(s.Name, candidate.TypeName, StringComparison.OrdinalIgnoreCase));
            if (already != null) return already;

            if (existing.Count == 0)
            {
                problem = "The family \"" + mapping.FamilyName + "\" has no types in this project, so there "
                        + "is nothing to build \"" + candidate.TypeName + "\" from.\n\n"
                        + "Load the family into the project first.";
                return null;
            }

            FamilySymbol created;
            try
            {
                created = existing[0].Duplicate(candidate.TypeName) as FamilySymbol;
            }
            catch (Exception exception)
            {
                problem = "The type \"" + candidate.TypeName + "\" could not be created in this project: "
                        + exception.Message;
                return null;
            }

            if (created == null)
            {
                problem = "The type \"" + candidate.TypeName + "\" could not be created in this project.";
                return null;
            }

            note = ApplyCatalogRow(created, candidate, mapping);
            return created;
        }

        /// <summary>
        /// Write the whole catalogue line onto a freshly made type. Every column of
        /// a type catalogue is named after a family parameter — that is how Revit
        /// matches them — so the names line up by construction. Columns that name
        /// an instance parameter cannot be set here and are reported, because a
        /// dimension that silently kept the template type's value would give the
        /// wrong fan.
        /// </summary>
        private static string ApplyCatalogRow(FamilySymbol symbol, FanCandidate candidate,
                                              FamilyMapping mapping)
        {
            CatalogFile file = CatalogFile.For(mapping.CatalogPath);
            if (candidate.Row == null || !file.IsUsable)
                return "The type \"" + candidate.TypeName + "\" was created by copying \""
                     + symbol.Name + "\", but its catalogue line could not be re-read, so only the "
                     + "figures below were set.";

            var missed = new List<string>();
            int applied = 0;

            foreach (CatalogColumn column in file.Columns)
            {
                Parameter parameter;
                try { parameter = symbol.LookupParameter(column.Name); }
                catch { parameter = null; }

                if (parameter == null || parameter.IsReadOnly)
                {
                    // A column with no value on this line was never meant to be set.
                    if (candidate.Row.Raw.ContainsKey(column.Name)) missed.Add(column.Name);
                    continue;
                }

                if (Apply(parameter, candidate.Row, column.Name)) applied++;
            }

            string text = "\"" + candidate.TypeName + "\" was not in the model, so it was created from the "
                        + "catalogue (" + applied + " of " + file.Columns.Count + " columns set).";
            if (missed.Count > 0)
                text += "\n\nThese columns are not type parameters of the family, so they could not be set "
                      + "on the type: " + string.Join(", ", missed.ToArray())
                      + ". If any of them drives the geometry, the new type will look like the one it was "
                      + "copied from.";
            return text;
        }

        private static bool Apply(Parameter parameter, CatalogRow row, string column)
        {
            try
            {
                double? number = row.Number(column);
                if (parameter.StorageType == StorageType.Double && number.HasValue)
                    return parameter.Set(number.Value);
                if (parameter.StorageType == StorageType.Integer && number.HasValue)
                    return parameter.Set((int)Math.Round(number.Value));
                if (parameter.StorageType == StorageType.String)
                {
                    string text = row.Text(column);
                    return text.Length > 0 && parameter.Set(text);
                }
            }
            catch { /* one column must not lose the type */ }
            return false;
        }

        private static string Join(string first, string second)
        {
            if (string.IsNullOrEmpty(first)) return second;
            if (string.IsNullOrEmpty(second)) return first;
            return first + "\n\n" + second;
        }

        /// <summary>
        /// Copy the catalogue figures onto the placed fan, into whichever family
        /// parameters the user mapped. This is what the whole write half of the
        /// mapping is for: a fan family normally declares air flow and pressure as
        /// instance parameters that hold nothing until something fills them.
        ///
        /// Both sides are in internal units, so nothing is converted here.
        /// </summary>
        private static string WriteFigures(FamilyInstance instance, FamilyMapping mapping,
                                           FanCandidate candidate)
        {
            if (instance == null || mapping == null) return null;

            var problems = new List<string>();

            foreach (QuantityInfo info in Quantities.All)
            {
                string parameterName = mapping.Param(info.Quantity);
                if (string.IsNullOrEmpty(parameterName)) continue;

                double value;
                if (!candidate.Values.TryGetValue(info.Quantity, out value)) continue;

                Parameter parameter;
                try { parameter = instance.LookupParameter(parameterName); }
                catch { parameter = null; }

                if (parameter == null)
                {
                    problems.Add("\"" + parameterName + "\" (" + info.DisplayName
                                 + ") is not an instance parameter of this family");
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    problems.Add("\"" + parameterName + "\" (" + info.DisplayName + ") is read-only");
                    continue;
                }

                try
                {
                    if (parameter.StorageType == StorageType.Double) parameter.Set(value);
                    else if (parameter.StorageType == StorageType.Integer)
                        parameter.Set((int)Math.Round(value));
                    else if (parameter.StorageType == StorageType.String)
                        parameter.Set(value.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture));
                    else problems.Add("\"" + parameterName + "\" cannot hold a number");
                }
                catch (Exception exception)
                {
                    problems.Add("\"" + parameterName + "\": " + exception.Message);
                }
            }

            if (problems.Count == 0) return null;

            return "The fan was placed, but some figures could not be written onto it:\n\n  • "
                 + string.Join("\n  • ", problems.ToArray())
                 + "\n\nCheck the \"write to\" column in Options for this family.";
        }

        /// <summary>
        /// The level to host the instance on: the view's own level in a plan, and
        /// otherwise the nearest level at or below the picked point. Reading
        /// ActiveView.GenLevel unguarded throws in every 3D view and section.
        /// </summary>
        private static Level ResolveLevel(Document doc, XYZ point)
        {
            try
            {
                Level viewLevel = doc.ActiveView == null ? null : doc.ActiveView.GenLevel;
                if (viewLevel != null) return viewLevel;
            }
            catch { /* some view types throw rather than return null */ }

            List<Level> levels;
            try
            {
                levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();
            }
            catch { return null; }

            if (levels.Count == 0) return null;

            Level below = levels.LastOrDefault(l => l.Elevation <= point.Z + 1e-6);
            return below ?? levels[0];
        }
    }
}
