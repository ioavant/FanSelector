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

        /// <summary>How many types of the family were looked at, matched or not.</summary>
        public int TypesExamined { get; set; }

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
    /// The selection itself: read every type of the chosen family through the
    /// user's parameter mapping, keep the ones within tolerance of the requested
    /// duty, and rank them.
    /// </summary>
    internal static class FanSearch
    {
        /// <summary>
        /// The spec of the parameter a quantity is mapped to, so the caller can
        /// format and parse in exactly the units that parameter uses. Null when
        /// the quantity is unmapped or the family is not loaded.
        /// </summary>
        public static ForgeTypeId SpecFor(Document doc, FamilyMapping mapping, FanQuantity quantity)
        {
            if (mapping == null) return null;
            FamilySymbol probe = ParameterScanner.ProbeSymbol(doc, mapping.FamilyName);
            if (probe == null) return null;

            string name = mapping.Get(quantity);
            if (string.IsNullOrEmpty(name)) return null;

            return RevitUnits.SpecOf(probe.LookupParameter(name));
        }

        public static SearchResult Run(Document doc, FamilyMapping mapping,
                                       double targetAirFlow, double targetPressure,
                                       double tolerancePercent, FanSort sort)
        {
            var result = new SearchResult();
            if (mapping == null || !mapping.IsUsable)
            {
                result.Note = "This family has no air flow / pressure mapping yet. Open Options to set one up.";
                return result;
            }

            List<FamilySymbol> symbols = ParameterScanner.SymbolsOf(doc, mapping.FamilyName);
            result.TypesExamined = symbols.Count;
            if (symbols.Count == 0)
            {
                result.Note = "The family \"" + mapping.FamilyName +
                              "\" is not loaded in this project. Load it, or pick another family.";
                return result;
            }

            double tolerance = Math.Max(tolerancePercent, 0.0) / 100.0;
            int unreadable = 0;

            foreach (FamilySymbol symbol in symbols)
            {
                double? airFlow = RevitUnits.Number(symbol.LookupParameter(mapping.AirFlow));
                double? pressure = RevitUnits.Number(symbol.LookupParameter(mapping.Pressure));
                if (!airFlow.HasValue || !pressure.HasValue) { unreadable++; continue; }

                double deviation = Math.Max(Relative(airFlow.Value, targetAirFlow),
                                            Relative(pressure.Value, targetPressure));
                if (deviation > tolerance) continue;

                result.Candidates.Add(Build(symbol, mapping, airFlow.Value, pressure.Value, deviation));
            }

            if (result.Candidates.Count == 0 && unreadable == symbols.Count)
                result.Note = "None of the " + symbols.Count + " types of \"" + mapping.FamilyName +
                              "\" has a value in both mapped parameters. Check the mapping in Options.";

            Sort(result.Candidates, sort);
            return result;
        }

        private static FanCandidate Build(FamilySymbol symbol, FamilyMapping mapping,
                                          double airFlow, double pressure, double deviation)
        {
            Parameter power = Lookup(symbol, mapping.Power);
            Parameter sfp = Lookup(symbol, mapping.Sfp);
            Parameter sound = Lookup(symbol, mapping.SoundPower);

            var candidate = new FanCandidate
            {
                Symbol = symbol,
                TypeName = symbol.Name,
                AirFlow = airFlow,
                Pressure = pressure,
                AirFlowText = RevitUnits.Display(Lookup(symbol, mapping.AirFlow)),
                PressureText = RevitUnits.Display(Lookup(symbol, mapping.Pressure)),
                PowerText = RevitUnits.Display(power),
                SpeedText = RevitUnits.Display(Lookup(symbol, mapping.Speed)),
                SfpText = RevitUnits.Display(sfp),
                SoundText = RevitUnits.Display(sound),
                PowerValue = RevitUnits.Number(power),
                SfpValue = RevitUnits.Number(sfp),
                SoundValue = RevitUnits.Number(sound),
                Deviation = deviation
            };

            foreach (string extra in mapping.ExtraColumns)
                candidate.Extras.Add(RevitUnits.Display(Lookup(symbol, extra)));

            return candidate;
        }

        /// <summary>
        /// LookupParameter by a name that may be absent from the mapping at all.
        /// Every optional quantity goes through here, so an unmapped one reads as
        /// "no value" rather than throwing on a null name.
        /// </summary>
        private static Parameter Lookup(Element element, string parameterName)
        {
            if (element == null || string.IsNullOrEmpty(parameterName)) return null;
            try { return element.LookupParameter(parameterName); }
            catch { return null; }
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

        // Types with no value for the sort key go last rather than first, which is
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
