using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    internal enum FanSort
    {
        BestMatch,
        LowestSfp,
        LowestSound,
        LowestPower
    }

    internal class SearchResult
    {
        public List<FanCandidate> Candidates { get; set; }

        /// <summary>How many catalogue rows were looked at, matched or not.</summary>
        public int RowsExamined { get; set; }

        /// <summary>How many of the matches are not loaded in this project yet.</summary>
        public int NotLoaded { get; set; }

        /// <summary>
        /// Why an empty result is empty, when the reason is something other than
        /// "nothing is close enough". Null when there is nothing to explain.
        /// </summary>
        public string Note { get; set; }

        public SearchResult()
        {
            Candidates = new List<FanCandidate>();
        }
    }

    /// <summary>
    /// The selection itself: read the type catalogue through the user's column
    /// mapping, keep the rows within tolerance of the requested duty, and rank
    /// them.
    ///
    /// The catalogue is the database. Nothing is read from the project except
    /// which types happen to be loaded already, and that only decides whether
    /// placing a row has to load it first.
    /// </summary>
    internal static class FanSearch
    {
        /// <summary>
        /// The spec of the catalogue column a quantity is read from, so the caller
        /// can format and parse in the units that column declares. Null when the
        /// quantity is unmapped or the catalogue cannot be read.
        /// </summary>
        public static ForgeTypeId SpecFor(FamilyMapping mapping, FanQuantity quantity)
        {
            if (mapping == null) return null;

            CatalogFile file = CatalogFile.For(mapping.CatalogPath);
            if (!file.IsUsable) return null;

            CatalogColumn column = file.Column(mapping.Column(quantity));
            return column == null ? null : column.Spec;
        }

        /// <summary>
        /// The Revit family type a catalogue line asks for. Falls back to the
        /// line's first cell, which is right for a catalogue whose designation IS
        /// the type name and wrong only for one that has a separate size column —
        /// which is what the mapping is for.
        /// </summary>
        public static string TypeNameFor(FamilyMapping mapping, CatalogRow row)
        {
            if (row == null) return null;
            if (mapping != null && !string.IsNullOrEmpty(mapping.TypeColumn))
            {
                string named = row.Text(mapping.TypeColumn);
                if (!string.IsNullOrEmpty(named)) return named;
            }
            return row.TypeName;
        }

        public static SearchResult Run(Document doc, FamilyMapping mapping,
                                       double targetAirFlow, double targetPressure,
                                       double tolerancePercent, FanSort sort)
        {
            var result = new SearchResult();
            if (mapping == null || !mapping.IsUsable)
            {
                result.Note = "This family has no catalogue file, or no air flow / pressure column mapped. "
                            + "Open Options to set it up.";
                return result;
            }

            CatalogFile file = CatalogFile.For(mapping.CatalogPath);
            if (!file.IsUsable)
            {
                result.Note = file.Problem;
                return result;
            }

            Dictionary<string, FamilySymbol> loaded = LoadedTypes(doc, mapping.FamilyName);
            Units units = doc.GetUnits();
            double tolerance = Math.Max(tolerancePercent, 0.0) / 100.0;
            int unreadable = 0;

            foreach (CatalogRow row in file.Rows)
            {
                result.RowsExamined++;

                double? airFlow = row.Number(mapping.AirFlowColumn);
                double? pressure = row.Number(mapping.PressureColumn);
                if (!airFlow.HasValue || !pressure.HasValue) { unreadable++; continue; }

                double deviation = Math.Max(Relative(airFlow.Value, targetAirFlow),
                                            Relative(pressure.Value, targetPressure));
                if (deviation > tolerance) continue;

                FamilySymbol symbol;
                loaded.TryGetValue(TypeNameFor(mapping, row) ?? string.Empty, out symbol);
                result.Candidates.Add(Build(row, symbol, mapping, file, units,
                                            airFlow.Value, pressure.Value, deviation));
            }

            result.NotLoaded = result.Candidates.Count(c => !c.IsLoaded);

            if (result.Candidates.Count == 0 && unreadable == result.RowsExamined && unreadable > 0)
                result.Note = "None of the " + unreadable + " rows in the catalogue has a number in both "
                            + "mapped columns. Check which columns are mapped in Options — \""
                            + mapping.AirFlowColumn + "\" and \"" + mapping.PressureColumn + "\" were used.";

            Sort(result.Candidates, sort);
            return result;
        }

        /// <summary>Types of the family already in this project, by type name.</summary>
        private static Dictionary<string, FamilySymbol> LoadedTypes(Document doc, string familyName)
        {
            var map = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilySymbol symbol in ParameterScanner.SymbolsOf(doc, familyName))
                if (!map.ContainsKey(symbol.Name)) map[symbol.Name] = symbol;
            return map;
        }

        private static FanCandidate Build(CatalogRow row, FamilySymbol symbol, FamilyMapping mapping,
                                          CatalogFile file, Units units,
                                          double airFlow, double pressure, double deviation)
        {
            var candidate = new FanCandidate
            {
                Designation = row.TypeName,
                TypeName = TypeNameFor(mapping, row),
                Symbol = symbol,
                Row = row,
                AirFlow = airFlow,
                Pressure = pressure,
                AirFlowText = Display(row, file, units, mapping.AirFlowColumn),
                PressureText = Display(row, file, units, mapping.PressureColumn),
                PowerText = Display(row, file, units, mapping.PowerColumn),
                SpeedText = Display(row, file, units, mapping.SpeedColumn),
                SfpText = Display(row, file, units, mapping.SfpColumn),
                SoundText = Display(row, file, units, mapping.SoundPowerColumn),
                PowerValue = row.Number(mapping.PowerColumn),
                SfpValue = row.Number(mapping.SfpColumn),
                SoundValue = row.Number(mapping.SoundPowerColumn),
                Deviation = deviation
            };

            foreach (FanQuantity quantity in Enum.GetValues(typeof(FanQuantity)).Cast<FanQuantity>())
            {
                double? value = row.Number(mapping.Column(quantity));
                if (value.HasValue) candidate.Values[quantity] = value.Value;
            }

            foreach (string extra in mapping.ExtraColumns)
                candidate.Extras.Add(row.Text(extra));

            return candidate;
        }

        /// <summary>
        /// A catalogue value the way the project would show it. Falls back to the
        /// file's own text for a column whose unit Revit does not recognise, which
        /// is better than pretending to know what it means.
        /// </summary>
        private static string Display(CatalogRow row, CatalogFile file, Units units, string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return string.Empty;

            CatalogColumn column = file.Column(columnName);
            double? value = row.Number(columnName);
            if (column == null || column.Spec == null || !value.HasValue) return row.Text(columnName);

            return RevitUnits.Format(units, column.Spec, value.Value);
        }

        /// <summary>
        /// Relative error against the requested value. A target of zero is not a
        /// meaningful duty, and dividing by it would rank everything as perfect,
        /// so it is treated as "no match".
        /// </summary>
        private static double Relative(double actual, double target)
        {
            if (Math.Abs(target) < 1e-9) return double.MaxValue;
            return Math.Abs(actual - target) / Math.Abs(target);
        }

        // Rows with no value for the sort key go last rather than first, which is
        // what ordering nulls naturally would do and is never what the user meant.
        private static void Sort(List<FanCandidate> candidates, FanSort sort)
        {
            switch (sort)
            {
                case FanSort.LowestSfp:
                    candidates.Sort((a, b) => Compare(a.SfpValue, b.SfpValue, a, b));
                    break;
                case FanSort.LowestSound:
                    candidates.Sort((a, b) => Compare(a.SoundValue, b.SoundValue, a, b));
                    break;
                case FanSort.LowestPower:
                    candidates.Sort((a, b) => Compare(a.PowerValue, b.PowerValue, a, b));
                    break;
                default:
                    candidates.Sort((a, b) => a.Deviation.CompareTo(b.Deviation));
                    break;
            }
        }

        private static int Compare(double? a, double? b, FanCandidate fa, FanCandidate fb)
        {
            if (a.HasValue && b.HasValue)
            {
                int byValue = a.Value.CompareTo(b.Value);
                return byValue != 0 ? byValue : fa.Deviation.CompareTo(fb.Deviation);
            }
            if (a.HasValue) return -1;
            if (b.HasValue) return 1;
            return fa.Deviation.CompareTo(fb.Deviation);
        }
    }
}
