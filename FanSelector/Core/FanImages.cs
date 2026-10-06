using System;

namespace FanSelector.Core
{
    /// <summary>
    /// Which of the four pictures shipped inside the add-in belongs to a family.
    ///
    /// Chosen from the family's NAME, not from a setting: the four cover the
    /// shapes a fan comes in, the answer is always the same for a given name, and
    /// asking the user to pick a file for something this predictable is a setting
    /// that earns nothing.
    ///
    /// The pictures are embedded resources, so there is no file to find, no path
    /// to store and nothing to go missing.
    /// </summary>
    internal static class FanImages
    {
        public const string Axial = "fan_axial.jpg";
        public const string Centrifugal = "fan_centrifugal.jpg";
        public const string InLine = "fan_inline.jpg";

        /// <summary>Shown for anything the three shapes above do not describe.</summary>
        public const string Generic = "fan_generic.jpg";

        /// <summary>
        /// The picture for a family name. Matching is on a substring and
        /// case-insensitive, so "ME_Axial Fan", "AXIAL-500" and "Roof axial" all
        /// land on the same image.
        /// </summary>
        public static string For(string familyName)
        {
            if (Has(familyName, "axial")) return Axial;
            if (Has(familyName, "centrifugal")) return Centrifugal;

            // "InLine", "In-Line" and "in line" are all the same fan.
            if (Has(familyName, "inline") || Has(familyName, "in-line") || Has(familyName, "in line"))
                return InLine;

            return Generic;
        }

        private static bool Has(string text, string fragment)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
