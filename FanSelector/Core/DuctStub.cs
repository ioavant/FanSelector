using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Structure;

namespace FanSelector.Core
{
    /// <summary>
    /// The optional extra a placed fan can grow: a short round duct off one of its
    /// connectors, capped by an air terminal that carries the air flow the user
    /// searched for.
    ///
    /// Why it exists: a fan on its own has nowhere to put the duty. Hanging the
    /// requested air flow on a terminal at a stub gives the model a real, connected
    /// flow figure straight away, which is what makes the fan show up correctly in
    /// schedules and system calculations instead of as an unconnected object.
    ///
    /// All of it runs inside the caller's transaction, so a fan and its stub are
    /// one undo step, and a failure half-way leaves nothing behind.
    /// </summary>
    internal static class DuctStub
    {
        /// <summary>
        /// Grow the stub. Returns null when everything worked, or a description of
        /// what did not — the fan itself is already placed by then, so a problem
        /// here is worth reporting but never worth rolling the fan back.
        /// </summary>
        public static string Build(Document doc, FamilyInstance fan, FamilyMapping mapping,
                                   FanSettings settings, double requestedAirFlow)
        {
            Connector outlet = PickConnector(fan);
            if (outlet == null)
                return "The fan was placed, but no free round duct connector was found on it, so no stub "
                     + "was grown.";

            ElementId ductTypeId = ResolveDuctType(doc, settings, outlet);
            if (ductTypeId == ElementId.InvalidElementId)
                return "The fan was placed, but the project has no duct type of the right shape to grow a "
                     + "stub with.";

            ElementId levelId = ResolveLevel(doc, fan);
            if (levelId == ElementId.InvalidElementId)
                return "The fan was placed, but no level could be found to host a duct stub on.";

            string trouble;
            Duct duct = CreateDuct(doc, ductTypeId, levelId, outlet, mapping.StubLengthFt, out trouble);
            if (duct == null) return "The fan was placed, but the duct stub could not be created: " + trouble;

            doc.Regenerate();

            // The system follows the connector the stub grew from. Only override
            // that if the user actually asked for a particular system type.
            ApplySystemType(doc, duct, settings);

            Connector open = OpenEnd(duct);
            if (open == null)
                return "The stub was created, but its open end could not be found, so no closer was placed.";

            FamilySymbol closer = CloserSymbol(doc, mapping);
            if (closer == null)
                return "The stub was created, but the air terminal family \"" + mapping.CloserFamily
                     + "\" is not loaded in this project, so the stub was left open. Load it and the next "
                     + "fan will be capped.";

            if (!closer.IsActive)
            {
                closer.Activate();
                doc.Regenerate();
            }

            FamilyInstance terminal;
            try
            {
                // Placed free rather than hosted on the duct. Hosting lets Revit
                // decide the orientation, and what it decides is a terminal
                // tapping into the duct's SIDE — square to the run. Capping the
                // open end wants the terminal coaxial with it, so it is placed
                // loose and then aligned onto the connector by hand.
                terminal = doc.Create.NewFamilyInstance(
                    open.Origin, closer, StructuralType.NonStructural);
            }
            catch (Exception exception)
            {
                return "The stub was created, but the closer could not be placed: " + exception.Message;
            }

            if (terminal == null)
                return "The stub was created, but the closer could not be placed.";

            doc.Regenerate();

            var notes = new List<string>();
            string fitNote = FitToEnd(doc, terminal, duct);
            if (fitNote != null) notes.Add(fitNote);

            string flowNote = SetFlow(terminal, requestedAirFlow);
            if (flowNote != null) notes.Add(flowNote);

            return notes.Count == 0 ? null
                 : "The fan, its stub and the closer were placed, but: " + string.Join(" ", notes.ToArray());
        }

        /// <summary>
        /// Sit the closer squarely on the open end: turn it until its connector
        /// faces back down the duct, slide it until the two connectors are in the
        /// same place, and join them.
        ///
        /// Two connectors mate when their origins coincide and their axes are
        /// OPPOSITE — each points into the other — which is why the wanted
        /// direction is the duct connector's axis negated.
        /// </summary>
        private static string FitToEnd(Document doc, FamilyInstance terminal, Duct duct)
        {
            int ownId, endId;
            if (!FreeConnectorId(terminal, out ownId))
                return "the closer has no free duct connector, so it is not joined to the stub.";
            if (!FreeConnectorId(duct, out endId))
                return "the stub's open end could not be found again, so the closer is not joined to it.";

            try
            {
                XYZ wanted = ConnectorById(duct, endId).CoordinateSystem.BasisZ.Negate();
                XYZ facing = ConnectorById(terminal, ownId).CoordinateSystem.BasisZ;

                double angle = facing.AngleTo(wanted);
                if (angle > 1e-9)
                {
                    XYZ axis = facing.CrossProduct(wanted);
                    // Exactly opposite directions leave no cross product to turn
                    // about; any perpendicular axis does the half turn.
                    if (axis.GetLength() < 1e-9) axis = AnyPerpendicular(facing);

                    ElementTransformUtils.RotateElement(doc, terminal.Id,
                        Line.CreateUnbound(ConnectorById(terminal, ownId).Origin, axis.Normalize()), angle);
                    doc.Regenerate();
                }

                // Re-read the connectors: both moved with their elements.
                XYZ shift = ConnectorById(duct, endId).Origin - ConnectorById(terminal, ownId).Origin;
                if (shift.GetLength() > 1e-9)
                {
                    ElementTransformUtils.MoveElement(doc, terminal.Id, shift);
                    doc.Regenerate();
                }

                Connector own = ConnectorById(terminal, ownId);
                Connector end = ConnectorById(duct, endId);
                if (!own.IsConnected && !end.IsConnected) own.ConnectTo(end);
                return null;
            }
            catch (Exception exception)
            {
                return "the closer could not be aligned to the stub (" + exception.Message + ").";
            }
        }

