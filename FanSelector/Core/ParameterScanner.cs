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

        /// <summary>The "leave this quantity unmapped" entry every dropdown starts with.</summary>
        public bool IsNone { get; set; }

        /// <summary>
        /// What the dropdown shows: "AirFlow — Air Flow (m³/h)". The KIND is
        /// spelled out, not just the unit: a list full of "A — Length (cm)" makes
        /// it obvious at a glance that the family carries no air flow at all,
        /// where a bare "A — cm" only looks like noise.
        /// </summary>
        public string Label
        {
            get
            {
                if (IsNone) return "(not mapped)";
                if (string.IsNullOrEmpty(SpecLabel)) return Name;
                return string.IsNullOrEmpty(UnitSymbol)
                    ? Name + "  —  " + SpecLabel
                    : Name + "  —  " + SpecLabel + " (" + UnitSymbol + ")";
            }
        }

        public static ParamChoice None()
        {
            return new ParamChoice { IsNone = true };
        }

        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Reads what a project actually contains: which fan families are loaded,
    /// which types they have, and which of their parameters could plausibly
    /// carry a given performance figure.
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

        /// <summary>Every type of one family, in the order Revit lists them.</summary>
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
        /// Any one type of the family, used to read the parameter list the
        /// mapping dialog offers. Every type of a family carries the same type
        /// parameters, so which one is irrelevant.
        /// </summary>
        public static FamilySymbol ProbeSymbol(Document doc, string familyName)
        {
            List<FamilySymbol> symbols = SymbolsOf(doc, familyName);
            return symbols.Count == 0 ? null : symbols[0];
        }

        /// <summary>
        /// A placed instance of the family, if the project has one. Instance
        /// parameters cannot be listed from a FamilySymbol, so this is what makes
        /// the optional "write air flow onto the instance" dropdown — and the
        /// diagnosis below — possible.
        /// </summary>
        public static FamilyInstance ProbeInstance(Document doc, string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .Cast<FamilyInstance>()
                    .FirstOrDefault(i => i.Symbol != null && i.Symbol.Family != null &&
                                         string.Equals(i.Symbol.Family.Name, familyName,
                                                       StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        /// <summary>
        /// Parameters of <paramref name="probe"/> that may carry this quantity.
        /// By default only those whose spec says so; with <paramref name="showAll"/>
        /// literally every parameter, because an escape hatch that still filters
        /// is not an escape hatch.
        /// </summary>
        public static List<ParamChoice> Choices(Element probe, QuantityInfo quantity, bool showAll, Units units)
        {
            if (showAll) return AllChoices(probe, units);

            var choices = new List<ParamChoice>();
            if (probe == null || quantity == null) return choices;

            foreach (Parameter parameter in Enumerate(probe))
            {
                ForgeTypeId spec = RevitUnits.SpecOf(parameter);
                if (!Quantities.AcceptsSpec(quantity, spec)) continue;
                choices.Add(Describe(parameter, spec, units));
            }

            return Dedupe(choices);
        }

        /// <summary>Every parameter of the element — the pool for extra display columns.</summary>
        public static List<ParamChoice> AllChoices(Element probe, Units units)
        {
            var choices = new List<ParamChoice>();
            if (probe == null) return choices;

            foreach (Parameter parameter in Enumerate(probe))
                choices.Add(Describe(parameter, RevitUnits.SpecOf(parameter), units));

            return Dedupe(choices);
        }

        private static ParamChoice Describe(Parameter parameter, ForgeTypeId spec, Units units)
        {
            return new ParamChoice
            {
                Name = parameter.Definition.Name,
                SpecLabel = RevitUnits.SpecLabel(spec),
                UnitSymbol = RevitUnits.Symbol(units, spec)
            };
        }

        /// <summary>
        /// The parameter most likely to carry this quantity: a spec match whose
        /// name looks right, or — failing that — the only spec match there is.
        /// Returns null when guessing would be a coin toss.
        /// </summary>
        public static string Guess(Element probe, QuantityInfo quantity)
        {
            if (probe == null || quantity == null) return null;

            List<string> specMatches = Matching(probe, quantity);

            foreach (string hint in quantity.NameHints)
                foreach (string name in specMatches)
                    if (name.IndexOf(hint, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        return name;

            return specMatches.Count == 1 ? specMatches[0] : null;
        }

        /// <summary>
        /// Why a REQUIRED quantity has nothing to map to, in words the user can
        /// act on — or null when it does have candidates. Without this the
        /// dropdown is simply empty and the family looks broken for no stated
        /// reason.
        /// </summary>
        public static string Diagnose(FamilySymbol type, FamilyInstance instance, QuantityInfo quantity)
        {
            if (type == null || quantity == null) return null;
            if (Matching(type, quantity).Count > 0) return null;

            string kind = RevitUnits.SpecLabel(quantity.Specs.FirstOrDefault());
            if (string.IsNullOrEmpty(kind)) kind = quantity.DisplayName;

            // The likely case, and the one nobody can diagnose from an empty
            // dropdown: the family does carry the figure, but per instance.
            if (instance != null)
            {
                List<string> onInstance = Matching(instance, quantity);
                if (onInstance.Count > 0)
                    return quantity.DisplayName + ": this family carries \"" + onInstance[0]
                         + "\" on the INSTANCE, not on the type. Fan Selector compares one type against "
                         + "another, so the figure has to be a TYPE parameter. Change it to a type "
                         + "parameter in the Family Editor, or select a family that stores it per type.";
            }

            return quantity.DisplayName + ": no type parameter of this family holds a \"" + kind
                 + "\" value. Either the family carries no performance data on its types, or it keeps it "
                 + "in a parameter of another kind — tick \"Show every parameter\" to see all of them.";
        }

        /// <summary>Names of the element's parameters whose spec fits the quantity.</summary>
        private static List<string> Matching(Element element, QuantityInfo quantity)
        {
            var names = new List<string>();
            foreach (Parameter parameter in Enumerate(element))
                if (Quantities.AcceptsSpec(quantity, RevitUnits.SpecOf(parameter)))
                    names.Add(parameter.Definition.Name);
            return names;
        }

        /// <summary>
        /// The whole parameter picture for one family as plain text, for the
        /// "Copy parameter list" button. When a mapping cannot be made, this is
        /// what turns "the parameter isn't in the list" into something answerable.
        /// </summary>
        public static string Dump(Document doc, string familyName, Units units)
        {
            var text = new StringBuilder();
            List<FamilySymbol> symbols = SymbolsOf(doc, familyName);

            text.AppendLine("Family: " + familyName);
            text.AppendLine("Types loaded in this project: " + symbols.Count);
            if (symbols.Count > 0)
                text.AppendLine("First few: " + string.Join(", ", symbols.Take(8).Select(s => s.Name)));
            text.AppendLine();

            if (symbols.Count == 0)
            {
                text.AppendLine("Nothing to report - the family has no types in this project.");
                return text.ToString();
            }

            FamilySymbol probe = symbols[0];
            text.AppendLine("TYPE PARAMETERS  (read from type \"" + probe.Name + "\")");
            text.AppendLine("name | kind | unit | storage | read-only | value");
            AppendParameters(text, probe, units);

            FamilyInstance instance = ProbeInstance(doc, familyName);
            text.AppendLine();
            if (instance == null)
            {
                text.AppendLine("INSTANCE PARAMETERS: no instance of this family is placed in the project, "
                                + "so they cannot be listed.");
            }
            else
            {
                text.AppendLine("INSTANCE PARAMETERS  (read from a placed instance, id " + instance.Id + ")");
                text.AppendLine("name | kind | unit | storage | read-only | value");
                AppendParameters(text, instance, units);
            }

            return text.ToString();
        }

        private static void AppendParameters(StringBuilder text, Element element, Units units)
        {
            foreach (Parameter parameter in Enumerate(element)
                         .OrderBy(p => p.Definition.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ForgeTypeId spec = RevitUnits.SpecOf(parameter);
                text.AppendLine(string.Join(" | ",
                    parameter.Definition.Name,
                    RevitUnits.SpecLabel(spec),
                    RevitUnits.Symbol(units, spec),
                    parameter.StorageType.ToString(),
                    parameter.IsReadOnly ? "read-only" : "writable",
                    RevitUnits.Display(parameter)));
            }
        }

        private static IEnumerable<Parameter> Enumerate(Element element)
        {
            ParameterSet set;
            try { set = element.Parameters; }
            catch { yield break; }

            foreach (Parameter parameter in set)
            {
                if (parameter == null || parameter.Definition == null) continue;
                if (string.IsNullOrEmpty(parameter.Definition.Name)) continue;
                yield return parameter;
            }
        }

        // Two parameters of an element can share a name (a shared parameter and a
        // family parameter, say). LookupParameter would only ever find the first,
        // so offering the name twice would be offering a choice that does not exist.
        private static List<ParamChoice> Dedupe(List<ParamChoice> choices)
        {
            return choices
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
