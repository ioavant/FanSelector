using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>
    /// The performance figures Fan Selector understands. Nothing about them is
    /// tied to a particular family: which parameter carries each one is the
    /// user's decision, recorded in <see cref="FamilyMapping"/>.
    /// </summary>
    internal enum FanQuantity
    {
        AirFlow,
        Pressure,
        Power,
        Speed,
        Sfp,
        SoundPower
    }

    /// <summary>
    /// What the mapping dialog needs to know about one quantity: how to name it,
    /// which parameters may legitimately carry it, and how to guess.
    /// </summary>
    internal class QuantityInfo
    {
        public FanQuantity Quantity { get; private set; }
        public string DisplayName { get; private set; }
        public string ColumnHeader { get; private set; }

        /// <summary>
        /// Required quantities are the two the search filters on. A mapping
        /// without them cannot be saved.
        /// </summary>
        public bool Required { get; private set; }

        /// <summary>
        /// Specs a parameter may have to be offered for this quantity by default.
        /// </summary>
        public ForgeTypeId[] Specs { get; private set; }

        /// <summary>Lower-case fragments that make a parameter name a likely match.</summary>
        public string[] NameHints { get; private set; }

        public QuantityInfo(FanQuantity quantity, string displayName, string columnHeader,
                            bool required, ForgeTypeId[] specs, string[] nameHints)
        {
            Quantity = quantity;
            DisplayName = displayName;
            ColumnHeader = columnHeader;
            Required = required;
            Specs = specs;
            NameHints = nameHints;
        }
    }

    internal static class Quantities
    {
        public static readonly QuantityInfo AirFlow = new QuantityInfo(
            FanQuantity.AirFlow, "Air flow", "Air flow", true,
            new[] { SpecTypeId.AirFlow },
            new[] { "airflow", "air flow", "flow", "volume", "cfm", "расход" });

        public static readonly QuantityInfo Pressure = new QuantityInfo(
            FanQuantity.Pressure, "Pressure", "Pressure", true,
            new[] { SpecTypeId.HvacPressure, SpecTypeId.PipingPressure },
            new[] { "pressure", "static", "total", "давление" });

        public static readonly QuantityInfo Power = new QuantityInfo(
            FanQuantity.Power, "Motor power", "Power", false,
            new[] { SpecTypeId.ElectricalPower, SpecTypeId.HvacPower },
            new[] { "motorpower", "motor power", "power", "motor", "мощность" });

        public static readonly QuantityInfo Speed = new QuantityInfo(
            FanQuantity.Speed, "Speed", "Speed", false,
            new[] { SpecTypeId.AngularSpeed, SpecTypeId.Number, SpecTypeId.Int.Integer },
            new[] { "rpm", "speed", "rotation", "обороты" });

        // PowerPerFlow is Revit's own specific-fan-power spec (W per unit flow);
        // Factor / Number / Efficacy cover families that store a plain efficiency.
        public static readonly QuantityInfo Sfp = new QuantityInfo(
            FanQuantity.Sfp, "Efficiency / SFP", "SFP", false,
            new[] { SpecTypeId.PowerPerFlow, SpecTypeId.Efficacy, SpecTypeId.Factor, SpecTypeId.Number },
            new[] { "sfp", "specific fan power", "efficiency", "efficacy", "кпд" });

        // Revit has no sound-power spec, so this is a plain number in every
        // family that carries it at all.
        public static readonly QuantityInfo SoundPower = new QuantityInfo(
            FanQuantity.SoundPower, "Sound power", "Sound", false,
            new[] { SpecTypeId.Number, SpecTypeId.Int.Integer },
            new[] { "sound", "noise", "lwa", "lpa", "db", "шум", "звук" });

        /// <summary>All quantities, in the order the mapping dialog shows them.</summary>
        public static readonly QuantityInfo[] All =
            { AirFlow, Pressure, Power, Speed, Sfp, SoundPower };

        public static QuantityInfo Of(FanQuantity quantity)
        {
            foreach (QuantityInfo info in All)
                if (info.Quantity == quantity) return info;
            throw new ArgumentOutOfRangeException("quantity");
        }

        /// <summary>
        /// True if <paramref name="spec"/> is one this quantity accepts. Compared
        /// by TypeId string: ForgeTypeId equality is reference-safe but the string
        /// is what actually identifies the spec.
        /// </summary>
        public static bool AcceptsSpec(QuantityInfo info, ForgeTypeId spec)
        {
            if (spec == null || spec.Empty()) return false;
            foreach (ForgeTypeId candidate in info.Specs)
                if (candidate != null && candidate.TypeId == spec.TypeId) return true;
            return false;
        }
    }
}
