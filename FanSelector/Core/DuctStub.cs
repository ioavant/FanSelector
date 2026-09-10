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

            ElementId systemTypeId = ResolveSystemType(doc, settings, outlet);
            if (systemTypeId == ElementId.InvalidElementId)
                return "The fan was placed, but the project has no duct system type, so no stub was grown.";

            ElementId ductTypeId = ResolveDuctType(doc, settings, outlet);
            if (ductTypeId == ElementId.InvalidElementId)
                return "The fan was placed, but the project has no round duct type to grow a stub with.";

            string trouble;
            Duct duct = CreateDuct(doc, systemTypeId, ductTypeId, outlet, mapping.StubLengthFt, out trouble);
            if (duct == null) return "The fan was placed, but the duct stub could not be created: " + trouble;

            doc.Regenerate();

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
                // Hosting the terminal ON the duct is what Revit's own "Air Terminal
                // on Duct" placement does; it cuts into the duct and connects.
                terminal = doc.Create.NewFamilyInstance(
                    open.Origin, closer, duct, StructuralType.NonStructural);
            }
            catch (Exception exception)
            {
                return "The stub was created, but the closer could not be placed on it: " + exception.Message;
            }

            if (terminal == null)
                return "The stub was created, but the closer could not be placed on it.";

            doc.Regenerate();

            var notes = new List<string>();
            string connectNote = EnsureConnected(terminal, duct);
            if (connectNote != null) notes.Add(connectNote);

            string flowNote = SetFlow(terminal, requestedAirFlow);
            if (flowNote != null) notes.Add(flowNote);

            return notes.Count == 0 ? null
                 : "The fan, its stub and the closer were placed, but: " + string.Join(" ", notes.ToArray());
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
        /// The system type for the stub: the user's choice if they made one,
        /// otherwise the one matching the fan connector's own system type — a
        /// supply connector gets a supply system — and failing that whatever the
        /// project has. Both enums spell these the same ("SupplyAir"), so they are
        /// matched by name.
        /// </summary>
        private static ElementId ResolveSystemType(Document doc, FanSettings settings, Connector outlet)
        {
            List<MechanicalSystemType> types;
            try
            {
                types = new FilteredElementCollector(doc)
                    .OfClass(typeof(MechanicalSystemType))
                    .Cast<MechanicalSystemType>()
                    .ToList();
            }
            catch { return ElementId.InvalidElementId; }

            if (types.Count == 0) return ElementId.InvalidElementId;

            if (!string.IsNullOrEmpty(settings.DuctSystemType))
            {
                MechanicalSystemType chosen = types.FirstOrDefault(
                    t => string.Equals(t.Name, settings.DuctSystemType, StringComparison.OrdinalIgnoreCase));
                if (chosen != null) return chosen.Id;
            }

            try
            {
                string wanted = outlet.DuctSystemType.ToString();
                MechanicalSystemType matching = types.FirstOrDefault(
                    t => string.Equals(t.SystemClassification.ToString(), wanted,
                                       StringComparison.OrdinalIgnoreCase));
                if (matching != null) return matching.Id;
            }
            catch { /* connector has no duct system type - fall through */ }

            return types[0].Id;
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
        /// itself to it, so the stub is the right diameter without being told.
        ///
        /// Which way the connector's Z axis points is the one thing not worth
        /// trusting blind, so if the first direction is refused the opposite one is
        /// tried before giving up.
        /// </summary>
        private static Duct CreateDuct(Document doc, ElementId systemTypeId, ElementId ductTypeId,
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
                    Duct duct = Duct.Create(doc, systemTypeId, ductTypeId, start, end);
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
        /// Hosting the terminal on the duct normally connects it. When it has not,
        /// the two coincident connectors are joined by hand — without that the
        /// terminal is decoration and carries no flow.
        /// </summary>
        private static string EnsureConnected(FamilyInstance terminal, Duct duct)
        {
            try
            {
                List<Connector> onTerminal = new List<Connector>();
                MEPModel model = terminal.MEPModel;
                if (model == null || model.ConnectorManager == null)
                    return "the closer has no duct connector, so it is not joined to the stub.";

                foreach (Connector connector in model.ConnectorManager.Connectors)
                    onTerminal.Add(connector);

                if (onTerminal.Any(c => c.IsConnected)) return null;

                Connector free = OpenEnd(duct);
                if (free == null) return null;   // the stub is fully connected already

                Connector nearest = onTerminal
                    .OrderBy(c => c.Origin.DistanceTo(free.Origin))
                    .FirstOrDefault();
                if (nearest == null) return null;

                nearest.ConnectTo(free);
                return null;
            }
            catch (Exception exception)
            {
                return "the closer could not be joined to the stub (" + exception.Message + ").";
            }
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
