using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>
    /// Everything the add-in shows or reads is in the PROJECT's units. Values
    /// themselves never leave Revit's internal units: they are read from a
    /// parameter and compared against a target parsed with the same spec, so
    /// there is no conversion anywhere in this add-in — only formatting.
    /// </summary>
    internal static class RevitUnits
    {
        /// <summary>Format an internal-unit value the way this project would display it.</summary>
        public static string Format(Units units, ForgeTypeId spec, double internalValue)
        {
            if (units != null && spec != null && !spec.Empty())
            {
                try { return UnitFormatUtils.Format(units, spec, internalValue, false); }
                catch { /* spec has no format options - fall through */ }
            }
            return internalValue.ToString("0.###", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Parse what the user typed into an internal-unit value, accepting both a
        /// bare number in project units ("5000") and a qualified one ("3000 CFM").
        /// </summary>
        public static bool TryParse(Units units, ForgeTypeId spec, string text, out double internalValue)
        {
            internalValue = 0.0;
            if (string.IsNullOrEmpty(text)) return false;

            if (units != null && spec != null && !spec.Empty())
            {
                try { return UnitFormatUtils.TryParse(units, spec, text, out internalValue); }
                catch { /* spec cannot be parsed - fall through */ }
            }

            return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out internalValue)
                || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out internalValue);
        }

        /// <summary>The project's unit symbol for a spec ("m³/h", "Pa", "CFM"), or "".</summary>
        public static string Symbol(Units units, ForgeTypeId spec)
        {
            if (units == null || spec == null || spec.Empty()) return string.Empty;
            try
            {
                return LabelUtils.GetLabelForUnit(units.GetFormatOptions(spec).GetUnitTypeId());
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// A parameter's value as text, formatted in project units. AsValueString
        /// already honours the document's unit and rounding settings, which is
        /// exactly what a results grid should show.
        /// </summary>
        public static string Display(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue) return string.Empty;
            try
            {
                string text = parameter.AsValueString();
                if (!string.IsNullOrEmpty(text)) return text;
                if (parameter.StorageType == StorageType.String) return parameter.AsString() ?? string.Empty;
                if (parameter.StorageType == StorageType.Integer)
                    return parameter.AsInteger().ToString(CultureInfo.CurrentCulture);
                if (parameter.StorageType == StorageType.Double)
                    return parameter.AsDouble().ToString("0.###", CultureInfo.CurrentCulture);
            }
            catch { /* fall through */ }
            return string.Empty;
        }

        /// <summary>A parameter's numeric value in internal units, or null if it has none.</summary>
        public static double? Number(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue) return null;
            switch (parameter.StorageType)
            {
                case StorageType.Double: return parameter.AsDouble();
                case StorageType.Integer: return parameter.AsInteger();
                default: return null;
            }
        }

        /// <summary>The spec behind a parameter, or null when it has none.</summary>
        public static ForgeTypeId SpecOf(Parameter parameter)
        {
            if (parameter == null || parameter.Definition == null) return null;
            try
            {
                ForgeTypeId spec = parameter.Definition.GetDataType();
                return spec == null || spec.Empty() ? null : spec;
            }
            catch { return null; }
        }
    }
}
