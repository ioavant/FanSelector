using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>
    /// Translates the tokens in a type-catalogue header — "CUBIC_METERS_PER_HOUR",
    /// "HVAC_AIR_FLOW" — into the Revit units and specs they name.
    ///
    /// Revit can in principle be asked (UnitUtils.GetTypeCatalogStringForUnit /
    /// ForSpec), and walking UnitTypeId and SpecTypeId to build these tables
    /// automatically would need no maintenance. It is deliberately NOT done that
    /// way: those calls go straight into native code, and one that misbehaves
    /// raises an AccessViolationException, which is a corrupted-state exception
    /// that `catch` does not intercept — it would take Revit down with it. Six
    /// hundred such calls every time the Options dialog opens is not a risk worth
    /// the saved typing. A table cannot crash.
    ///
    /// A token that is not listed here resolves to null, and the column is then
    /// treated as a plain number and flagged in the dialog, so an unknown unit
    /// shows up as something to look at rather than as a silently wrong value.
    /// </summary>
    internal static class CatalogTokens
    {
        // Tokens whose value needs no conversion: a general or fixed number is
        // already in the units Revit stores it in.
        private static readonly HashSet<string> Unitless =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GENERAL", "FIXED", "" };

        private static Dictionary<string, ForgeTypeId> _units;
        private static Dictionary<string, ForgeTypeId> _specs;

        /// <summary>The unit a token names; null when it needs no conversion or is unknown.</summary>
        public static ForgeTypeId Unit(string token)
        {
            if (token == null) token = string.Empty;
            if (Unitless.Contains(token.Trim())) return null;
            if (_units == null) _units = BuildUnits();
            return Lookup(_units, token);
        }

        /// <summary>The spec a token names, or null when it is unknown or means "text".</summary>
        public static ForgeTypeId Spec(string token)
        {
            if (_specs == null) _specs = BuildSpecs();
            return Lookup(_specs, token);
        }

        /// <summary>
        /// True when a token is one this add-in understands — or one that needs no
        /// understanding. Used to warn about a column whose unit is a mystery.
        /// </summary>
        public static bool KnowsUnit(string token)
        {
            if (token == null) token = string.Empty;
            return Unitless.Contains(token.Trim()) || Unit(token) != null;
        }

        private static ForgeTypeId Lookup(Dictionary<string, ForgeTypeId> map, string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            ForgeTypeId found;
            return map.TryGetValue(token.Trim(), out found) ? found : null;
        }

        private static Dictionary<string, ForgeTypeId> BuildUnits()
        {
            return new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
            {
                // Length
                { "MILLIMETERS", UnitTypeId.Millimeters },
                { "CENTIMETERS", UnitTypeId.Centimeters },
                { "METERS", UnitTypeId.Meters },
                { "METERS_CENTIMETERS", UnitTypeId.MetersCentimeters },
                { "DECIMAL_FEET", UnitTypeId.Feet },
                { "FEET_FRACTIONAL_INCHES", UnitTypeId.FeetFractionalInches },
                { "DECIMAL_INCHES", UnitTypeId.Inches },
                { "FRACTIONAL_INCHES", UnitTypeId.FractionalInches },

                // Air flow
                { "CUBIC_METERS_PER_HOUR", UnitTypeId.CubicMetersPerHour },
                { "CUBIC_METERS_PER_SECOND", UnitTypeId.CubicMetersPerSecond },
                { "LITERS_PER_SECOND", UnitTypeId.LitersPerSecond },
                { "CUBIC_FEET_PER_MINUTE", UnitTypeId.CubicFeetPerMinute },
                { "CUBIC_FEET_PER_HOUR", UnitTypeId.CubicFeetPerHour },

                // Pressure
                { "PASCALS", UnitTypeId.Pascals },
                { "KILOPASCALS", UnitTypeId.Kilopascals },
                { "MEGAPASCALS", UnitTypeId.Megapascals },
                { "BARS", UnitTypeId.Bars },
                { "ATMOSPHERES", UnitTypeId.Atmospheres },
                { "POUNDS_FORCE_PER_SQUARE_INCH", UnitTypeId.PoundsForcePerSquareInch },
                { "INCHES_OF_WATER_60_DEGREES_FAHRENHEIT", UnitTypeId.InchesOfWater60DegreesFahrenheit },
                { "FEET_OF_WATER_39_2_DEGREES_FAHRENHEIT", UnitTypeId.FeetOfWater39_2DegreesFahrenheit },
                { "MILLIMETERS_OF_WATER_COLUMN", UnitTypeId.MillimetersOfWaterColumn },
                { "METERS_OF_WATER_COLUMN", UnitTypeId.MetersOfWaterColumn },
                { "MILLIMETERS_OF_MERCURY", UnitTypeId.MillimetersOfMercury },

                // Power
                { "WATTS", UnitTypeId.Watts },
                { "KILOWATTS", UnitTypeId.Kilowatts },
                { "HORSEPOWER", UnitTypeId.Horsepower },
                { "BRITISH_THERMAL_UNITS_PER_SECOND", UnitTypeId.BritishThermalUnitsPerSecond },
                { "BRITISH_THERMAL_UNITS_PER_HOUR", UnitTypeId.BritishThermalUnitsPerHour },
                { "VOLT_AMPERES", UnitTypeId.VoltAmperes },

                // Rotation, angle, ratio, mass, money
                { "REVOLUTIONS_PER_MINUTE", UnitTypeId.RevolutionsPerMinute },
                { "REVOLUTIONS_PER_SECOND", UnitTypeId.RevolutionsPerSecond },
                { "DEGREES", UnitTypeId.Degrees },
                { "RADIANS", UnitTypeId.Radians },
                { "PERCENTAGE", UnitTypeId.Percentage },
                { "KILOGRAMS_MASS", UnitTypeId.Kilograms },
                { "POUNDS_MASS", UnitTypeId.PoundsMass },
                { "CURRENCY", UnitTypeId.Currency }
            };
        }

        private static Dictionary<string, ForgeTypeId> BuildSpecs()
        {
            return new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
            {
                { "LENGTH", SpecTypeId.Length },
                { "AREA", SpecTypeId.Area },
                { "VOLUME", SpecTypeId.Volume },
                { "ANGLE", SpecTypeId.Angle },
                { "NUMBER", SpecTypeId.Number },
                { "MASS", SpecTypeId.Mass },
                { "FORCE", SpecTypeId.Force },
                { "CURRENCY", SpecTypeId.Currency },
                { "ANGULAR_SPEED", SpecTypeId.AngularSpeed },

                { "HVAC_AIR_FLOW", SpecTypeId.AirFlow },
                { "HVAC_AIRFLOW", SpecTypeId.AirFlow },
                { "HVAC_PRESSURE", SpecTypeId.HvacPressure },
                { "HVAC_POWER", SpecTypeId.HvacPower },
                { "HVAC_VELOCITY", SpecTypeId.HvacVelocity },
                { "HVAC_TEMPERATURE", SpecTypeId.HvacTemperature },

                { "PIPING_PRESSURE", SpecTypeId.PipingPressure },

                { "ELECTRICAL_POWER", SpecTypeId.ElectricalPower },
                { "ELECTRICAL_CURRENT", SpecTypeId.Current },
                { "ELECTRICAL_POTENTIAL", SpecTypeId.ElectricalPotential },
                { "ELECTRICAL_FREQUENCY", SpecTypeId.ElectricalFrequency },
                { "ELECTRICAL_APPARENT_POWER", SpecTypeId.ApparentPower }

                // OTHER and TEXT are deliberately absent: they mean "not a
                // measured quantity", which is exactly what a null spec says.
            };
        }
    }
}
