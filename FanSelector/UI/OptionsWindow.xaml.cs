using System;
using System.Collections.Generic;
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
    /// Where the user tells Fan Selector what the parameters of their own fan
    /// families mean. Nothing here is specific to any manufacturer — that is the
    /// entire point of the dialog.
    /// </summary>
    internal partial class OptionsWindow : Window
    {
        private readonly Document _doc;
        private readonly Units _units;
        private readonly Dictionary<FanQuantity, ComboBox> _combos =
            new Dictionary<FanQuantity, ComboBox>();

        /// <summary>Working copy; the caller's settings are only touched on OK.</summary>
        public FanSettings Settings { get; private set; }

        private FamilyMapping _current;
        private bool _loading;

        /// <param name="notice">Optional line explaining why the dialog opened by itself.</param>
        public OptionsWindow(Document doc, FanSettings settings, string notice)
        {
            InitializeComponent();

            _doc = doc;
            _units = doc.GetUnits();
            Settings = Copy(settings);

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

            if (!string.IsNullOrEmpty(notice))
            {
                NoticeText.Text = notice;
                NoticeBorder.Visibility = System.Windows.Visibility.Visible;
            }

            BuildMappingRows();

            ToleranceBox.Text = Settings.TolerancePercent.ToString("0.#");
            OffsetBox.Text = RevitUnits.Format(_units, SpecTypeId.Length, Settings.MountingOffsetFt);
            OffsetUnitText.Text = RevitUnits.Symbol(_units, SpecTypeId.Length);
            LocationText.Text = "Shared by every Revit version on this machine, stored in:\n"
                              + FanSettings.CurrentLocation;

            string samples = FanSettings.SamplesFolder;
            if (samples != null)
                FamilyHint.Text += "\nSample fan families to try this against were installed in:\n" + samples;

            RefreshFamilyList(Settings.Families.FirstOrDefault());
        }

        private static FanSettings Copy(FanSettings source)
        {
            var copy = new FanSettings
            {
                TolerancePercent = source.TolerancePercent,
                MountingOffsetMm = source.MountingOffsetMm
            };
            foreach (FamilyMapping mapping in source.Families)
                copy.Families.Add(mapping.Clone());
            return copy;
        }

        // ── Layout built from the quantity table ──────────────────────────────

        private void BuildMappingRows()
        {
            QuantityInfo[] all = Quantities.All;
            for (int row = 0; row < all.Length; row++)
            {
                QuantityInfo info = all[row];
                MappingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = info.DisplayName + (info.Required ? " *" : ""),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 6)
                };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 0);

                var combo = new ComboBox
                {
                    DisplayMemberPath = "Label",
                    Margin = new Thickness(0, 0, 0, 6),
                    Tag = info
                };
                combo.SelectionChanged += OnMappingChanged;
                Grid.SetRow(combo, row);
                Grid.SetColumn(combo, 1);

                MappingGrid.Children.Add(label);
                MappingGrid.Children.Add(combo);
                _combos[info.Quantity] = combo;
            }

            MappingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var footnote = new TextBlock
            {
                Text = "* required — the search filters on air flow and pressure.",
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(footnote, all.Length);
            Grid.SetColumn(footnote, 1);
            MappingGrid.Children.Add(footnote);
        }

        // ── Family list ───────────────────────────────────────────────────────

        private void RefreshFamilyList(FamilyMapping select)
        {
            _loading = true;
            FamilyList.ItemsSource = null;
            FamilyList.ItemsSource = Settings.Families;

            List<string> configured = Settings.Families
                .Select(f => f.FamilyName)
                .ToList();
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

            var mapping = new FamilyMapping { FamilyName = family };
            GuessMapping(mapping);
            Settings.Families.Add(mapping);
            RefreshFamilyList(mapping);
        }

        private void OnRemoveFamily(object sender, RoutedEventArgs e)
        {
            var mapping = FamilyList.SelectedItem as FamilyMapping;
            if (mapping == null) return;
            Settings.Families.Remove(mapping);
            RefreshFamilyList(null);
        }

        /// <summary>Pre-fill what can be worked out from the family's own parameters.</summary>
        private void GuessMapping(FamilyMapping mapping)
        {
            FamilySymbol probe = ParameterScanner.ProbeSymbol(_doc, mapping.FamilyName);
            if (probe == null) return;
            foreach (QuantityInfo info in Quantities.All)
                mapping.Set(info.Quantity, ParameterScanner.Guess(probe, info));
        }

        // ── Mapping panel ─────────────────────────────────────────────────────

        /// <summary>
        /// The instance-parameter box is free text, so it has no SelectionChanged
        /// to write itself back. Committing it whenever the panel is about to be
        /// reloaded is what stops a typed name from being lost by switching family.
        /// </summary>
        private void CommitInstanceBox()
        {
            if (_loading || _current == null) return;
            _current.InstanceAirFlow = string.IsNullOrWhiteSpace(InstanceBox.Text)
                ? null : InstanceBox.Text.Trim();
        }

        private void LoadMapping(FamilyMapping mapping)
        {
            CommitInstanceBox();

            _current = mapping;
            MappingPanel.IsEnabled = mapping != null;
            MappingWarnBorder.Visibility = System.Windows.Visibility.Collapsed;
            CopyParamsButton.IsEnabled = mapping != null;

            if (mapping == null)
            {
                MappingHeader.Text = "Parameter mapping";
                foreach (ComboBox combo in _combos.Values) combo.ItemsSource = null;
                ExtraList.ItemsSource = null;
                ExtraPicker.ItemsSource = null;
                InstanceBox.ItemsSource = null;
                InstanceBox.Text = string.Empty;
                InstanceHint.Text = string.Empty;
                return;
            }

            FamilySymbol probe = ParameterScanner.ProbeSymbol(_doc, mapping.FamilyName);
            MappingHeader.Text = "Parameter mapping for \"" + mapping.FamilyName + "\"";

            _loading = true;
            try
            {
                if (probe == null)
                {
                    // The mapping was made against a project that had the family
                    // loaded. Keep it exactly as it is rather than blanking it.
                    MappingHeader.Text += "  —  not loaded in this project";
                    foreach (QuantityInfo info in Quantities.All)
                    {
                        ComboBox combo = _combos[info.Quantity];
                        combo.ItemsSource = Stored(mapping.Get(info.Quantity));
                        combo.SelectedIndex = combo.Items.Count - 1;
                        combo.IsEnabled = false;
                    }
                    ExtraList.ItemsSource = new List<string>(mapping.ExtraColumns);
                    ExtraPicker.ItemsSource = null;
                    InstanceBox.ItemsSource = null;
                    InstanceBox.Text = mapping.InstanceAirFlow ?? string.Empty;
                    InstanceHint.Text = "Load the family into this project to edit its mapping.";
                    return;
                }

                bool showAll = ShowAllBox.IsChecked == true;
                foreach (QuantityInfo info in Quantities.All)
                {
                    ComboBox combo = _combos[info.Quantity];
                    combo.IsEnabled = true;

                    var choices = new List<ParamChoice> { ParamChoice.None() };
                    choices.AddRange(ParameterScanner.Choices(probe, info, showAll, _units));

                    string stored = mapping.Get(info.Quantity);
                    if (!string.IsNullOrEmpty(stored) &&
                        !choices.Any(c => string.Equals(c.Name, stored, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Whatever the filter thinks, the user already chose this
                        // one. Dropping it here would silently unmap it on OK.
                        choices.Add(new ParamChoice
                        {
                            Name = stored,
                            SpecLabel = "(not found on this family)"
                        });
                    }

                    combo.ItemsSource = choices;
                    combo.SelectedItem = choices.FirstOrDefault(
                        c => string.Equals(c.Name, stored, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
                }

                ExtraList.ItemsSource = new List<string>(mapping.ExtraColumns);
                ExtraPicker.ItemsSource = ParameterScanner.AllChoices(probe, _units);

                FamilyInstance instance = ParameterScanner.ProbeInstance(_doc, mapping.FamilyName);
                InstanceBox.ItemsSource = instance == null
                    ? new List<string>()
                    : ParameterScanner.AllChoices(instance, _units).Select(c => c.Name).ToList();
                InstanceBox.Text = mapping.InstanceAirFlow ?? string.Empty;
                InstanceHint.Text = instance == null
                    ? "This project has no instance of the family yet, so the name has to be typed."
                    : string.Empty;

                ShowDiagnosis(probe, instance);
            }
            finally { _loading = false; }
        }

        /// <summary>
        /// Spell out what the family does not offer. The required figures get a
        /// full explanation — including the common case of the value living on the
        /// instance rather than the type — and the optional ones are just named,
        /// so the panel does not turn into a wall of warnings.
        /// </summary>
        private void ShowDiagnosis(FamilySymbol probe, FamilyInstance instance)
        {
            var explained = new List<string>();
            var alsoMissing = new List<string>();

            foreach (QuantityInfo info in Quantities.All)
            {
                string problem = ParameterScanner.Diagnose(probe, instance, info);
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
                text.Add("Nothing fitting for these either, which is fine if the family simply has no such "
                         + "data: " + string.Join(", ", alsoMissing) + ".");

            MappingWarnText.Text = string.Join("\n\n", text);
            MappingWarnBorder.Visibility = System.Windows.Visibility.Visible;
        }

        private void OnCopyParameters(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            string dump = ParameterScanner.Dump(_doc, _current.FamilyName, _units);
            try
            {
                // SetDataObject with copy:true, not SetText: the clipboard is a
                // shared resource and Revit is not the only thing touching it.
                Clipboard.SetDataObject(dump, true);
                MessageBox.Show(this,
                    "The parameter list of \"" + _current.FamilyName + "\" is on the clipboard.\n\n"
                    + "Paste it anywhere to see every parameter of the family with its kind, unit and "
                    + "current value — including whether it belongs to the type or to the instance.",
                    Brand.FullProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "The clipboard could not be written to: " + exception.Message,
                                Brand.FullProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static List<ParamChoice> Stored(string name)
        {
            var choices = new List<ParamChoice> { ParamChoice.None() };
            if (!string.IsNullOrEmpty(name)) choices.Add(new ParamChoice { Name = name });
            return choices;
        }

        private void OnMappingChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _current == null) return;
            var combo = (ComboBox)sender;
            var info = (QuantityInfo)combo.Tag;
            var choice = combo.SelectedItem as ParamChoice;
            _current.Set(info.Quantity, choice == null || choice.IsNone ? null : choice.Name);
        }

        private void OnShowAllChanged(object sender, RoutedEventArgs e)
        {
            LoadMapping(_current);
        }

        // ── Extra columns ─────────────────────────────────────────────────────

        private void OnAddExtra(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var choice = ExtraPicker.SelectedItem as ParamChoice;
            if (choice == null || string.IsNullOrEmpty(choice.Name)) return;

            if (_current.ExtraColumns.Contains(choice.Name, StringComparer.OrdinalIgnoreCase)) return;
            _current.ExtraColumns.Add(choice.Name);
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
            CommitInstanceBox();

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

            FamilyMapping incomplete = Settings.Families.FirstOrDefault(f => !f.IsUsable);
            if (incomplete != null)
            {
                FamilyList.SelectedItem = incomplete;
                Warn("\"" + incomplete.FamilyName + "\" has no air flow or pressure parameter mapped yet.\n\n"
                     + "Those two are what the search filters on, so a family without them cannot be used. "
                     + "Map them, or remove the family from the list.");
                return;
            }

            Settings.TolerancePercent = tolerance;
            Settings.SetMountingOffsetFt(offsetFt);
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
