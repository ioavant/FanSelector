using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>One parameter offered in a mapping dropdown.</summary>
    internal class ParamChoice
    {
        public string Name { get; set; }

        /// <summary>The kind of quantity it holds — "Air Flow", "Length", "Currency".</summary>
        public string SpecLabel { get; set; }

        /// <summary>The project's unit for that kind — "m³/h", "cm".</summary>
        public string UnitSymbol { get; set; }

        /// <summary>Whether each placed fan owns its own value, or the type does.</summary>
        public bool IsInstance { get; set; }

        /// <summary>The "leave this quantity unmapped" entry every dropdown starts with.</summary>
        public bool IsNone { get; set; }

        /// <summary>
        /// What the dropdown shows: "AirFlow — Air Flow (m³/h), instance". The KIND
        /// is spelled out, not just the unit: a list full of "A — Length (cm)"
        /// makes it obvious at a glance that the family carries no air flow, where
        /// a bare "A — cm" only looks like noise.
        /// </summary>
        public string Label
        {
            get
            {
                if (IsNone) return "(not mapped)";

                string label = Name;
                if (!string.IsNullOrEmpty(SpecLabel))
                {
                    label += "  —  " + SpecLabel;
                    if (!string.IsNullOrEmpty(UnitSymbol)) label += " (" + UnitSymbol + ")";
                }
                return label + (IsInstance ? ",  instance" : string.Empty);
            }
        }

        public static ParamChoice None()
        {
            return new ParamChoice { IsNone = true };
        }

        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Reads what a project actually contains: which fan families are loaded and
    /// which types they have. Which of their PARAMETERS could carry a performance
    /// figure is answered from <see cref="FamilyCatalog"/>, because the project
    /// side cannot see instance parameters at all.
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

        /// <summary>
        /// Parameters that may carry this quantity. By default only those whose
        /// spec says so; with <paramref name="showAll"/> every parameter the family
        /// declares, because an escape hatch that still filters is not one.
        /// </summary>
        public static List<ParamChoice> Choices(FamilyCatalog catalog, QuantityInfo quantity,
                                                bool showAll, Units units)
        {
            if (showAll) return AllChoices(catalog, units);

            var choices = new List<ParamChoice>();
            if (catalog == null || quantity == null) return choices;

            foreach (CatalogParameter parameter in catalog.Parameters)
                if (Quantities.AcceptsSpec(quantity, parameter.Spec))
                    choices.Add(Describe(parameter, units));

            return Sorted(choices);
        }

        /// <summary>Every parameter the family declares — the pool for extra display columns.</summary>
        public static List<ParamChoice> AllChoices(FamilyCatalog catalog, Units units)
        {
            var choices = new List<ParamChoice>();
            if (catalog == null) return choices;

            foreach (CatalogParameter parameter in catalog.Parameters)
                choices.Add(Describe(parameter, units));

            return Sorted(choices);
        }

        /// <summary>Only the instance parameters, for the optional "write onto the instance" mapping.</summary>
        public static List<ParamChoice> InstanceChoices(FamilyCatalog catalog, Units units)
        {
            var choices = new List<ParamChoice>();
            if (catalog == null) return choices;

            foreach (CatalogParameter parameter in catalog.Parameters)
                if (parameter.IsInstance) choices.Add(Describe(parameter, units));

            return Sorted(choices);
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

        /// <summary>
        /// The parameter most likely to carry this quantity: a spec match whose
        /// name looks right, or — failing that — the only spec match there is.
        /// Returns null when guessing would be a coin toss.
        /// </summary>
        public static string Guess(FamilyCatalog catalog, QuantityInfo quantity)
        {
            if (catalog == null || quantity == null) return null;

            List<string> specMatches = Matching(catalog, quantity);

            foreach (string hint in quantity.NameHints)
                foreach (string name in specMatches)
                    if (name.IndexOf(hint, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        return name;

            return specMatches.Count == 1 ? specMatches[0] : null;
        }

        /// <summary>
        /// Why a quantity has nothing to map to, in words the user can act on — or
        /// null when it does have candidates.
        /// </summary>
        public static string Diagnose(FamilyCatalog catalog, QuantityInfo quantity)
        {
            if (catalog == null || !catalog.IsUsable || quantity == null) return null;
            if (Matching(catalog, quantity).Count > 0) return null;

            string kind = RevitUnits.SpecLabel(quantity.Specs.FirstOrDefault());
            if (string.IsNullOrEmpty(kind)) kind = quantity.DisplayName;

            return quantity.DisplayName + ": no parameter of this family — type or instance — holds a \""
                 + kind + "\" value. Either the family carries no such data, or it keeps it in a parameter "
                 + "of another kind; tick \"Show every parameter\" to see all of them.";
        }

        private static List<string> Matching(FamilyCatalog catalog, QuantityInfo quantity)
        {
            return catalog.Parameters
                .Where(p => Quantities.AcceptsSpec(quantity, p.Spec))
                .Select(p => p.Name)
                .ToList();
        }

        /// <summary>
        /// The whole picture for one family as plain text, for the "Copy parameter
        /// list" button: every parameter with its kind and whether it is per type
        /// or per instance, then the values of the first few types.
        /// </summary>
        public static string Dump(Document doc, FamilyCatalog catalog, Units units)
        {
            var text = new StringBuilder();
            text.AppendLine("Family: " + (catalog == null ? "(none)" : catalog.FamilyName));

            if (catalog == null || !catalog.IsUsable)
            {
                text.AppendLine("Could not be read: " + (catalog == null ? "no catalogue" : catalog.Problem));
                return text.ToString();
            }

            int loaded = SymbolsOf(doc, catalog.FamilyName).Count;
            text.AppendLine("Types in the family: " + catalog.Rows.Count
                            + "   |   types loaded in this project: " + loaded);
            text.AppendLine();

            text.AppendLine("PARAMETERS");
            text.AppendLine("name | kind | unit | storage | type or instance");
            foreach (CatalogParameter parameter in catalog.Parameters
                         .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                text.AppendLine(string.Join(" | ",
                    parameter.Name,
                    RevitUnits.SpecLabel(parameter.Spec),
                    RevitUnits.Symbol(units, parameter.Spec),
                    parameter.Storage.ToString(),
                    parameter.IsInstance ? "instance" : "type"));
            }

            text.AppendLine();
            text.AppendLine("VALUES OF THE FIRST TYPES  (parameter = value, blank ones omitted)");
            foreach (CatalogRow row in catalog.Rows.Take(5))
            {
                text.AppendLine();
                text.AppendLine("[" + row.TypeName + "]");
                foreach (var pair in row.Texts.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
                    text.AppendLine("  " + pair.Key + " = " + pair.Value);
            }
            if (catalog.Rows.Count > 5)
                text.AppendLine();
            if (catalog.Rows.Count > 5)
                text.AppendLine("... and " + (catalog.Rows.Count - 5) + " more types.");

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
