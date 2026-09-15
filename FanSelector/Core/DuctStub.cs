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

            // Noted before anything is created: once the stub is on, this is how
            // the fan's own joint is found again to check it survived, and the
            // diameter it should have handed on.
            int outletId = outlet.Id;
            XYZ outletOrigin = outlet.Origin;
            double outletDiameter = DiameterOf(outlet);

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
                // Hosted on the duct on purpose: this IS Revit's "Air Terminal on
                // Duct" placement, and it is what makes the terminal adopt the
                // duct's size instead of keeping the family's default. Nothing
                // else does that — sizing it afterwards means guessing which
                // parameter drives the connector, which is how it ended up at the
                // family's default 900 mm regardless of the fan.
                //
                // What hosting does NOT get right is the orientation for capping
                // an open end, so that is corrected below, by hand.
                terminal = doc.Create.NewFamilyInstance(
                    open.Origin, closer, duct, StructuralType.NonStructural);
            }
            catch (Exception exception)
            {
                return "The stub was created, but the closer could not be placed: " + exception.Message;
            }

            if (terminal == null)
                return "The stub was created, but the closer could not be placed.";

            doc.Regenerate();

            var notes = new List<string>();

            // Said before anything else, because if the stub itself came out the
            // wrong size then nothing downstream of it can be right, and the
            // difference between "the duct is wrong" and "the closer is wrong" is
            // the whole diagnosis.
            double stubDiameter = DiameterOf(ConnectorById(duct, FarEndId(duct, outletOrigin)));
            if (outletDiameter > 0.0 && stubDiameter > 0.0
                && Math.Abs(stubDiameter - outletDiameter) > 1e-6)
                notes.Add("the stub came out " + Size(doc, stubDiameter) + " where the fan's connector is "
                          + Size(doc, outletDiameter) + ", so it did not take the connector's size.");

            string fitNote = FitToEnd(doc, terminal, duct, outletOrigin, mapping);
            if (fitNote != null) notes.Add(fitNote);

            string flowNote = SetFlow(terminal, requestedAirFlow);
            if (flowNote != null) notes.Add(flowNote);

            // The whole point is a stub ON the fan. Checked rather than assumed,
            // because moving anything in a connected network moves the rest of it.
            if (!StillJoined(fan, outletId))
                notes.Add("the stub came away from the fan's connector while the closer was fitted.");

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
        private static string FitToEnd(Document doc, FamilyInstance terminal, Duct duct, XYZ fanOrigin,
                                       FamilyMapping mapping)
        {
            // Hosting has by now given the terminal the duct's size, which is the
            // only reason it was hosted. From here it has to come off: nothing may
            // be moved while joined to something else, because Revit drags the
            // connected network along, and rotating a terminal still snapped to
            // the stub swings the stub — and the fan at its far end — out of
            // place. The size stays; it is a value on the instance, not a
            // consequence of the joint.
            Detach(doc, terminal);

            int ownId, endId;
            if (!FreeConnectorId(terminal, out ownId))
                return "the closer has no free duct connector, so it is not joined to the stub.";

            // The end to cap is the one AWAY from the fan. Picking "the first
            // unconnected one" is a coin toss whenever the stub is not yet joined
            // to the fan, and getting it wrong caps the wrong end.
            endId = FarEndId(duct, fanOrigin);
            if (endId < 0)
                return "the stub's open end could not be found, so the closer is not joined to it.";

            // Size before alignment: changing it moves the family's own geometry,
            // so there is no point aligning a connector that is about to shift.
            string sizeNote = MatchSize(doc, terminal, ownId, duct, endId, mapping);

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
                return sizeNote;
            }
            catch (Exception exception)
            {
                return Join(sizeNote,
                    "the closer could not be aligned to the stub (" + exception.Message + ").");
            }
        }

        /// <summary>
        /// Give the closer the duct's diameter. The duct already has the fan
        /// connector's size, so this carries the fan's own outlet size all the way
        /// through to the terminal — which is the point of a closer.
        ///
        /// Which family parameter drives the connector is not guessed from its
        /// name. The connector has a diameter right now, and the parameter that
        /// currently EQUALS it is the one driving it; that is a fact about this
        /// family rather than a hope about its naming. A name hint only breaks
        /// ties, and the result is verified by re-reading the connector.
        /// </summary>
        private static string MatchSize(Document doc, FamilyInstance terminal, int ownId,
                                        Duct duct, int endId, FamilyMapping mapping)
        {
            double target = DiameterOf(ConnectorById(duct, endId));
            double current = DiameterOf(ConnectorById(terminal, ownId));
            if (target <= 0.0 || current <= 0.0) return null;
            if (Math.Abs(target - current) < 1e-9) return null;   // already right

            // Told, not deduced. When the user has named the parameter, that is
            // the end of it — no evidence gathering, no name matching, no chance
            // of writing the duct's diameter into something unrelated.
            if (!string.IsNullOrEmpty(mapping.CloserSizeParam))
                return SetNamed(doc, terminal, ownId, mapping.CloserSizeParam, target);

            var lengths = new List<Parameter>();
            try
            {
                foreach (Parameter parameter in terminal.Parameters)
                {
                    if (parameter == null || parameter.Definition == null) continue;
                    if (parameter.IsReadOnly || parameter.StorageType != StorageType.Double) continue;

                    ForgeTypeId spec = RevitUnits.SpecOf(parameter);
                    if (spec == null || spec.TypeId != SpecTypeId.Length.TypeId) continue;
                    lengths.Add(parameter);
                }
            }
            catch { return null; }

            // ONLY a parameter that currently equals the connector's diameter.
            // There is no name-based fallback: a parameter called "Size" or
            // "Duct" that does not hold the diameter is not evidence of anything,
            // and writing the duct's diameter into it produces a closer at some
            // unrelated fixed size — which is precisely what a name-matched
            // fallback here did.
            List<Parameter> driving = lengths
                .Where(p => Math.Abs(p.AsDouble() - current) < 1e-7)
                .ToList();

            Parameter driver = Hinted(driving) ?? driving.FirstOrDefault();
            if (driver == null)
                return "the closer's connection size was left alone: its connector is "
                     + Size(doc, current) + " against the duct's " + Size(doc, target)
                     + ", and nothing writable on it holds that " + Size(doc, current)
                     + " for this to follow.\n\nName the parameter that sets its connection diameter under "
                     + "\"Connection size parameter\" in Options, and it will be set outright instead of "
                     + "looked for.\n\n" + WhatItHas(doc, terminal);

            string name = driver.Definition.Name;

            try { driver.Set(target); }
            catch (Exception exception)
            {
                return "the closer's connection size could not be set (" + exception.Message + ").";
            }

            doc.Regenerate();

            double now = DiameterOf(ConnectorById(terminal, ownId));
            if (Math.Abs(now - target) > 1e-6)
                return "\"" + name + "\" was set to " + Size(doc, target) + " on the closer, but its "
                     + "connector reads " + Size(doc, now) + " against the duct's " + Size(doc, target)
                     + ", so something else drives it.";

            return null;
        }

        /// <summary>
        /// Write the duct's diameter into the parameter the user named for it.
        /// Told, not deduced: no evidence gathering and no name matching, so it
        /// cannot end up writing the diameter into something unrelated.
        /// </summary>
        private static string SetNamed(Document doc, FamilyInstance terminal, int ownId,
                                       string parameterName, double target)
        {
            Parameter parameter;
            try { parameter = terminal.LookupParameter(parameterName); }
            catch { parameter = null; }

            if (parameter == null)
                return "the closer's size was left alone: it has no instance parameter called \""
                     + parameterName + "\". Check \"Connection size parameter\" in Options.";

            if (parameter.IsReadOnly)
                return "the closer's size was left alone: \"" + parameterName + "\" is read-only on the "
                     + "placed terminal, so it cannot be what sets the diameter.";

            if (parameter.StorageType != StorageType.Double)
                return "the closer's size was left alone: \"" + parameterName + "\" does not hold a length.";

            try { parameter.Set(target); }
            catch (Exception exception)
            {
                return "the closer's size could not be set through \"" + parameterName + "\" ("
                     + exception.Message + ").";
            }

            doc.Regenerate();

            double now = DiameterOf(ConnectorById(terminal, ownId));
            if (Math.Abs(now - target) > 1e-6)
                return "\"" + parameterName + "\" was set to " + Size(doc, target) + ", but the closer's "
                     + "connector reads " + Size(doc, now) + ", so that parameter is not what drives it.";

            return null;
        }

        /// <summary>
        /// The closer's own numeric parameters, so the right name can be picked
        /// out of the message rather than hunted for in the family.
        /// </summary>
        private static string WhatItHas(Document doc, FamilyInstance terminal)
        {
            var lines = new List<string>();
            try
            {
                foreach (Parameter parameter in terminal.Parameters)
                {
                    if (parameter == null || parameter.Definition == null) continue;
                    if (parameter.StorageType != StorageType.Double || !parameter.HasValue) continue;

                    lines.Add(parameter.Definition.Name + " = " + Size(doc, parameter.AsDouble())
                              + (parameter.IsReadOnly ? "  (read-only)" : string.Empty));
                }
            }
            catch { /* report whatever was collected */ }

            if (lines.Count == 0) return "The closer reports no numeric instance parameters.";
            return "Its numeric instance parameters are:\n  " + string.Join("\n  ", lines.ToArray());
        }

        /// <summary>A length in the project's own units, for saying what went wrong.</summary>
        private static string Size(Document doc, double feet)
        {
            try { return RevitUnits.Format(doc.GetUnits(), SpecTypeId.Length, feet); }
            catch { return feet.ToString("0.###") + " ft"; }
        }

        private static Parameter Hinted(List<Parameter> parameters)
        {
            string[] hints = { "diameter", "size", "neck", "duct", "connection" };
            foreach (string hint in hints)
                foreach (Parameter parameter in parameters)
                    if (parameter.Definition.Name.IndexOf(hint, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        return parameter;
            return null;
        }

        private static double DiameterOf(Connector connector)
        {
            if (connector == null) return 0.0;
            try
            {
                return connector.Shape == ConnectorProfileType.Round ? connector.Radius * 2.0 : 0.0;
            }
            catch { return 0.0; }
        }

        private static string Join(string first, string second)
        {
            if (string.IsNullOrEmpty(first)) return second;
            if (string.IsNullOrEmpty(second)) return first;
            return first + " " + second;
        }

        /// <summary>
        /// Break every joint an element has, so it can be moved on its own.
        /// </summary>
        private static void Detach(Document doc, Element element)
        {
            try
            {
                foreach (Connector own in Connectors(element))
                {
                    if (!own.IsConnected) continue;

                    // Collected first: disconnecting while enumerating AllRefs
                    // mutates what is being walked.
                    var joined = new List<Connector>();
                    foreach (Connector other in own.AllRefs)
                        if (other.Owner != null && other.Owner.Id != element.Id) joined.Add(other);

                    foreach (Connector other in joined)
                    {
                        try { own.DisconnectFrom(other); } catch { /* already apart */ }
                    }
                }
                doc.Regenerate();
            }
            catch { /* nothing joined, or nothing that can be parted */ }
        }

        /// <summary>
        /// The stub's connector farthest from the fan — the end to cap. Distance
        /// decides it rather than connection state, which is only trustworthy once
        /// the fan joint has actually been made.
        /// </summary>
        private static int FarEndId(Duct duct, XYZ fanOrigin)
        {
            int best = -1;
            double farthest = -1.0;
            foreach (Connector connector in Connectors(duct))
            {
                try
                {
                    double distance = connector.Origin.DistanceTo(fanOrigin);
                    if (distance > farthest) { farthest = distance; best = connector.Id; }
                }
                catch { /* skip a connector that will not report a position */ }
            }
            return best;
        }

        /// <summary>Whether the fan's own connector still has the stub on it.</summary>
        private static bool StillJoined(FamilyInstance fan, int outletId)
        {
            try
            {
                Connector outlet = ConnectorById(fan, outletId);
                return outlet != null && outlet.IsConnected;
            }
            catch { return false; }
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
