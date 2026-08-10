using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>One family type that matched the search, ready to show in the grid.</summary>
    internal class FanCandidate
    {
        public FamilySymbol Symbol { get; set; }

        /// <summary>The type name — a fan's designation is its Revit type name.</summary>
        public string TypeName { get; set; }

        // Internal units, used for ranking only. Everything shown to the user is
        // the *Text property beside it, already formatted in project units.
        public double AirFlow { get; set; }
        public double Pressure { get; set; }

        public string AirFlowText { get; set; }
        public string PressureText { get; set; }
        public string PowerText { get; set; }
        public string SpeedText { get; set; }
        public string SfpText { get; set; }
        public string SoundText { get; set; }

        /// <summary>Sort keys for the optional quantities; null when unmapped or blank.</summary>
        public double? PowerValue { get; set; }
        public double? SfpValue { get; set; }
        public double? SoundValue { get; set; }

        /// <summary>Values of the user's extra display columns, in mapping order.</summary>
        public List<string> Extras { get; set; }

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
            Extras = new List<string>();
        }
    }
}
