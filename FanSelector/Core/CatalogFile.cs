using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>One column of a type catalogue, as its header declares it.</summary>
    internal class CatalogColumn
    {
        public string Name { get; set; }
        public string SpecToken { get; set; }
        public string UnitToken { get; set; }

        /// <summary>Resolved from SpecToken; null for a text column.</summary>
        public ForgeTypeId Spec { get; set; }

        /// <summary>Resolved from UnitToken; null when the column carries no unit.</summary>
        public ForgeTypeId Unit { get; set; }

        /// <summary>
        /// False when the header names a unit this add-in does not know. The value
        /// is then taken at face value, which would be wrong by whatever the
        /// conversion factor was — so it is said out loud rather than assumed away.
        /// </summary>
        public bool UnitUnderstood { get; set; }

        public bool IsNumeric { get { return Unit != null || Spec != null; } }

        /// <summary>"AirFlow — Air Flow (m³/h)", for the mapping dropdowns.</summary>
        public string Label
        {
            get
            {
                // A nameless column is the line's own first cell, which has no
                // header of its own; SpecToken carries its description instead.
                if (Name == null) return "(" + SpecToken + ")";

                string kind = RevitUnits.SpecLabel(Spec);
                string label = string.IsNullOrEmpty(kind)
                    ? (string.IsNullOrEmpty(UnitToken) ? Name : Name + "  —  " + UnitToken)
                    : Name + "  —  " + kind;
                return UnitUnderstood ? label : label + "  ·  unit \"" + UnitToken + "\" not recognised";
            }
        }

        public override string ToString() { return Label; }
    }

    /// <summary>One line of a type catalogue: a family type and its values.</summary>
    internal class CatalogRow
    {
        /// <summary>
        /// The first cell of the line, which is what Revit names the family type
        /// when it loads the catalogue.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>Numeric values converted to Revit's internal units.</summary>
        public Dictionary<string, double> Numbers { get; private set; }

        /// <summary>Values exactly as the file spells them.</summary>
        public Dictionary<string, string> Raw { get; private set; }

        public CatalogRow()
        {
            Numbers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public double? Number(string column)
        {
            double value;
            return !string.IsNullOrEmpty(column) && Numbers.TryGetValue(column, out value)
                ? (double?)value : null;
        }

        public string Text(string column)
        {
            string value;
            return !string.IsNullOrEmpty(column) && Raw.TryGetValue(column, out value)
                ? value : string.Empty;
        }
    }

    /// <summary>
    /// A Revit type catalogue (.csv) read as a fan performance table.
    ///
    /// The same file serves two purposes and that is the whole point of it: Revit
    /// reads it to decide which types a family offers when it is loaded, and Fan
    /// Selector reads it to know what each of those types can do. One file, one
    /// source of truth, no separate database to keep in step.
    ///
    /// Format: the first header cell is empty, the rest are
    /// "Name##SPEC##UNIT"; every later line starts with the type name.
    /// </summary>
    internal class CatalogFile
    {
        private static readonly Dictionary<string, CatalogFile> Cache =
            new Dictionary<string, CatalogFile>(StringComparer.OrdinalIgnoreCase);

        public string Path { get; private set; }
        public List<CatalogColumn> Columns { get; private set; }
        public List<CatalogRow> Rows { get; private set; }

        /// <summary>Why the file could not be used, or null when it could.</summary>
        public string Problem { get; private set; }

        /// <summary>Lines that were not a usable row, for the status line.</summary>
        public int SkippedLines { get; private set; }

        public bool IsUsable { get { return Problem == null && Columns.Count > 0 && Rows.Count > 0; } }

        private CatalogFile()
        {
            Columns = new List<CatalogColumn>();
            Rows = new List<CatalogRow>();
        }

        public CatalogColumn Column(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Numeric columns only — the pool for a mappable figure.</summary>
        public List<CatalogColumn> NumericColumns()
        {
            return Columns.Where(c => c.IsNumeric).ToList();
        }

        /// <summary>
        /// The catalogue at a path, re-read whenever the file changes on disk so
        /// editing it does not need Revit restarted. Never throws.
        /// </summary>
        public static CatalogFile For(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Failed(path, "No catalogue file has been chosen for this family yet.");

            string key;
            try { key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
            catch { key = path; }

            CatalogFile cached;
            if (Cache.TryGetValue(key, out cached)) return cached;

            CatalogFile file = Read(path);
            Cache[key] = file;
            return file;
        }

        private static CatalogFile Read(string path)
        {
            string[] lines;
            try
            {
                if (!File.Exists(path))
                    return Failed(path, "The catalogue file was not found:\n" + path);
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                return Failed(path, "The catalogue file could not be read: " + exception.Message);
            }

            string header = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (header == null)
                return Failed(path, "The catalogue file is empty:\n" + path);

            char separator = Separator(header);
            var file = new CatalogFile { Path = path };

            List<string> headerCells = Split(header, separator);
            for (int i = 1; i < headerCells.Count; i++)
            {
                CatalogColumn column = ParseHeaderCell(headerCells[i]);
                if (column == null) continue;
                // A duplicate column name cannot be told apart when mapping, so the
                // first one wins rather than shadowing silently.
                if (file.Column(column.Name) == null) file.Columns.Add(column);
            }

            if (file.Columns.Count == 0)
                return Failed(path,
                    "No columns could be read from the catalogue header. A type catalogue's first header "
                    + "cell is empty and the rest look like \"AirFlow##HVAC_AIR_FLOW##CUBIC_METERS_PER_HOUR\".");

            bool pastHeader = false;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!pastHeader) { pastHeader = true; continue; }

                List<string> cells = Split(line, separator);
                if (cells.Count == 0 || string.IsNullOrWhiteSpace(cells[0])) { file.SkippedLines++; continue; }

                var row = new CatalogRow { TypeName = cells[0].Trim() };
                for (int i = 1; i < headerCells.Count && i < cells.Count; i++)
                {
                    CatalogColumn column = ParseHeaderCell(headerCells[i]);
                    if (column == null) continue;
                    column = file.Column(column.Name);
                    if (column == null) continue;

                    string text = cells[i].Trim();
                    if (text.Length == 0) continue;
                    row.Raw[column.Name] = text;

                    double value;
                    if (!TryParseNumber(text, out value)) continue;
                    if (column.Unit != null)
                    {
                        try { value = UnitUtils.ConvertToInternalUnits(value, column.Unit); }
                        catch { /* keep the raw number rather than losing the row */ }
                    }
                    row.Numbers[column.Name] = value;
                }

                file.Rows.Add(row);
            }

            if (file.Rows.Count == 0)
                return Failed(path, "The catalogue header was read but it has no type rows under it:\n" + path);

            return file;
        }

        private static CatalogColumn ParseHeaderCell(string cell)
        {
            if (cell == null) return null;
            string text = cell.Trim();
            if (text.Length == 0) return null;

            string[] parts = text.Split(new[] { "##" }, StringSplitOptions.None);
            string name = parts[0].Trim();
            if (name.Length == 0) return null;

            var column = new CatalogColumn
            {
                Name = name,
                SpecToken = parts.Length > 1 ? parts[1].Trim() : string.Empty,
                UnitToken = parts.Length > 2 ? parts[2].Trim() : string.Empty
            };
            column.Spec = CatalogTokens.Spec(column.SpecToken);
            column.Unit = CatalogTokens.Unit(column.UnitToken);
            column.UnitUnderstood = CatalogTokens.KnowsUnit(column.UnitToken);
            return column;
        }

        /// <summary>
        /// Revit writes a type catalogue with commas when the decimal separator is
        /// a point and with semicolons when it is a comma, so the separator has to
        /// be read off the file rather than assumed.
        /// </summary>
        private static char Separator(string header)
        {
            int commas = header.Count(c => c == ',');
            int semicolons = header.Count(c => c == ';');
            int tabs = header.Count(c => c == '\t');

            if (semicolons > commas && semicolons >= tabs) return ';';
            if (tabs > commas && tabs > semicolons) return '\t';
            return ',';
        }

        private static List<string> Split(string line, char separator)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    // "" inside a quoted field is a literal quote.
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == separator && !quoted)
                {
                    cells.Add(cell.ToString());
                    cell.Length = 0;
                }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }

        /// <summary>
        /// Invariant first, then with a comma read as the decimal separator — a
        /// semicolon-separated catalogue writes 0,37 where a comma-separated one
        /// writes 0.37, and reading either with the current culture is how a
        /// motor turns into a 37 kW one.
        /// </summary>
        private static bool TryParseNumber(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            return text.IndexOf(',') >= 0
                && double.TryParse(text.Replace(',', '.'), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out value);
        }

        private static CatalogFile Failed(string path, string problem)
        {
            return new CatalogFile { Path = path, Problem = problem };
        }
    }
}
