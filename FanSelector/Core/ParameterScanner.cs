using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>One family parameter offered as a "write the figure to" target.</summary>
    internal class ParamChoice
    {
        public string Name { get; set; }

        /// <summary>The kind of quantity it holds — "Air Flow", "Length", "Currency".</summary>
        public string SpecLabel { get; set; }

        /// <summary>The project's unit for that kind — "m³/h", "cm".</summary>
        public string UnitSymbol { get; set; }

        /// <summary>Whether each placed fan owns its own value, or the type does.</summary>
        public bool IsInstance { get; set; }

        /// <summary>The "leave this unmapped" entry every dropdown starts with.</summary>
        public bool IsNone { get; set; }

        /// <summary>
        /// What the dropdown shows: "TES_Pressure — HVAC Pressure (Pa), instance".
        /// The KIND is spelled out, not just the unit: a list full of
        /// "A — Length (cm)" makes it obvious that the family has nothing suitable.
        /// </summary>
        public string Label
        {
            get
            {
                if (IsNone) return "(not written)";

                string label = Name;
                if (!string.IsNullOrEmpty(SpecLabel))
                {
                    label += "  —  " + SpecLabel;
                    if (!string.IsNullOrEmpty(UnitSymbol)) label += " (" + UnitSymbol + ")";
                }
                return label + (IsInstance ? ",  instance" : ",  type");
            }
        }

        public static ParamChoice None()
        {
            return new ParamChoice { IsNone = true };
        }

        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Puts the two halves of a mapping in front of the user: which columns the
    /// type catalogue offers to read a figure FROM, and which parameters the
    /// family offers to write it TO.
    /// </summary>
    internal static class ParameterScanner
    {
        /// <summary>Family names of every mechanical equipment type loaded in the project.</summary>
        public static List<string> EquipmentFamilies(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .Cast<FamilySymbol>()
                    .Where(s => s.Family != null && !string.IsNullOrEmpty(s.Family.Name))
                    .Select(s => s.Family.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Every type of one family that is loaded in the project.</summary>
        public static List<FamilySymbol> SymbolsOf(Document doc, string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return new List<FamilySymbol>();
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .Cast<FamilySymbol>()
                    .Where(s => s.Family != null &&
                                string.Equals(s.Family.Name, familyName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<FamilySymbol>(); }
        }

        /// <summary>Air terminal family names loaded in the project — the pool for a stub closer.</summary>
        public static List<string> AirTerminalFamilies(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .Cast<FamilySymbol>()
                    .Where(s => s.Family != null && !string.IsNullOrEmpty(s.Family.Name))
                    .Select(s => s.Family.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Duct system type names in the project — Supply Air, Return Air and so on.</summary>
        public static List<string> DuctSystemTypes(Document doc)
        {
            return Names<Autodesk.Revit.DB.Mechanical.MechanicalSystemType>(doc);
        }

        /// <summary>Duct type names in the project.</summary>
        public static List<string> DuctTypes(Document doc)
        {
            return Names<Autodesk.Revit.DB.Mechanical.DuctType>(doc);
        }

        private static List<string> Names<T>(Document doc) where T : Element
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(T))
                    .Cast<T>()
                    .Where(e => !string.IsNullOrEmpty(e.Name))
                    .Select(e => e.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        // ── Catalogue columns: where a figure is READ from ─────────────────────

        /// <summary>
        /// Columns that could carry this figure. By default only those whose
        /// declared kind fits; with <paramref name="showAll"/> every column,
        /// because an escape hatch that still filters is not one.
        /// </summary>
        public static List<CatalogColumn> Columns(CatalogFile file, QuantityInfo quantity, bool showAll)
        {
            if (file == null || !file.IsUsable) return new List<CatalogColumn>();
            if (showAll) return file.Columns.ToList();
            if (quantity == null) return new List<CatalogColumn>();

            return file.Columns.Where(c => Quantities.AcceptsSpec(quantity, c.Spec)).ToList();
        }

        /// <summary>
        /// Which catalogue column names the Revit family type, worked out by
        /// trying each one against the type names the project actually has and
        /// keeping whichever matches most lines.
        ///
        /// Guessing beats asking here because the answer is checkable: a column
        /// either names types that exist or it does not. Null means the first
        /// column already matches, or nothing does.
        /// </summary>
        public static string GuessTypeColumn(Document doc, string familyName, CatalogFile file)
        {
            if (file == null || !file.IsUsable) return null;

            var loaded = new HashSet<string>(
                SymbolsOf(doc, familyName).Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            if (loaded.Count == 0) return null;

            // The first column is the default, so it only loses to something better.
            int best = file.Rows.Count(r => loaded.Contains(r.TypeName ?? string.Empty));
            string bestColumn = null;

            foreach (CatalogColumn column in file.Columns)
            {
                int hits = file.Rows.Count(r => loaded.Contains(r.Text(column.Name)));
                if (hits > best) { best = hits; bestColumn = column.Name; }
            }

            return bestColumn;
        }

        /// <summary>
        /// The column most likely to carry this figure: a kind match whose name
        /// looks right, or the only kind match there is. Null when guessing would
        /// be a coin toss.
        /// </summary>
        /// <param name="taken">
        /// Columns already claimed by another figure. Without this, every
        /// quantity that accepts a plain Number — speed, SFP, sound power —
        /// grabs the same RPM column, and the results grid shows it three times.
        /// </param>
        public static string GuessColumn(CatalogFile file, QuantityInfo quantity, ICollection<string> taken)
        {
            List<CatalogColumn> matches = Columns(file, quantity, false)
                .Where(c => taken == null || !taken.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (string hint in quantity.NameHints)
                foreach (CatalogColumn column in matches)
                    if (column.Name.IndexOf(hint, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        return column.Name;

            // One candidate is only convincing when the figure is a required one.
            // For an optional figure it is just as likely the catalogue does not
            // carry it at all, and a wrong guess is worse than none.
            return matches.Count == 1 && quantity.Required ? matches[0].Name : null;
        }

        /// <summary>
        /// Why a figure has no column to read from, in words the user can act on —
        /// or null when it does have candidates.
        /// </summary>
        public static string DiagnoseColumn(CatalogFile file, QuantityInfo quantity)
        {
            if (file == null || !file.IsUsable || quantity == null) return null;
            if (Columns(file, quantity, false).Count > 0) return null;

            string kind = RevitUnits.SpecLabel(quantity.Specs.FirstOrDefault());
            if (string.IsNullOrEmpty(kind)) kind = quantity.DisplayName;

            return quantity.DisplayName + ": no column of the catalogue declares a \"" + kind
                 + "\" value. Either the catalogue does not carry this figure, or its header names a "
                 + "different kind for it — tick \"Show every column\" to map it anyway.";
        }

        // ── Family parameters: where a figure is WRITTEN to ────────────────────

        /// <summary>
        /// Parameters of the family a figure could be written to. Instance
        /// parameters first in spirit — they are the ones that normally matter —
        /// but the list is alphabetical and each entry says which it is.
        /// </summary>
        public static List<ParamChoice> Params(FamilyCatalog catalog, QuantityInfo quantity,
                                               bool showAll, Units units)
        {
            var choices = new List<ParamChoice>();
            if (catalog == null || !catalog.IsUsable) return choices;

            foreach (CatalogParameter parameter in catalog.Parameters)
            {
                if (parameter.IsReadOnly) continue;
                if (!showAll && !Quantities.AcceptsSpec(quantity, parameter.Spec)) continue;
                choices.Add(Describe(parameter, units));
            }

            return Sorted(choices);
        }

        /// <summary>Every writable parameter, whatever its kind.</summary>
        public static List<ParamChoice> AllParams(FamilyCatalog catalog, Units units)
        {
            var choices = new List<ParamChoice>();
            if (catalog == null || !catalog.IsUsable) return choices;

            foreach (CatalogParameter parameter in catalog.Parameters)
                if (!parameter.IsReadOnly) choices.Add(Describe(parameter, units));

            return Sorted(choices);
        }

        /// <summary>
        /// The parameter most likely to be meant for this figure. Prefers an
        /// instance parameter, because a type parameter would be shared by every
        /// fan of that type and writing one is almost never the intent.
        /// </summary>
        public static string GuessParam(FamilyCatalog catalog, QuantityInfo quantity, Units units)
        {
            List<ParamChoice> matches = Params(catalog, quantity, false, units);
            if (matches.Count == 0) return null;

            foreach (string hint in quantity.NameHints)
            {
                ParamChoice named = matches.FirstOrDefault(
                    c => c.Name.IndexOf(hint, StringComparison.CurrentCultureIgnoreCase) >= 0);
                if (named != null) return named.Name;
            }

            ParamChoice onInstance = matches.FirstOrDefault(c => c.IsInstance);
            if (onInstance != null) return onInstance.Name;

            return matches.Count == 1 ? matches[0].Name : null;
        }

        private static ParamChoice Describe(CatalogParameter parameter, Units units)
        {
            return new ParamChoice
            {
                Name = parameter.Name,
                SpecLabel = RevitUnits.SpecLabel(parameter.Spec),
                UnitSymbol = RevitUnits.Symbol(units, parameter.Spec),
                IsInstance = parameter.IsInstance
            };
        }

        // ── Diagnostics ───────────────────────────────────────────────────────

        /// <summary>
        /// Both halves of the picture as plain text, for the "Copy details" button:
        /// the catalogue's columns and first rows, and the family's parameters.
        /// When a mapping cannot be made, this is what makes it answerable.
        /// </summary>
        public static string Dump(Document doc, FamilyMapping mapping, CatalogFile file,
                                 FamilyCatalog catalog, Units units)
        {
            var text = new StringBuilder();
            text.AppendLine("Family: " + (mapping == null ? "(none)" : mapping.FamilyName));
            text.AppendLine("Catalogue: " + (mapping == null ? "(none)" : mapping.CatalogPath));
            if (mapping != null)
                text.AppendLine("Types loaded in this project: "
                                + SymbolsOf(doc, mapping.FamilyName).Count);
            text.AppendLine();

            text.AppendLine("=== CATALOGUE (where figures are read from) ===");
            if (file == null || !file.IsUsable)
            {
                text.AppendLine("Not usable: " + (file == null ? "no file" : file.Problem));
            }
            else
            {
                text.AppendLine("Rows: " + file.Rows.Count
                                + (file.SkippedLines > 0 ? "   skipped lines: " + file.SkippedLines : ""));
                text.AppendLine("column | kind token | unit token | kind understood | unit understood");
                foreach (CatalogColumn column in file.Columns)
                    text.AppendLine(string.Join(" | ",
                        column.Name,
                        column.SpecToken,
                        column.UnitToken,
                        column.Spec == null ? "(none)" : RevitUnits.SpecLabel(column.Spec),
                        column.UnitUnderstood ? "yes" : "NO - value taken as written"));

                text.AppendLine();
                text.AppendLine("First rows, as written in the file:");
                foreach (CatalogRow row in file.Rows.Take(4))
                {
                    text.AppendLine("  [" + row.TypeName + "]  "
                        + string.Join("  ", row.Raw.Select(p => p.Key + "=" + p.Value).ToArray()));
                }
                if (file.Rows.Count > 4) text.AppendLine("  ... and " + (file.Rows.Count - 4) + " more.");
            }

            text.AppendLine();
            text.AppendLine("=== FAMILY PARAMETERS (where figures are written to) ===");
            if (catalog == null || !catalog.IsUsable)
            {
                text.AppendLine("Not readable: " + (catalog == null ? "no family" : catalog.Problem));
            }
            else
            {
                text.AppendLine("name | kind | unit | storage | type or instance | writable");
                foreach (CatalogParameter parameter in catalog.Parameters
                             .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                    text.AppendLine(string.Join(" | ",
                        parameter.Name,
                        RevitUnits.SpecLabel(parameter.Spec),
                        RevitUnits.Symbol(units, parameter.Spec),
                        parameter.Storage.ToString(),
                        parameter.IsInstance ? "instance" : "type",
                        parameter.IsReadOnly ? "read-only" : "writable"));
            }

            return text.ToString();
        }

        private static List<ParamChoice> Sorted(List<ParamChoice> choices)
        {
            return choices
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
