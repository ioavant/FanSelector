using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using FanSelector.Core;
// Autodesk.Revit.DB has its own Grid (a modelled grid line), so the name is
// ambiguous once the Revit namespace is in scope. Here it is always the WPF one.
using Grid = System.Windows.Controls.Grid;

namespace FanSelector.UI
{
    /// <summary>
    /// Where the user wires up a fan family: which column of its type catalogue
    /// carries each figure, and which parameter of the family each figure should
    /// be written to when a fan is placed. Nothing here is specific to any
    /// manufacturer — that is the entire point of the dialog.
    /// </summary>
    internal partial class OptionsWindow : Window
    {
        private readonly Document _doc;
        private readonly Units _units;
        private readonly Dictionary<FanQuantity, ComboBox> _columnCombos =
            new Dictionary<FanQuantity, ComboBox>();
        private readonly Dictionary<FanQuantity, ComboBox> _paramCombos =
            new Dictionary<FanQuantity, ComboBox>();

        /// <summary>Working copy; the caller's settings are only touched on OK.</summary>
        public FanSettings Settings { get; private set; }

        private FamilyMapping _current;
        private CatalogFile _file;
        private FamilyCatalog _catalog;
        private bool _loading;

        /// <param name="notice">Optional line explaining why the dialog opened by itself.</param>
        public OptionsWindow(Document doc, FanSettings settings, string notice)
        {
            InitializeComponent();

            _doc = doc;
            _units = doc.GetUnits();
            Settings = Copy(settings);

            // A family edited since the last time the dialog was open should be
            // read again rather than served from the session cache.
            FamilyCatalog.Forget();

            Title = Brand.FullProductName + " Options";
            TitleText.Text = Brand.ProductDisplayName + " Options  —  v" + UpdateChecker.CurrentVersion;
            LogoImage.Source = WindowSupport.Image("logo.png");
            if (LogoImage.Source == null) LogoImage.Visibility = System.Windows.Visibility.Collapsed;

            string banner = WindowSupport.UpdateBannerText();
            if (banner != null)
            {
                BannerText.Text = banner;
                BannerBorder.Visibility = System.Windows.Visibility.Visible;
            }

            // A settings file that would not load matters more than any other
            // notice: it is the difference between "nothing saved" and "lost".
            string trouble = FanSettings.LastLoadError ?? notice;
            if (!string.IsNullOrEmpty(trouble))
            {
                NoticeText.Text = trouble;
                NoticeBorder.Visibility = System.Windows.Visibility.Visible;
            }

            BuildMappingRows();

            ToleranceBox.Text = Settings.TolerancePercent.ToString("0.#");
            OffsetBox.Text = RevitUnits.Format(_units, SpecTypeId.Length, Settings.MountingOffsetFt);
            OffsetUnitText.Text = RevitUnits.Symbol(_units, SpecTypeId.Length);
            StubUnitText.Text = RevitUnits.Symbol(_units, SpecTypeId.Length);

            FillAutomatic(SystemTypeBox, ParameterScanner.DuctSystemTypes(_doc), Settings.DuctSystemType);
            FillAutomatic(DuctTypeBox, ParameterScanner.DuctTypes(_doc), Settings.DuctType);
            CloserBox.ItemsSource = ParameterScanner.AirTerminalFamilies(_doc);
            LocationText.Text = "Shared by every Revit version on this machine, stored in:\n"
                              + FanSettings.CurrentLocation;

            string samples = FanSettings.SamplesFolder;
            if (samples != null)
                FamilyHint.Text += "\nSample fan families and their catalogues are in:\n" + samples;

            RefreshFamilyList(Settings.Families.FirstOrDefault());
        }

        /// <summary>
        /// A dropdown of project elements with "(automatic)" in front. Leaving it
        /// on automatic is a real answer, not an unfinished setting: the stub then
        /// takes the system type of the connector it grows from.
        /// </summary>
        private const string Automatic = "(automatic)";

        private static void FillAutomatic(ComboBox combo, List<string> names, string stored)
        {
            var items = new List<string> { Automatic };
            items.AddRange(names);
            if (!string.IsNullOrEmpty(stored) && !names.Contains(stored, StringComparer.OrdinalIgnoreCase))
                items.Add(stored);   // keep a name this project happens not to have

            combo.ItemsSource = items;
            combo.SelectedItem = string.IsNullOrEmpty(stored)
                ? Automatic
                : items.FirstOrDefault(n => string.Equals(n, stored, StringComparison.OrdinalIgnoreCase))
                  ?? Automatic;
        }

