using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>One parameter of a family, as the family itself declares it.</summary>
    internal class CatalogParameter
    {
        public string Name { get; set; }

        /// <summary>
        /// True for a parameter each placed instance owns. Fan families very often
        /// declare their performance figures this way, which is why the catalogue
        /// below has to exist at all: an instance parameter is invisible on a
        /// FamilySymbol, so it cannot be read from the project side.
        /// </summary>
        public bool IsInstance { get; set; }

        public ForgeTypeId Spec { get; set; }
        public StorageType Storage { get; set; }
    }

    /// <summary>The values one family type holds, keyed by parameter name.</summary>
    internal class CatalogRow
    {
        public string TypeName { get; set; }

        /// <summary>Numeric values in Revit's internal units.</summary>
        public Dictionary<string, double> Numbers { get; private set; }

        /// <summary>Values formatted in the project's units, for display.</summary>
        public Dictionary<string, string> Texts { get; private set; }

        public CatalogRow()
        {
            Numbers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public double? Number(string parameterName)
        {
            double value;
            return parameterName != null && Numbers.TryGetValue(parameterName, out value)
                ? (double?)value : null;
        }

        public string Text(string parameterName)
        {
            string value;
            return parameterName != null && Texts.TryGetValue(parameterName, out value)
                ? value : string.Empty;
        }
    }

    /// <summary>
    /// Everything one fan family knows about its own types, read from the family
    /// document rather than from the project.
    ///
    /// The project side only exposes TYPE parameters of a loaded FamilySymbol.
    /// Fan families routinely declare air flow, pressure and motor power as
    /// INSTANCE parameters with a per-type default, and those defaults are what a
    /// selection tool has to compare — they exist only inside the family. Reading
    /// them means opening the family with Document.EditFamily and going through
    /// FamilyManager, which sees type and instance parameters alike.
    ///
    /// That is not cheap, so a catalogue is read once and cached for the session.
    /// </summary>
    internal class FamilyCatalog
    {
        private static readonly Dictionary<string, FamilyCatalog> Cache =
            new Dictionary<string, FamilyCatalog>(StringComparer.OrdinalIgnoreCase);

        public string FamilyName { get; private set; }
        public List<CatalogParameter> Parameters { get; private set; }
        public List<CatalogRow> Rows { get; private set; }

        /// <summary>Why the family could not be read, or null when it was.</summary>
        public string Problem { get; private set; }

        public bool IsUsable { get { return Problem == null && Parameters.Count > 0; } }

        private FamilyCatalog()
        {
            Parameters = new List<CatalogParameter>();
            Rows = new List<CatalogRow>();
        }

        public CatalogParameter Find(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName)) return null;
            return Parameters.FirstOrDefault(
                p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        }

        public CatalogRow Row(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            return Rows.FirstOrDefault(
                r => string.Equals(r.TypeName, typeName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The catalogue for one family, read once per session. Never throws:
        /// a family that cannot be opened comes back with Problem set.
        /// </summary>
        public static FamilyCatalog For(Document doc, string familyName)
        {
            if (doc == null || string.IsNullOrEmpty(familyName))
                return Failed(familyName, "No family selected.");

            string key = (doc.PathName ?? "") + "|" + familyName;
            FamilyCatalog cached;
            if (Cache.TryGetValue(key, out cached)) return cached;

            FamilyCatalog catalog = Read(doc, familyName);
            Cache[key] = catalog;
            return catalog;
        }

        /// <summary>
        /// Forget what was read, so a family edited in this session is picked up
        /// again. Called when the Options dialog opens.
        /// </summary>
        public static void Forget()
        {
            Cache.Clear();
        }

        private static FamilyCatalog Read(Document doc, string familyName)
        {
            Family family;
            try
            {
                family = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, familyName,
                                                       StringComparison.OrdinalIgnoreCase));
            }
            catch { family = null; }

            if (family == null)
                return Failed(familyName, "The family \"" + familyName + "\" is not loaded in this project.");

            if (!family.IsEditable)
                return Failed(familyName,
                    "The family \"" + familyName + "\" cannot be opened for reading — in-place and system "
                    + "families have no editable definition.");

            Document familyDoc = null;
            try
            {
                familyDoc = doc.EditFamily(family);
                return Harvest(familyName, familyDoc);
            }
            catch (Exception exception)
            {
                return Failed(familyName,
                    "The family \"" + familyName + "\" could not be opened to read its types: "
                    + exception.Message
                    + "\n\nIf the family is open in another window, close it and try again.");
            }
            finally
            {
                // Opened only to be read; closing without saving leaves the
                // project untouched.
                if (familyDoc != null)
                {
                    try { familyDoc.Close(false); } catch { /* nothing useful to do */ }
                }
            }
        }

        private static FamilyCatalog Harvest(string familyName, Document familyDoc)
        {
            var catalog = new FamilyCatalog { FamilyName = familyName };

            FamilyManager manager = familyDoc.FamilyManager;
            if (manager == null)
                return Failed(familyName, "The family has no parameter definitions.");

            var parameters = new List<FamilyParameter>();
            foreach (FamilyParameter parameter in manager.Parameters)
            {
                if (parameter == null || parameter.Definition == null) continue;
                if (string.IsNullOrEmpty(parameter.Definition.Name)) continue;

                parameters.Add(parameter);
                catalog.Parameters.Add(new CatalogParameter
                {
                    Name = parameter.Definition.Name,
                    IsInstance = parameter.IsInstance,
                    Spec = SpecOf(parameter),
                    Storage = parameter.StorageType
                });
            }

            foreach (FamilyType type in manager.Types)
            {
                // A family with no named types still reports one with an empty
                // name; it is not something the user can place.
                if (type == null || string.IsNullOrEmpty(type.Name)) continue;

                var row = new CatalogRow { TypeName = type.Name };
                foreach (FamilyParameter parameter in parameters)
                {
                    string name = parameter.Definition.Name;
                    try
                    {
                        if (!type.HasValue(parameter)) continue;

                        if (parameter.StorageType == StorageType.Double)
                        {
                            double? value = type.AsDouble(parameter);
                            if (value.HasValue) row.Numbers[name] = value.Value;
                        }
                        else if (parameter.StorageType == StorageType.Integer)
                        {
                            int? value = type.AsInteger(parameter);
                            if (value.HasValue) row.Numbers[name] = value.Value;
                        }

                        string text = type.AsValueString(parameter);
                        if (string.IsNullOrEmpty(text) && parameter.StorageType == StorageType.String)
                            text = type.AsString(parameter);
                        if (!string.IsNullOrEmpty(text)) row.Texts[name] = text;
                    }
                    catch { /* one unreadable parameter must not lose the whole type */ }
                }

                catalog.Rows.Add(row);
            }

            return catalog;
        }

        private static ForgeTypeId SpecOf(FamilyParameter parameter)
        {
            try
            {
                ForgeTypeId spec = parameter.Definition.GetDataType();
                return spec == null || spec.Empty() ? null : spec;
            }
            catch { return null; }
        }

        private static FamilyCatalog Failed(string familyName, string problem)
        {
            return new FamilyCatalog { FamilyName = familyName, Problem = problem };
        }
    }
}
