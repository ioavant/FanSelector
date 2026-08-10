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

        public static PlacementResult Ok() { return new PlacementResult { Placed = true }; }
        public static PlacementResult Cancel() { return new PlacementResult { Cancelled = true }; }
        public static PlacementResult Fail(string message) { return new PlacementResult { Message = message }; }
    }

    internal static class FanPlacer
    {
        /// <summary>
        /// Ask for a point and put the chosen fan type there.
        ///
        /// The point is picked BEFORE the transaction opens. Picking is a modal
        /// user interaction and has no business running inside an open
        /// transaction — and pressing Esc during the pick is a cancellation, not
        /// an error.
        /// </summary>
        public static PlacementResult Place(UIDocument uidoc, FanCandidate candidate,
                                            FamilyMapping mapping, double offsetFt)
        {
            if (uidoc == null || candidate == null || candidate.Symbol == null)
                return PlacementResult.Fail("Nothing to place.");

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
                    FamilySymbol symbol = candidate.Symbol;
                    if (!symbol.IsActive)
                    {
                        symbol.Activate();
                        doc.Regenerate();
                    }

                    XYZ target = new XYZ(point.X, point.Y, point.Z + offsetFt);
                    FamilyInstance instance = doc.Create.NewFamilyInstance(
                        target, symbol, level, StructuralType.NonStructural);

                    string note = WriteInstanceAirFlow(instance, mapping, candidate);

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
        /// Optional: copy the selected type's air flow onto an instance parameter.
        /// Families that drive a duct connector from an instance override need
        /// this; most do not, which is why it is a mapping the user opts into.
        /// Both values are in internal units, so nothing is converted.
        /// </summary>
        private static string WriteInstanceAirFlow(FamilyInstance instance, FamilyMapping mapping,
                                                   FanCandidate candidate)
        {
            if (instance == null || mapping == null || string.IsNullOrEmpty(mapping.InstanceAirFlow))
                return null;

            Parameter parameter;
            try { parameter = instance.LookupParameter(mapping.InstanceAirFlow); }
            catch { parameter = null; }

            if (parameter == null)
                return "The fan was placed, but it has no instance parameter called \"" +
                       mapping.InstanceAirFlow + "\", so the air flow was not written to it.";

            if (parameter.IsReadOnly)
                return "The fan was placed, but \"" + mapping.InstanceAirFlow + "\" is read-only on the instance.";

            try
            {
                if (parameter.StorageType == StorageType.Double) parameter.Set(candidate.AirFlow);
                else if (parameter.StorageType == StorageType.Integer)
                    parameter.Set((int)Math.Round(candidate.AirFlow));
                else return "The fan was placed, but \"" + mapping.InstanceAirFlow +
                            "\" is not a numeric parameter, so the air flow was not written to it.";
            }
            catch (Exception exception)
            {
                return "The fan was placed, but the air flow could not be written to \"" +
                       mapping.InstanceAirFlow + "\": " + exception.Message;
            }

            return null;
        }

        /// <summary>
        /// The level to host the instance on: the view's own level in a plan, and
        /// otherwise the nearest level at or below the picked point. The original
        /// version read ActiveView.GenLevel unguarded, which is null in every 3D
        /// view and section.
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