        private static Connector ConnectorById(Element owner, int id)
        {
            foreach (Connector connector in Connectors(owner))
                if (connector.Id == id) return connector;
            return null;
        }

        /// <summary>An unconnected connector's id, which survives the element moving.</summary>
        private static bool FreeConnectorId(Element owner, out int id)
        {
            id = -1;
            foreach (Connector connector in Connectors(owner))
            {
                try { if (connector.IsConnected) continue; }
                catch { continue; }
                id = connector.Id;
                return true;
            }
            return false;
        }

        private static IEnumerable<Connector> Connectors(Element owner)
        {
            ConnectorManager manager = null;
            try
            {
                FamilyInstance instance = owner as FamilyInstance;
                if (instance != null && instance.MEPModel != null) manager = instance.MEPModel.ConnectorManager;
                MEPCurve curve = owner as MEPCurve;
                if (curve != null) manager = curve.ConnectorManager;
            }
            catch { yield break; }

            if (manager == null) yield break;
            foreach (Connector connector in manager.Connectors) yield return connector;
        }

        private static XYZ AnyPerpendicular(XYZ direction)
        {
            XYZ candidate = Math.Abs(direction.X) < 0.9 ? XYZ.BasisX : XYZ.BasisY;
            return direction.CrossProduct(candidate);
        }

        /// <summary>
        /// The connector to grow from. The discharge side is preferred — that is
        /// the end a duct run normally leaves by — then the intake, then anything
        /// free and round.
        /// </summary>
        private static Connector PickConnector(FamilyInstance fan)
        {
            List<Connector> usable = FreeRoundConnectors(fan);
            if (usable.Count == 0) return null;

            Connector outward = usable.FirstOrDefault(c => Is(c, FlowDirectionType.Out));
            if (outward != null) return outward;

            Connector inward = usable.FirstOrDefault(c => Is(c, FlowDirectionType.In));
            return inward ?? usable[0];
        }

        private static bool Is(Connector connector, FlowDirectionType direction)
        {
            try { return connector.Direction == direction; }
            catch { return false; }
        }

        private static List<Connector> FreeRoundConnectors(FamilyInstance fan)
        {
            var usable = new List<Connector>();
            ConnectorSet set;
            try
            {
                MEPModel model = fan.MEPModel;
                if (model == null || model.ConnectorManager == null) return usable;
                set = model.ConnectorManager.Connectors;
            }
            catch { return usable; }

            foreach (Connector connector in set)
            {
                try
                {
                    // Shape throws on connectors of other domains (an electrical
                    // one, say), which is exactly the filter wanted here.
                    if (connector.Shape != ConnectorProfileType.Round) continue;
                    if (connector.IsConnected) continue;
                    usable.Add(connector);
                }
                catch { /* not a duct connector - skip */ }
            }
            return usable;
        }

        /// <summary>
        /// The level to hang the stub on: the fan's own, so the two live together
        /// in schedules and view ranges. Any level will do as a fallback — the
        /// duct's geometry comes from the connector either way.
        /// </summary>
        private static ElementId ResolveLevel(Document doc, FamilyInstance fan)
        {
            try
            {
                if (fan.LevelId != null && fan.LevelId != ElementId.InvalidElementId) return fan.LevelId;
            }
            catch { /* fall through */ }

            try
            {
                Level any = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .FirstOrDefault();
                return any == null ? ElementId.InvalidElementId : any.Id;
            }
            catch { return ElementId.InvalidElementId; }
        }

        /// <summary>
        /// Put the stub on the system type the user picked, if they picked one.
        /// Growing from a connector already lands it in that connector's system,
        /// which is the right answer nearly always — so this is an override, not
        /// a requirement, and a refusal is not worth troubling anyone with.
        /// </summary>
        private static void ApplySystemType(Document doc, Duct duct, FanSettings settings)
        {
            if (string.IsNullOrEmpty(settings.DuctSystemType)) return;

            try
            {
                MechanicalSystemType chosen = new FilteredElementCollector(doc)
                    .OfClass(typeof(MechanicalSystemType))
                    .Cast<MechanicalSystemType>()
                    .FirstOrDefault(t => string.Equals(t.Name, settings.DuctSystemType,
                                                       StringComparison.OrdinalIgnoreCase));
                if (chosen == null) return;

                Parameter parameter = duct.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                if (parameter != null && !parameter.IsReadOnly) parameter.Set(chosen.Id);
            }
            catch { /* the connector's own system stands */ }
        }

