using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>
    /// The options the user edits in the Options dialog.
    ///
    /// Stored in a small JSON file NEXT TO THE DLL — i.e. in the install folder,
    /// %ProgramData%\&lt;brand&gt;\FanSelector\ — so one set of mappings serves every
    /// Revit version on the machine, and every project. Deliberately not per
    /// project: which parameter of a family carries air flow is a property of
    /// the family library an office uses, not of one model.
    ///
    /// ProgramData grants BUILTIN\Users write access to subfolders, so no
    /// elevation is needed. A file created there by another user can still be
    /// unwritable, so saving falls back to a per-user copy under %AppData%,
    /// which then takes precedence when loading.
    /// </summary>
    [DataContract]
    internal class FanSettings
    {
        private const string FileName = "FanSelector.settings.json";

        public const double DefaultTolerancePercent = 10.0;

        /// <summary>
        /// UTF-8 WITHOUT a byte-order mark. File.WriteAllText(..., Encoding.UTF8)
        /// prepends one, and DataContractJsonSerializer.ReadObject rejects it
        /// outright ("Encountered unexpected character 'ï'") — which is how every
        /// saved mapping silently came back empty until this was found.
        /// </summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Why the last load failed, or null. Loading falls back to defaults on a
        /// bad file, and without this the fallback is indistinguishable from
        /// "nothing was ever saved".
        /// </summary>
        public static string LastLoadError { get; private set; }

        [DataMember(Name = "tolerancePercent", Order = 0)]
        public double TolerancePercent { get; set; }

        /// <summary>
        /// Height above the picked point the fan is placed at, in millimetres.
        /// Zero — place exactly where the user clicked — is the default because
        /// it is the only answer that is never surprising.
        /// </summary>
        [DataMember(Name = "mountingOffsetMm", Order = 1)]
        public double MountingOffsetMm { get; set; }

        [DataMember(Name = "families", Order = 2)]
        public List<FamilyMapping> Families { get; set; }

        /// <summary>
        /// Duct system type used for a generated stub, by name. Empty means "let
        /// the add-in choose": it then matches the fan connector's own system type,
        /// which is what Revit would have picked anyway.
        /// </summary>
        [DataMember(Name = "ductSystemType", Order = 3)]
        public string DuctSystemType { get; set; }

        /// <summary>Duct type used for a generated stub, by name. Empty means the first in the project.</summary>
        [DataMember(Name = "ductType", Order = 4)]
        public string DuctType { get; set; }

        public FanSettings()
        {
            TolerancePercent = DefaultTolerancePercent;
            Families = new List<FamilyMapping>();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Families == null) Families = new List<FamilyMapping>();
            if (TolerancePercent <= 0.0) TolerancePercent = DefaultTolerancePercent;
        }

        public double MountingOffsetFt
        {
            get
            {
                try { return UnitUtils.ConvertToInternalUnits(MountingOffsetMm, UnitTypeId.Millimeters); }
                catch { return 0.0; }
            }
        }

        public void SetMountingOffsetFt(double feet)
        {
            try { MountingOffsetMm = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters); }
            catch { MountingOffsetMm = 0.0; }
        }

        public FamilyMapping Find(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            foreach (FamilyMapping mapping in Families)
                if (string.Equals(mapping.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                    return mapping;
            return null;
        }

        // ── Locations ─────────────────────────────────────────────────────────

        private static string SharedPath
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FileName);
                }
                catch { return null; }
            }
        }

        private static string UserPath
        {
            get
            {
                try
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        Brand.DisplayName, Brand.ProductName, FileName);
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// The sample families the installer laid down beside the DLL, or null when
        /// that feature was skipped. The add-in never loads them itself — this is
        /// only so the Options dialog can tell a first-time user where to look.
        /// </summary>
        public static string SamplesFolder
        {
            get
            {
                try
                {
                    string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (string.IsNullOrEmpty(dir)) return null;
                    string samples = Path.Combine(dir, "Sample Families");
                    return Directory.Exists(samples) ? samples : null;
                }
                catch { return null; }
            }
        }

        /// <summary>Where the options are currently kept, for display in the dialog.</summary>
        public static string CurrentLocation
        {
            get
            {
                string user = UserPath;
                if (user != null && File.Exists(user)) return user;
                return SharedPath ?? user ?? string.Empty;
            }
        }

        // ── Load / Save ───────────────────────────────────────────────────────

        /// <summary>The stored options, or the defaults. Never throws.</summary>
        public static FanSettings Load()
        {
            LastLoadError = null;
            return ReadFrom(UserPath) ?? ReadFrom(SharedPath) ?? new FanSettings();
        }

        /// <summary>
        /// Write the options next to the DLL, falling back to a per-user copy when
        /// that folder is not writable for this account. False only if both
        /// attempts failed.
        /// </summary>
        public bool Save()
        {
            if (TryWrite(SharedPath))
            {
                // A per-user copy left over from an earlier failed shared write
                // would silently win the next Load and mask what was just saved.
                try
                {
                    string user = UserPath;
                    if (user != null && File.Exists(user)) File.Delete(user);
                }
                catch { /* leaving it is bad but not worth failing the save over */ }
                return true;
            }
            return TryWrite(UserPath);
        }

        private static FanSettings ReadFrom(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;

                // Skip a byte-order mark rather than choke on one: files written by
                // the version that added one are still out there, and a text editor
                // may well put one back.
                byte[] bytes = File.ReadAllBytes(path);
                int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                    ? 3 : 0;

                using (var stream = new MemoryStream(bytes, start, bytes.Length - start))
                {
                    var serializer = new DataContractJsonSerializer(typeof(FanSettings));
                    return serializer.ReadObject(stream) as FanSettings;
                }
            }
            catch (Exception exception)
            {
                LastLoadError = "The settings file could not be read, so the defaults are in use:\n"
                              + path + "\n\n" + exception.Message;
                return null;
            }
        }

        private bool TryWrite(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, ToJson(), Utf8NoBom);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Indented JSON — the file lives in a folder people open, and a mapping
        /// they can read and hand-edit is worth the four extra lines here.
        /// </summary>
        private string ToJson()
        {
            using (var buffer = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(
                           buffer, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(FanSettings)).WriteObject(writer, this);
                    writer.Flush();
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }
    }
}
