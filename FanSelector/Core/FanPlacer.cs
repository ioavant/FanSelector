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
        public static PlacementResult Place(UIDocument uidoc, FanCandidate candidate,
                                            FamilyMapping mapping, double offsetFt)
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
                    FamilySymbol symbol = Resolve(doc, candidate, mapping, out problem);
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

                    XYZ target = new XYZ(point.X, point.Y, point.Z + offsetFt);
                    FamilyInstance instance = doc.Create.NewFamilyInstance(
                        target, symbol, level, StructuralType.NonStructural);

                    string note = WriteFigures(instance, mapping, candidate);

                    transaction.Commit();
                    return new PlacementResult { Placed = true, Message = note };
                }
                catch (Exception exception)
                {
                    transaction.RollBack();
                    return PlacementResult.Fail("The fan could not be placed: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// The type to place. A catalogue holds every type the manufacturer offers
        /// and a project normally has only a handful loaded, so a row the user
        /// picked may still have to be brought in — LoadFamilySymbol pulls in that
        /// ONE type rather than all of them.
        /// </summary>
        private static FamilySymbol Resolve(Document doc, FanCandidate candidate,
                                            FamilyMapping mapping, out string problem)
        {
            problem = null;
            if (candidate.Symbol != null) return candidate.Symbol;

            string familyFile = FamilyFileFor(mapping);
            if (familyFile == null)
            {
                problem = "The type \"" + candidate.TypeName + "\" is not loaded in this project, and the "
                        + "family file it should come from was not found beside the catalogue.\n\n"
                        + "Revit expects a type catalogue and its family to sit together under the same "
                        + "name. Load the type by hand, or put the .rfa next to:\n" + mapping.CatalogPath;
                return null;
            }

            FamilySymbol symbol;
            try
            {
                if (!doc.LoadFamilySymbol(familyFile, candidate.TypeName, new LoadOptions(), out symbol)
                    || symbol == null)
                {
                    problem = "Revit did not load the type \"" + candidate.TypeName + "\" from:\n"
                            + familyFile + "\n\nThe catalogue and the family file may be out of step.";
                    return null;
                }
            }
            catch (Exception exception)
            {
                problem = "The type \"" + candidate.TypeName + "\" could not be loaded from:\n"
                        + familyFile + "\n\n" + exception.Message;
                return null;
            }

            return symbol;
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

        /// <summary>
        /// Loading one type must not quietly rewrite types already in the model:
        /// somebody may have adjusted them, and this add-in is not the authority
        /// on that.
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
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