        /// <summary>
        /// The duct type for the stub. Shape matters and is not negotiable: a
        /// round connector cannot start a rectangular duct, and Duct.Create
        /// rejects the type outright rather than adapting — which is what "the
        /// duct type ductTypeId is not valid duct type" means. So the project's
        /// first duct type is not good enough; it has to be one of the right shape.
        /// </summary>
        private static ElementId ResolveDuctType(Document doc, FanSettings settings, Connector outlet)
        {
            List<DuctType> types;
            try
            {
                types = new FilteredElementCollector(doc)
                    .OfClass(typeof(DuctType))
                    .Cast<DuctType>()
                    .ToList();
            }
            catch { return ElementId.InvalidElementId; }

            if (types.Count == 0) return ElementId.InvalidElementId;

            ConnectorProfileType shape;
            try { shape = outlet.Shape; }
            catch { shape = ConnectorProfileType.Round; }

            List<DuctType> fitting = types.Where(t => ShapeOf(t) == shape).ToList();

            if (!string.IsNullOrEmpty(settings.DuctType))
            {
                // An explicit choice still has to be able to carry the connector.
                DuctType chosen = fitting.FirstOrDefault(
                    t => string.Equals(t.Name, settings.DuctType, StringComparison.OrdinalIgnoreCase));
                if (chosen != null) return chosen.Id;
            }

            return fitting.Count > 0 ? fitting[0].Id : ElementId.InvalidElementId;
        }

        private static ConnectorProfileType ShapeOf(DuctType type)
        {
            try { return type.Shape; }
            catch { return ConnectorProfileType.Invalid; }
        }

        /// <summary>
        /// Duct.Create from a connector inherits the connector's size and connects
        /// itself to it, so the stub is the right diameter without being told, and
        /// it joins the system the connector already belongs to.
        ///
        /// The argument order matters and is not the one the six-argument overload
        /// uses: from a connector it is (ductTypeId, levelId), with no system type
        /// at all. Passing a system type first is what produced "the duct type
        /// ductTypeId is not valid duct type" — the call was handed a
        /// MechanicalSystemType where a DuctType belongs.
        ///
        /// Which way the connector's Z axis points is the one thing not worth
        /// trusting blind, so if the first direction is refused the opposite one is
        /// tried before giving up.
        /// </summary>
        private static Duct CreateDuct(Document doc, ElementId ductTypeId, ElementId levelId,
                                       Connector start, double lengthFt, out string trouble)
        {
            trouble = null;
            if (lengthFt <= 0.0) lengthFt = FamilyMapping.DefaultStubLengthMm / 304.8;

            XYZ axis;
            try { axis = start.CoordinateSystem.BasisZ; }
            catch (Exception exception) { trouble = exception.Message; return null; }

            foreach (XYZ direction in new[] { axis, axis.Negate() })
            {
                try
                {
                    XYZ end = start.Origin + direction.Normalize().Multiply(lengthFt);
                    Duct duct = Duct.Create(doc, ductTypeId, levelId, start, end);
                    if (duct != null) return duct;
                }
                catch (Exception exception) { trouble = exception.Message; }
            }

            return null;
        }

        /// <summary>The stub's far end — the one Duct.Create did not connect to the fan.</summary>
        private static Connector OpenEnd(Duct duct)
        {
            try
            {
                foreach (Connector connector in duct.ConnectorManager.Connectors)
                    if (!connector.IsConnected) return connector;
            }
            catch { /* fall through */ }
            return null;
        }

        private static FamilySymbol CloserSymbol(Document doc, FamilyMapping mapping)
        {
            string wanted = string.IsNullOrEmpty(mapping.CloserFamily)
                ? FamilyMapping.DefaultCloserFamily : mapping.CloserFamily;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family != null &&
                                         string.Equals(s.Family.Name, wanted,
                                                       StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        /// <summary>
        /// The air flow the user asked for, put on the terminal. Deliberately the
        /// SEARCHED figure rather than the catalogue's: the catalogue value is the
        /// fan's rating at that duty, while what the system should carry is the
        /// duty itself.
        /// </summary>
        private static string SetFlow(FamilyInstance terminal, double requestedAirFlow)
        {
            if (requestedAirFlow <= 0.0) return null;

            Parameter flow;
            try { flow = terminal.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM); }
            catch { flow = null; }

            if (flow == null) return "the closer has no Flow parameter, so the air flow was not set on it.";
            if (flow.IsReadOnly)
                return "the closer's Flow is read-only - it is calculated from the system rather than set, "
                     + "so the air flow was not written.";

            try
            {
                flow.Set(requestedAirFlow);
                return null;
            }
            catch (Exception exception)
            {
                return "the air flow could not be set on the closer (" + exception.Message + ").";
            }
        }
    }
}
