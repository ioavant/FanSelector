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
        /// True for a parameter each placed instance owns. This is the reason the
        /// list has to come from the family definition at all: an instance
        /// parameter does not exist on a FamilySymbol, so the project side cannot
        /// see it — and fan families routinely declare their performance figures
        /// exactly that way.
        /// </summary>
        public bool IsInstance { get; set; }

        public ForgeTypeId Spec { get; set; }
        public StorageType Storage { get; set; }
        public bool IsReadOnly { get; set; }
    }

    /// <summary>
    /// The parameters one fan family declares, read from the family definition.
    ///
    /// Only the parameter LIST comes from here — the performance values come from
    /// the type catalogue (see <see cref="CatalogFile"/>). What this is for is the
    /// other half of a mapping: which parameter of the family each figure should
    /// be WRITTEN to when a fan is placed.
    ///
    /// Reading it means opening the family with Document.EditFamily, which is not
    /// cheap, so a family is read once and cached for the session.
    /// </summary>
    internal class FamilyCatalog
    {
        private static readonly Dictionary<string, FamilyCatalog> Cache =
            new Dictionary<string, FamilyCatalog>(StringComparer.OrdinalIgnoreCase);

        public string FamilyName { get; private set; }
        public List<CatalogParameter> Parameters { get; private set; }

        /// <summary>Why the family could not be read, or null when it was.</summary>
        public string Problem { get; private set; }

        public bool IsUsable { get { return Problem == null && Parameters.Count > 0; } }

        private FamilyCatalog()
        {
            Parameters = new List<CatalogParameter>();
        }

        public CatalogParameter Find(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName)) return null;
            return Parameters.FirstOrDefault(
                p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The parameters of one family, read once per session. Never throws:
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
                    "The family \"" + familyName + "\" could not be opened to read its parameters: "
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

            foreach (FamilyParameter parameter in manager.Parameters)
            {
                if (parameter == null || parameter.Definition == null) continue;
                if (string.IsNullOrEmpty(parameter.Definition.Name)) continue;

                catalog.Parameters.Add(new CatalogParameter
                {
                    Name = parameter.Definition.Name,
                    IsInstance = parameter.IsInstance,
                    Spec = SpecOf(parameter),
                    Storage = parameter.StorageType,
                    IsReadOnly = parameter.IsReadOnly
                });
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