        private static string Chosen(ComboBox combo)
        {
            string value = combo.SelectedItem as string;
            return string.IsNullOrEmpty(value) || value == Automatic ? null : value;
        }

        private static FanSettings Copy(FanSettings source)
        {
            var copy = new FanSettings
            {
                TolerancePercent = source.TolerancePercent,
                MountingOffsetMm = source.MountingOffsetMm,
                DuctSystemType = source.DuctSystemType,
                DuctType = source.DuctType
            };
            foreach (FamilyMapping mapping in source.Families)
                copy.Families.Add(mapping.Clone());
            return copy;
        }

        // ── Layout built from the quantity table ──────────────────────────────

        private void BuildMappingRows()
        {
            QuantityInfo[] all = Quantities.All;
            for (int i = 0; i < all.Length; i++)
            {
                QuantityInfo info = all[i];
                int row = i + 1;   // row 0 holds the two column headings
                MappingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = info.DisplayName + (info.Required ? " *" : ""),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 6)
                };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 0);
                MappingGrid.Children.Add(label);

                ComboBox columnCombo = AddCombo(row, 1, info, OnColumnChanged);
                ComboBox paramCombo = AddCombo(row, 3, info, OnParamChanged);
                _columnCombos[info.Quantity] = columnCombo;
                _paramCombos[info.Quantity] = paramCombo;
            }

            MappingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var footnote = new TextBlock
            {
                Text = "* required — the search filters on air flow and pressure.",
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(footnote, all.Length + 1);
            Grid.SetColumn(footnote, 1);
            MappingGrid.Children.Add(footnote);
        }

        private ComboBox AddCombo(int row, int column, QuantityInfo info, SelectionChangedEventHandler handler)
        {
            var combo = new ComboBox
            {
                DisplayMemberPath = "Label",
                Margin = new Thickness(0, 0, 0, 6),
                Tag = info
            };
            combo.SelectionChanged += handler;
            Grid.SetRow(combo, row);
            Grid.SetColumn(combo, column);
            MappingGrid.Children.Add(combo);
            return combo;
        }

        // ── Family list ───────────────────────────────────────────────────────

        private void RefreshFamilyList(FamilyMapping select)
        {
            _loading = true;
            FamilyList.ItemsSource = null;
            FamilyList.ItemsSource = Settings.Families;

            List<string> configured = Settings.Families.Select(f => f.FamilyName).ToList();
            AddFamilyBox.ItemsSource = ParameterScanner.EquipmentFamilies(_doc)
                .Where(n => !configured.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();
            _loading = false;

            if (select != null) FamilyList.SelectedItem = select;
            else if (Settings.Families.Count > 0) FamilyList.SelectedIndex = 0;
            else LoadMapping(null);
        }

        private void OnFamilySelected(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            LoadMapping(FamilyList.SelectedItem as FamilyMapping);
        }

        private void OnAddFamily(object sender, RoutedEventArgs e)
        {
            string family = AddFamilyBox.SelectedItem as string;
            if (string.IsNullOrEmpty(family))
            {
                Warn("Pick a family from the list first.\n\n"
                     + "The list holds the Mechanical Equipment families loaded in this project. "
                     + "If the fan family you want is missing, load it into the project and reopen this dialog.");
                return;
            }

            var mapping = new FamilyMapping
            {
                FamilyName = family,
                CatalogPath = Beside(family, ".csv")
            };
            Settings.Families.Add(mapping);
            RefreshFamilyList(mapping);
            GuessMapping();
            LoadMapping(mapping);
        }

        /// <summary>
        /// A file named after the family in the folder the sample families were
        /// installed to. Saves the common case a trip through Browse.
        /// </summary>
        private static string Beside(string familyName, string extension)
        {
            try
            {
                string samples = FanSettings.SamplesFolder;
                if (samples == null) return null;
                string candidate = Path.Combine(samples, familyName + extension);
                return File.Exists(candidate) ? candidate : null;
            }
            catch { return null; }
        }

        private void OnRemoveFamily(object sender, RoutedEventArgs e)
        {
            var mapping = FamilyList.SelectedItem as FamilyMapping;
            if (mapping == null) return;
            Settings.Families.Remove(mapping);
            RefreshFamilyList(null);
        }

        /// <summary>Pre-fill what can be worked out from the catalogue and the family.</summary>
        private void GuessMapping()
        {
            if (_current == null) return;

            CatalogFile file = CatalogFile.For(_current.CatalogPath);
            FamilyCatalog catalog = FamilyCatalog.For(_doc, _current.FamilyName);

            if (string.IsNullOrEmpty(_current.TypeColumn))
                _current.TypeColumn = ParameterScanner.GuessTypeColumn(_doc, _current.FamilyName, file);

            // A column already claimed by one figure is off the table for the
            // next, so speed, SFP and sound power cannot all land on RPM.
            var takenColumns = new List<string>();
            foreach (QuantityInfo info in Quantities.All)
            {
                string existing = _current.Column(info.Quantity);
                if (!string.IsNullOrEmpty(existing)) { takenColumns.Add(existing); continue; }

                string guess = ParameterScanner.GuessColumn(file, info, takenColumns);
                _current.SetColumn(info.Quantity, guess);
                if (!string.IsNullOrEmpty(guess)) takenColumns.Add(guess);
            }

            foreach (QuantityInfo info in Quantities.All)
                if (string.IsNullOrEmpty(_current.Param(info.Quantity)))
                    _current.SetParam(info.Quantity, ParameterScanner.GuessParam(
                        _doc, _current.FamilyName, catalog, info, _units));
        }

        // ── Catalogue file ────────────────────────────────────────────────────

        private void OnBrowseCatalog(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the type catalogue for " + _current.FamilyName,
                Filter = "Type catalogue (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };

            try
            {
                if (!string.IsNullOrEmpty(_current.CatalogPath))
                    dialog.InitialDirectory = Path.GetDirectoryName(_current.CatalogPath);
                else if (FanSettings.SamplesFolder != null)
                    dialog.InitialDirectory = FanSettings.SamplesFolder;
            }
            catch { /* a bad remembered folder is not worth failing over */ }

            if (dialog.ShowDialog(this) != true) return;

            CatalogBox.Text = dialog.FileName;   // TextChanged does the rest
        }

        private void OnCatalogTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _current == null) return;
            _current.CatalogPath = string.IsNullOrWhiteSpace(CatalogBox.Text)
                ? null : CatalogBox.Text.Trim();
            GuessMapping();
            LoadMapping(_current);
        }

        // ── Which column names the Revit type ─────────────────────────────────

        /// <summary>The first column of every line, offered as "the designation".</summary>
        private static readonly CatalogColumn FirstColumn =
            new CatalogColumn { Name = null, SpecToken = "the line's own designation" };

        private void FillTypeColumn()
        {
            var choices = new List<CatalogColumn> { FirstColumn };
            if (_file != null && _file.IsUsable) choices.AddRange(_file.Columns);

            TypeColumnBox.ItemsSource = choices;
            TypeColumnBox.SelectedItem = choices.FirstOrDefault(
                c => c.Name != null &&
                     string.Equals(c.Name, _current.TypeColumn, StringComparison.OrdinalIgnoreCase))
                ?? FirstColumn;
            TypeColumnBox.IsEnabled = _file != null && _file.IsUsable;

            ShowTypeColumnStatus();
        }

        /// <summary>
        /// How many catalogue lines currently name a type the project has. This is
        /// the one mapping whose correctness can be measured rather than argued
        /// about, so it is measured.
        /// </summary>
        private void ShowTypeColumnStatus()
        {
            if (_current == null || _file == null || !_file.IsUsable)
            {
                TypeColumnStatus.Text = string.Empty;
                return;
            }

            var loaded = new HashSet<string>(
                ParameterScanner.SymbolsOf(_doc, _current.FamilyName).Select(s => s.Name),
                StringComparer.OrdinalIgnoreCase);

            int hits = _file.Rows.Count(
                r => loaded.Contains(FanSearch.TypeNameFor(_current.TypeColumn, r) ?? string.Empty));

            if (loaded.Count == 0)
            {
                TypeColumnStatus.Text = "The family has no types in this project, so this cannot be "
                                      + "checked here — the family file will be loaded when a fan is placed.";
                return;
            }

            TypeColumnStatus.Text = hits + " of " + _file.Rows.Count
                + " catalogue lines name a type this project has."
                + (hits == 0
                    ? "  None do — this is almost certainly the wrong column."
                    : string.Empty);
        }

        private void OnTypeColumnChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _current == null) return;
            var column = TypeColumnBox.SelectedItem as CatalogColumn;
            _current.TypeColumn = column == null ? null : column.Name;
            ShowTypeColumnStatus();
        }

        /// <summary>
        /// Which of the four built-in pictures this family lands on, so the choice
        /// is visible even though there is nothing to configure about it.
        /// </summary>
        private void ShowPictureHint()
        {
            if (_current == null) { PictureHint.Text = string.Empty; return; }

            string picture = FanImages.For(_current.FamilyName);
            string described = picture == FanImages.Axial ? "the axial fan picture"
                             : picture == FanImages.Centrifugal ? "the centrifugal fan picture"
                             : picture == FanImages.InLine ? "the in-line fan picture"
                             : "the general fan picture, since the name says none of "
                               + "\"axial\", \"centrifugal\" or \"in-line\"";
            PictureHint.Text = "Beside the results this family shows " + described + ".";
        }

        // ── Duct stub ─────────────────────────────────────────────────────────

        private void OnStubToggled(object sender, RoutedEventArgs e)
        {
            if (_loading || _current == null) return;
            _current.AddDuctStub = StubBox.IsChecked == true;
            StubDetails.IsEnabled = _current.AddDuctStub;
        }

        /// <summary>
        /// The closer name and the stub length are free text, so they have no
        /// event that writes them back. Committing them before the panel reloads
        /// is what stops a typed value from being lost by switching family.
        /// </summary>
        private void CommitStubFields()
        {
            if (_loading || _current == null) return;

            _current.CloserFamily = string.IsNullOrWhiteSpace(CloserBox.Text)
                ? FamilyMapping.DefaultCloserFamily : CloserBox.Text.Trim();

            double feet;
            if (RevitUnits.TryParse(_units, SpecTypeId.Length, StubLengthBox.Text, out feet) && feet > 0.0)
                _current.SetStubLengthFt(feet);
        }

        // ── Mapping panel ─────────────────────────────────────────────────────

        private void LoadMapping(FamilyMapping mapping)
        {
            CommitStubFields();

            _current = mapping;
            _file = null;
            _catalog = null;
            MappingPanel.IsEnabled = mapping != null;
            MappingWarnBorder.Visibility = System.Windows.Visibility.Collapsed;

            if (mapping == null)
            {
                MappingHeader.Text = "Parameter mapping";
                CatalogBox.Text = string.Empty;
                CatalogStatus.Text = string.Empty;
                foreach (ComboBox combo in _columnCombos.Values) combo.ItemsSource = null;
                foreach (ComboBox combo in _paramCombos.Values) combo.ItemsSource = null;
                ExtraList.ItemsSource = null;
                ExtraPicker.ItemsSource = null;
                TypeColumnBox.ItemsSource = null;
                TypeColumnStatus.Text = string.Empty;
                PictureHint.Text = string.Empty;
                return;
            }

            MappingHeader.Text = "Parameter mapping for \"" + mapping.FamilyName + "\"";

            _loading = true;
            try
            {
                CatalogBox.Text = mapping.CatalogPath ?? string.Empty;
                ShowPictureHint();

                StubBox.IsChecked = mapping.AddDuctStub;
                StubDetails.IsEnabled = mapping.AddDuctStub;
                CloserBox.Text = mapping.CloserFamily ?? FamilyMapping.DefaultCloserFamily;
                StubLengthBox.Text = RevitUnits.Format(_units, SpecTypeId.Length, mapping.StubLengthFt);

                _file = CatalogFile.For(mapping.CatalogPath);

                // Fill in a type column the stored mapping never had — the setting
                // is newer than mappings people already saved.
                if (string.IsNullOrEmpty(mapping.TypeColumn))
                    mapping.TypeColumn = ParameterScanner.GuessTypeColumn(_doc, mapping.FamilyName, _file);

                // Reading the family definition is the slow part, and it is only
                // needed for the "write onto the fan" half.
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                try { _catalog = FamilyCatalog.For(_doc, mapping.FamilyName); }
                finally { System.Windows.Input.Mouse.OverrideCursor = null; }

                CatalogStatus.Text = _file.IsUsable
                    ? _file.Rows.Count + " lines, " + _file.Columns.Count + " columns"
                      + (_file.SkippedLines > 0 ? "   (" + _file.SkippedLines + " lines skipped)" : "")
                    : _file.Problem;

                FillTypeColumn();

                bool showAll = ShowAllBox.IsChecked == true;
                foreach (QuantityInfo info in Quantities.All)
                {
                    FillColumnCombo(_columnCombos[info.Quantity], info, showAll);
                    FillParamCombo(_paramCombos[info.Quantity], info, showAll);
                }

                ExtraList.ItemsSource = new List<string>(mapping.ExtraColumns);
                ExtraPicker.ItemsSource = _file.IsUsable ? _file.Columns : new List<CatalogColumn>();

                ShowDiagnosis();
            }
            finally { _loading = false; }
        }

        private void FillColumnCombo(ComboBox combo, QuantityInfo info, bool showAll)
        {
            string stored = _current.Column(info.Quantity);
            var choices = new List<CatalogColumn> { null };   // null == "(not mapped)"
            choices.AddRange(ParameterScanner.Columns(_file, info, showAll));

            if (!string.IsNullOrEmpty(stored) &&
                !choices.Any(c => c != null && string.Equals(c.Name, stored, StringComparison.OrdinalIgnoreCase)))
            {
                // Whatever the filter thinks, the user already chose this one.
                // Dropping it here would silently unmap it on OK.
                choices.Add(new CatalogColumn { Name = stored, SpecToken = "(not in this catalogue)" });
            }

            combo.ItemsSource = choices;
            combo.ItemTemplate = null;
            combo.DisplayMemberPath = "Label";
            combo.SelectedItem = choices.FirstOrDefault(
                c => c != null && string.Equals(c.Name, stored, StringComparison.OrdinalIgnoreCase));
            if (combo.SelectedItem == null) combo.SelectedIndex = 0;
            combo.IsEnabled = _file != null && _file.IsUsable;
        }

        private void FillParamCombo(ComboBox combo, QuantityInfo info, bool showAll)
        {
            string stored = _current.Param(info.Quantity);
            var choices = new List<ParamChoice> { ParamChoice.None() };
            choices.AddRange(ParameterScanner.Params(
                _doc, _current.FamilyName, _catalog, info, showAll, _units));

            if (!string.IsNullOrEmpty(stored) &&
                !choices.Any(c => string.Equals(c.Name, stored, StringComparison.OrdinalIgnoreCase)))
            {
                // The list holds only parameters that can actually be written, so
                // a stored name missing from it is usually a read-only one — say
                // which, rather than leaving the user to find out at placement.
                choices.Add(new ParamChoice
                {
                    Name = stored,
                    SpecLabel = "(read-only, or not on this family — cannot be written)"
                });
            }

            combo.ItemsSource = choices;
            combo.DisplayMemberPath = "Label";
            combo.SelectedItem = choices.FirstOrDefault(
                c => string.Equals(c.Name, stored, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            combo.IsEnabled = true;
        }

        private void OnColumnChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _current == null) return;
            var combo = (ComboBox)sender;
            var info = (QuantityInfo)combo.Tag;
            var column = combo.SelectedItem as CatalogColumn;
            _current.SetColumn(info.Quantity, column == null ? null : column.Name);
        }

        private void OnParamChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _current == null) return;
            var combo = (ComboBox)sender;
            var info = (QuantityInfo)combo.Tag;
            var choice = combo.SelectedItem as ParamChoice;
            _current.SetParam(info.Quantity, choice == null || choice.IsNone ? null : choice.Name);
        }

        private void OnShowAllChanged(object sender, RoutedEventArgs e)
        {
            LoadMapping(_current);
        }

        /// <summary>
        /// Spell out what the catalogue does not offer. The required figures get a
        /// full explanation, the optional ones are just named, so the panel does
        /// not turn into a wall of warnings.
        /// </summary>
        private void ShowDiagnosis()
        {
            if (_catalog != null && !_catalog.IsUsable)
            {
                MappingWarnText.Text = _catalog.Problem
                    + "\n\nThe search itself does not need this — it reads the catalogue. Only the "
                    + "\"write onto the placed fan\" column is unavailable.";
                MappingWarnBorder.Visibility = System.Windows.Visibility.Visible;
                return;
            }

            var explained = new List<string>();
            var alsoMissing = new List<string>();

            foreach (QuantityInfo info in Quantities.All)
            {
                // A mapped column whose unit is a mystery is worse than an
                // unmapped one: it produces a number that looks right.
                CatalogColumn mapped = _file == null ? null : _file.Column(_current.Column(info.Quantity));
                if (mapped != null && !mapped.UnitUnderstood)
                    explained.Add(info.DisplayName + ": the catalogue declares column \"" + mapped.Name
                                  + "\" in \"" + mapped.UnitToken + "\", which this add-in does not know. "
                                  + "Its values are used exactly as written, so they are only right if the "
                                  + "catalogue already holds them in Revit's own units.");

                string problem = ParameterScanner.DiagnoseColumn(_file, info);
                if (problem == null) continue;
                if (info.Required) explained.Add(problem);
                else alsoMissing.Add(info.DisplayName);
            }

            if (explained.Count == 0 && alsoMissing.Count == 0)
            {
                MappingWarnBorder.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            var text = new List<string>(explained);
            if (alsoMissing.Count > 0)
                text.Add("No column fits these either, which is fine if the catalogue simply does not carry "
                         + "them: " + string.Join(", ", alsoMissing) + ".");

            MappingWarnText.Text = string.Join("\n\n", text);
            MappingWarnBorder.Visibility = System.Windows.Visibility.Visible;
        }

        private void OnCopyParameters(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            string dump = ParameterScanner.Dump(_doc, _current, _file, _catalog, _units);
            try
            {
                // SetDataObject with copy:true, not SetText: the clipboard is a
                // shared resource and Revit is not the only thing touching it.
                Clipboard.SetDataObject(dump, true);
                MessageBox.Show(this,
                    "The details for \"" + _current.FamilyName + "\" are on the clipboard.\n\n"
                    + "Paste them anywhere to see the catalogue's columns and first rows, and every "
                    + "parameter the family declares.",
                    Brand.FullProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "The clipboard could not be written to: " + exception.Message,
                                Brand.FullProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Extra columns ─────────────────────────────────────────────────────

        private void OnAddExtra(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var column = ExtraPicker.SelectedItem as CatalogColumn;
            if (column == null || string.IsNullOrEmpty(column.Name)) return;

            if (_current.ExtraColumns.Contains(column.Name, StringComparer.OrdinalIgnoreCase)) return;
            _current.ExtraColumns.Add(column.Name);
            ExtraList.ItemsSource = new List<string>(_current.ExtraColumns);
        }

        private void OnRemoveExtra(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var name = ExtraList.SelectedItem as string;
            if (name == null) return;
            _current.ExtraColumns.Remove(name);
            ExtraList.ItemsSource = new List<string>(_current.ExtraColumns);
        }

        // ── Commit ────────────────────────────────────────────────────────────

        private void OnOk(object sender, RoutedEventArgs e)
        {
            CommitStubFields();

            double tolerance;
            if (!double.TryParse(ToleranceBox.Text, out tolerance) || tolerance <= 0.0 || tolerance > 100.0)
            {
                Warn("The default tolerance has to be a percentage between 0 and 100, for example 10.");
                ToleranceBox.Focus();
                ToleranceBox.SelectAll();
                return;
            }

            double offsetFt;
            if (!RevitUnits.TryParse(_units, SpecTypeId.Length, OffsetBox.Text, out offsetFt))
            {
                Warn("The mounting offset has to be a length in this project's units, for example \""
                     + RevitUnits.Format(_units, SpecTypeId.Length, 0.0) + "\".");
                OffsetBox.Focus();
                OffsetBox.SelectAll();
                return;
            }

            FamilyMapping badStub = Settings.Families.FirstOrDefault(
                f => f.AddDuctStub && string.IsNullOrEmpty(f.CloserFamily));
            if (badStub != null)
            {
                FamilyList.SelectedItem = badStub;
                Warn("\"" + badStub.FamilyName + "\" is set to grow a duct stub but has no closer family "
                     + "named.\n\nName the air terminal family that caps the stub, or switch the stub off.");
                return;
            }

            FamilyMapping incomplete = Settings.Families.FirstOrDefault(f => !f.IsUsable);
            if (incomplete != null)
            {
                FamilyList.SelectedItem = incomplete;
                Warn("\"" + incomplete.FamilyName + "\" is not ready to use yet.\n\n"
                     + "It needs a type catalogue file, and an air flow and a pressure column mapped from "
                     + "it — those two are what the search filters on. Finish it, or remove the family "
                     + "from the list.");
                return;
            }

            Settings.TolerancePercent = tolerance;
            Settings.SetMountingOffsetFt(offsetFt);
            Settings.DuctSystemType = Chosen(SystemTypeBox);
            Settings.DuctType = Chosen(DuctTypeBox);
            DialogResult = true;
        }

        private void OnBannerClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            WindowSupport.OpenUpdatePage();
        }

        private void Warn(string text)
        {
            MessageBox.Show(this, text, Brand.FullProductName,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
