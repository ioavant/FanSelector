using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>
    /// Attenuators on a fan's inlet and outlet, measured in duct diameters:
    /// 0 for none, 1 for one diameter's length, 2 for two.
    ///
    /// The family builds them itself; all that is needed is the two numbers. So
    /// this is not a modelling feature, it is two integers written onto the
    /// placed fan.
    ///
    /// Which families offer it is decided by asking the family, not by its name.
    /// Today that means the axial fan and nothing else, but a family authored the
    /// same way will work without anyone editing this.
    /// </summary>
    internal static class Silencers
    {
        /// <summary>Length of the inlet attenuator, in duct diameters.</summary>
        public const string InParameter = "D silencer In";

        /// <summary>Length of the outlet attenuator, in duct diameters.</summary>
        public const string OutParameter = "D silencer Out";

        public const int Maximum = 2;

        /// <summary>
        /// Whether this family can build attenuators — that is, whether it
        /// declares both parameters as whole numbers each fan owns.
        /// </summary>
        public static bool Available(Document doc, string familyName)
        {
            FamilyCatalog catalog = FamilyCatalog.For(doc, familyName);
            if (!catalog.IsUsable) return false;

            return IsLength(catalog.Find(InParameter)) && IsLength(catalog.Find(OutParameter));
        }

        private static bool IsLength(CatalogParameter parameter)
        {
            return parameter != null
                && parameter.IsInstance
                && !parameter.IsReadOnly
                && parameter.Storage == StorageType.Integer;
        }

        /// <summary>
        /// Write the two lengths onto a placed fan. Returns null when both took,
        /// or what could not be set — never throws, because a fan that is already
        /// in the model is not worth losing over an attenuator.
        /// </summary>
        public static string Apply(FamilyInstance fan, int inDiameters, int outDiameters)
        {
            if (fan == null) return null;
            if (inDiameters <= 0 && outDiameters <= 0) return null;

            var problems = new List<string>();
            Set(fan, InParameter, inDiameters, problems);
            Set(fan, OutParameter, outDiameters, problems);

            return problems.Count == 0 ? null
                 : "the attenuators could not be set: " + string.Join("; ", problems.ToArray()) + ".";
        }

        private static void Set(FamilyInstance fan, string parameterName, int diameters,
                                List<string> problems)
        {
            Parameter parameter;
            try { parameter = fan.LookupParameter(parameterName); }
            catch { parameter = null; }

            if (parameter == null)
            {
                problems.Add("\"" + parameterName + "\" is not a parameter of this fan");
                return;
            }

            if (parameter.IsReadOnly)
            {
                problems.Add("\"" + parameterName + "\" is read-only");
                return;
            }

            try { parameter.Set(diameters); }
            catch (Exception exception)
            {
                problems.Add("\"" + parameterName + "\": " + exception.Message);
            }
        }
    }
}
