using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>One catalogue row that matched the search, ready to show in the grid.</summary>
    internal class FanCandidate
    {
        /// <summary>The family type this row names — the catalogue's first cell.</summary>
        public string TypeName { get; set; }

        /// <summary>
        /// The type as loaded in the project, or null when it is in the catalogue
        /// but not in this model yet. Placing such a row loads that one type.
        /// </summary>
        public FamilySymbol Symbol { get; set; }

        public bool IsLoaded { get { return Symbol != null; } }

        /// <summary>Shown in the grid so it is clear which rows are already in the model.</summary>
        public string LoadedText { get { return Symbol != null ? "yes" : "on insert"; } }

        // Internal units, used for ranking and for writing onto the placed fan.
        public double AirFlow { get; set; }
        public double Pressure { get; set; }

        /// <summary>Every mapped figure that had a value, in internal units.</summary>
        public Dictionary<FanQuantity, double> Values { get; private set; }

        public string AirFlowText { get; set; }
        public string PressureText { get; set; }
        public string PowerText { get; set; }
        public string SpeedText { get; set; }
        public string SfpText { get; set; }
        public string SoundText { get; set; }

        /// <summary>Sort keys for the optional figures; null when unmapped or blank.</summary>
        public double? PowerValue { get; set; }
        public double? SfpValue { get; set; }
        public double? SoundValue { get; set; }

        /// <summary>Values of the user's extra display columns, in mapping order.</summary>
        public List<string> Extras { get; private set; }

        /// <summary>
        /// How far this type is from what was asked for: the larger of the two
        /// relative errors, as a fraction. 0.04 means "within 4% on both".
        /// </summary>
        public double Deviation { get; set; }

        public string DeviationText
        {
            get { return (Deviation * 100.0).ToString("0.#") + "%"; }
        }

        public FanCandidate()
        {
            Values = new Dictionary<FanQuantity, double>();
            Extras = new List<string>();
        }
    }
}
