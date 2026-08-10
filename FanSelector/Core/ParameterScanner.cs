using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>One parameter offered in a mapping dropdown.</summary>
    internal class ParamChoice
    {
        public string Name { get; set; }
        public string UnitSymbol { get; set; }

        /// <summary>The "leave this quantity unmapped" entry every dropdown starts with.</summary>
        public bool IsNone { get; set; }

        /// <summary>What the dropdown shows: "MotorPower — kW".</summary>
        public string Label
        {
            get
            {
                if (IsNone) return "(not mapped)";
                return string.IsNullOrEmpty(UnitSymbol) ? Name : Name + "  —  " + UnitSymbol;
            }
        }

        public static ParamChoice None()
        {
            return new ParamChoice { IsNone = true, Name = null, UnitSymbol = null };
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
        /// parameters cannot be listed from a FamilySymbol, so the optional
        /// "write air flow onto the instance" dropdown is seeded from here — and
        /// left as free text when the project has no instance yet.
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
        /// every parameter of a compatible storage type, which is the escape hatch
        /// for a family that keeps pressure in a plain Number.
        /// </summary>
        public static List<ParamChoice> Choices(Element probe, QuantityInfo quantity, bool showAll, Units units)
        {
            var choices = new List<ParamChoice>();
            if (probe == null || quantity == null) return choices;

            foreach (Parameter parameter in Enumerate(probe))
            {
                ForgeTypeId spec = RevitUnits.SpecOf(parameter);

                bool accepted = showAll
                    ? Quantities.AcceptsStorage(quantity, parameter.StorageType)
                    : Quantities.AcceptsSpec(quantity, spec);
                if (!accepted) continue;

                choices.Add(new ParamChoice
                {
                    Name = parameter.Definition.Name,
                    UnitSymbol = RevitUnits.Symbol(units, spec)
                });
            }

            return Dedupe(choices);
        }

        /// <summary>Every parameter of the element — the pool for extra display columns.</summary>
        public static List<ParamChoice> AllChoices(Element probe, Units units)
        {
            var choices = new List<ParamChoice>();
            if (probe == null) return choices;

            foreach (Parameter parameter in Enumerate(probe))
            {
                choices.Add(new ParamChoice
                {
                    Name = parameter.Definition.Name,
                    UnitSymbol = RevitUnits.Symbol(units, RevitUnits.SpecOf(parameter))
                });
            }

            return Dedupe(choices);
        }

        /// <summary>
        /// The parameter most likely to carry this quantity: a spec match whose
        /// name looks right, or — failing that — the only spec match there is.
        /// Returns null when guessing would be a coin toss.
        /// </summary>
        public static string Guess(Element probe, QuantityInfo quantity)
        {
            if (probe == null || quantity == null) return null;

            var specMatches = new List<string>();
            foreach (Parameter parameter in Enumerate(probe))
            {
                if (!Quantities.AcceptsSpec(quantity, RevitUnits.SpecOf(parameter))) continue;
                specMatches.Add(parameter.Definition.Name);
            }

            foreach (string hint in quantity.NameHints)
                foreach (string name in specMatches)
                    if (name.IndexOf(hint, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        return name;

            return specMatches.Count == 1 ? specMatches[0] : null;
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
