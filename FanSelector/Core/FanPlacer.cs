using System;
using System.Collections.Generic;
using System.IO;
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
        /// <param name="addStub">
        /// Decided at insertion time in the Fan Selector window, which seeds it
        /// from the family's own setting. Whether THIS fan gets a stub is a
        /// property of the placement, not of the family.
        /// </param>
        public static PlacementResult Place(UIDocument uidoc, FanCandidate candidate,
                                            FamilyMapping mapping, FanSettings settings,
                                            double requestedAirFlow, bool addStub)
        {
            if (uidoc == null || candidate == null) return PlacementResult.Fail("Nothing to place.");

            Document doc = uidoc.Document;

            XYZ point;
            try
            {
                point = uidoc.Selection.PickPoint("Click the insertion point for " + candidate.Designation);
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

                    string stubNote = addStub
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
        /// The type to place. If the project already has it — which it normally
        /// does, because a family holds one type per physical size and a project
        /// tends to have the sizes it uses — that one is used untouched. Only when
        /// it is missing is the family file loaded, and then without overwriting
        /// anything already there.
        ///
        /// Note this looks for the TYPE name, not the catalogue designation: the
        /// line "710/9/30/5Z" asks for the family's "710", and no amount of
        /// loading will produce a type called "710/9/30/5Z".
        /// </summary>
        private static FamilySymbol Resolve(Document doc, FanCandidate candidate,
                                            FamilyMapping mapping, out string note, out string problem)
        {
            note = null;
            problem = null;
            if (candidate.Symbol != null) return candidate.Symbol;

            CatalogFile file = CatalogFile.For(mapping.CatalogPath);
            string typeColumn = FanSearch.EffectiveTypeColumn(doc, mapping, file);
            List<string> wanted = FanSearch.TypeNameCandidates(typeColumn, candidate.Row, file);
            if (wanted.Count == 0) wanted.Add(candidate.TypeName);

            // Between searching and inserting, somebody may have loaded it.
            FamilySymbol already = FindAny(doc, mapping.FamilyName, wanted);
            if (already != null) return Accept(already, candidate, ref note);

            string familyFile = FamilyFileFor(mapping);
            if (familyFile == null)
            {
                problem = "The type \"" + candidate.TypeName + "\" is not in this project, and the family "
                        + "file it should come from was not found beside the catalogue.\n\n"
                        + "Put the .rfa next to:\n" + mapping.CatalogPath;
                return null;
            }

            try
            {
                Family family;
                doc.LoadFamily(familyFile, new LoadOptions(), out family);
            }
            catch (Exception exception)
            {
                problem = "The family could not be loaded from:\n" + familyFile + "\n\n" + exception.Message;
                return null;
            }

            doc.Regenerate();

            // LoadFamily reports false when the family was already present, which
            // says nothing about whether the wanted type arrived — so look rather
            // than trust the return value.
            FamilySymbol loaded = FindAny(doc, mapping.FamilyName, wanted);
            if (loaded == null)
            {
                problem = "The catalogue line \"" + candidate.Designation + "\" could not be matched to any "
                        + "type of \"" + mapping.FamilyName + "\".\n\nLooked for: "
                        + string.Join(", ", wanted.ToArray())
                        + "\n\n" + Existing(doc, mapping.FamilyName)
                        + "\n\nSet \"Revit type name from\" in Options to the catalogue column holding "
                        + "those names.";
                return null;
            }

            note = "The type \"" + loaded.Name + "\" was not in the model, so the family was loaded from "
                 + "its file. Types already in the project were left as they were.";
            return Accept(loaded, candidate, ref note);
        }

        /// <summary>
        /// Take the type that was actually found, and say so when it is not the
        /// one the mapping asked for — that means the type column is pointing
        /// somewhere else, and the user should know rather than wonder.
        /// </summary>
        private static FamilySymbol Accept(FamilySymbol symbol, FanCandidate candidate, ref string note)
        {
            if (!string.Equals(symbol.Name, candidate.TypeName, StringComparison.OrdinalIgnoreCase))
                note = Join(note,
                    "The mapping expected the family type \"" + candidate.TypeName + "\" for line \""
                    + candidate.Designation + "\", but the family has \"" + symbol.Name + "\", which was "
                    + "used instead. Set \"Revit type name from\" in Options to the column holding \""
                    + symbol.Name + "\" so this is not a guess every time.");

            candidate.TypeName = symbol.Name;
            return symbol;
        }

        /// <summary>
        /// What the family DOES have. Without this a failure says only what was
        /// missing, which leaves the user — and anyone they ask — guessing at the
        /// naming the family actually uses.
        /// </summary>
        private static string Existing(Document doc, string familyName)
        {
            List<string> names = ParameterScanner.SymbolsOf(doc, familyName)
                .Select(s => s.Name).ToList();

            if (names.Count == 0)
                return "The family has no types in this project at all.";

            const int show = 20;
            string listed = string.Join(", ", names.Take(show).ToArray());
            if (names.Count > show) listed += ", … (" + (names.Count - show) + " more)";
            return "The " + names.Count + " types it does have are named: " + listed;
        }

        private static FamilySymbol FindAny(Document doc, string familyName, List<string> typeNames)
        {
            List<FamilySymbol> symbols = ParameterScanner.SymbolsOf(doc, familyName);
            foreach (string wanted in typeNames)
            {
                FamilySymbol hit = symbols.FirstOrDefault(
                    s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>The .rfa beside the catalogue, under the same name, or null.</summary>
        private static string FamilyFileFor(FamilyMapping mapping)
        {
            if (mapping == null || string.IsNullOrEmpty(mapping.CatalogPath)) return null;
            try
            {
                string candidate = Path.ChangeExtension(mapping.CatalogPath, ".rfa");
                return File.Exists(candidate) ? candidate : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Loading must never rewrite a type the project already has. The office
        /// copy of a family is the authority on its own types — this add-in is
        /// only here to bring in one that is missing.
        /// </summary>
        private class LoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                                            out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Project;
                overwriteParameterValues = false;
                return true;
            }
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

                // A mapped target that cannot be written is not a reason to drop
                // the value on the floor. A fan family usually has a writable
                // twin of the same kind — "Override AirFlow" beside a calculated
                // "TES_AirFlow" — so use it and say so, rather than refusing and
                // leaving the user to work out which name was meant.
                if (parameter == null || parameter.IsReadOnly)
                {
                    Parameter substitute = Writable(instance, info);
                    if (substitute == null)
                    {
                        problems.Add("\"" + parameterName + "\" (" + info.DisplayName + ") "
                                     + (parameter == null
                                            ? "is not a parameter of this fan"
                                            : "is read-only")
                                     + ", and the fan has no writable one of that kind");
                        continue;
                    }

                    problems.Add("\"" + parameterName + "\" (" + info.DisplayName + ") "
                                 + (parameter == null ? "does not exist" : "is read-only")
                                 + " — \"" + substitute.Definition.Name + "\" was written instead. "
                                 + "Set it as the target in Options to make that permanent.");
                    parameter = substitute;
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

            return "The fan was placed. About the figures written onto it:\n\n  • "
                 + string.Join("\n  • ", problems.ToArray())
                 + "\n\nThe \"write to\" column in Options for this family is where these are set.";
        }

        /// <summary>
        /// A parameter of this fan that can actually take this figure: writable,
        /// of a fitting kind, and named like the thing it holds.
        /// </summary>
        private static Parameter Writable(FamilyInstance instance, QuantityInfo info)
        {
            var candidates = new List<Parameter>();
            try
            {
                foreach (Parameter parameter in instance.Parameters)
                {
                    if (parameter == null || parameter.Definition == null) continue;
                    if (parameter.IsReadOnly) continue;
                    if (!Quantities.AcceptsSpec(info, RevitUnits.SpecOf(parameter))) continue;
                    candidates.Add(parameter);
                }
            }
            catch { return null; }

            foreach (string hint in info.NameHints)
                foreach (Parameter parameter in candidates)
                    if (parameter.Definition.Name.IndexOf(hint, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        return parameter;

            return candidates.Count == 1 ? candidates[0] : null;
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
