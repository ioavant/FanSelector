using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using FanSelector.Core;
// Autodesk.Revit.DB has its own Binding (a parameter binding) and Grid (a modelled
// grid line), so those names are ambiguous once the Revit namespace is in scope.
// In this file they always mean the WPF ones.
using Binding = System.Windows.Data.Binding;
using Grid = System.Windows.Controls.Grid;

namespace FanSelector.UI
{
    /// <summary>
    /// The selection window: pick a configured family, state the duty, and get
    /// back the types of that family that can deliver it.
    /// </summary>
    internal partial class MainWindow : Window
    {
        private readonly Document _doc;
        private readonly Units _units;

        private FanSettings _settings;
        private ForgeTypeId _airFlowSpec;
        private ForgeTypeId _pressureSpec;

        /// <summary>
        /// The picture of the selected family. One of the four shipped inside the
        /// add-in, chosen from the family's name, and changing when the fan does —
        /// exactly as the old version behaved.
        /// </summary>
        private System.Windows.Media.ImageSource _familyImage;

        /// <summary>Whether the fan about to be placed should get a duct stub.</summary>
        public bool StubRequested { get { return StubBox.IsChecked == true; } }

        /// <summary>
        /// The air flow the last search asked for, in internal units. Handed to
        /// placement, where a generated duct stub's terminal is given the DUTY
        /// rather than the catalogue's own rating.
        /// </summary>
        public double RequestedAirFlow { get; private set; }

        /// <summary>The type the user chose to place. Non-null only when the dialog returned true.</summary>
        public FanCandidate SelectedCandidate { get; private set; }

        /// <summary>The mapping the chosen type belongs to, needed to write the instance air flow.</summary>
        public FamilyMapping SelectedMapping { get; private set; }

        /// <summary>Settings as they stand after any trip through the Options dialog.</summary>
        public FanSettings CurrentSettings { get { return _settings; } }

        public MainWindow(Document doc, FanSettings settings)
        {
            InitializeComponent();

            _doc = doc;
            _units = doc.GetUnits();
            _settings = settings;

            Title = Brand.FullProductName;
            TitleText.Text = Brand.ProductDisplayName + "  —  v" + UpdateChecker.CurrentVersion;
            LogoImage.Source = WindowSupport.Image("logo.png");
            if (LogoImage.Source == null) LogoImage.Visibility = System.Windows.Visibility.Collapsed;

            string banner = WindowSupport.UpdateBannerText();
            if (banner != null)
            {
                BannerText.Text = banner;
                BannerBorder.Visibility = System.Windows.Visibility.Visible;
            }

            ToleranceBox.Text = _settings.TolerancePercent.ToString("0.#", CultureInfo.CurrentCulture);
            LoadFamilies(null);
        }

        // ── Family selection ──────────────────────────────────────────────────

        private void LoadFamilies(string selectName)
        {
            FamilyCombo.ItemsSource = null;
            FamilyCombo.ItemsSource = _settings.Families;

            if (_settings.Families.Count == 0)
            {
                StatusText.Text = "No fan family has been set up yet — open Options and map one.";
                return;
            }

            FamilyMapping select = selectName == null ? null : _settings.Find(selectName);
            FamilyCombo.SelectedItem = select ?? _settings.Families[0];
        }

        private FamilyMapping Mapping { get { return FamilyCombo.SelectedItem as FamilyMapping; } }

        private void OnFamilyChanged(object sender, SelectionChangedEventArgs e)
        {
            FamilyMapping mapping = Mapping;
            ResultsGrid.ItemsSource = null;
            InsertButton.IsEnabled = false;

            _familyImage = mapping == null ? null : WindowSupport.Image(FanImages.For(mapping.FamilyName));
            PreviewImage.Source = _familyImage;
            StubBox.IsChecked = mapping != null && mapping.AddDuctStub;

            if (mapping == null)
            {
                _airFlowSpec = null;
                _pressureSpec = null;
                AirFlowUnit.Text = string.Empty;
                PressureUnit.Text = string.Empty;
                return;
            }

            // The search boxes are read in the units the catalogue column itself
            // declares, so a catalogue in CFM and inWG works as well as one in
            // m³/h and Pa.
            _airFlowSpec = FanSearch.SpecFor(mapping, FanQuantity.AirFlow);
            _pressureSpec = FanSearch.SpecFor(mapping, FanQuantity.Pressure);
            AirFlowUnit.Text = RevitUnits.Symbol(_units, _airFlowSpec);
            PressureUnit.Text = RevitUnits.Symbol(_units, _pressureSpec);

            BuildColumns(mapping);
            StatusText.Text = string.Empty;
        }

        /// <summary>
        /// Columns follow the mapping: a quantity nobody mapped is not an empty
        /// column, it is no column.
        /// </summary>
        private void BuildColumns(FamilyMapping mapping)
        {
            ResultsGrid.Columns.Clear();
            AddColumn("Type", "TypeName", 2.2);
            AddColumn(Quantities.AirFlow.ColumnHeader, "AirFlowText", 1);
            AddColumn(Quantities.Pressure.ColumnHeader, "PressureText", 1);

            if (!string.IsNullOrEmpty(mapping.PowerColumn))
                AddColumn(Quantities.Power.ColumnHeader, "PowerText", 1);
            if (!string.IsNullOrEmpty(mapping.SpeedColumn))
                AddColumn(Quantities.Speed.ColumnHeader, "SpeedText", 1);
            if (!string.IsNullOrEmpty(mapping.SfpColumn))
                AddColumn(Quantities.Sfp.ColumnHeader, "SfpText", 1);
            if (!string.IsNullOrEmpty(mapping.SoundPowerColumn))
                AddColumn(Quantities.SoundPower.ColumnHeader, "SoundText", 1);

            for (int i = 0; i < mapping.ExtraColumns.Count; i++)
                AddColumn(mapping.ExtraColumns[i], "Extras[" + i + "]", 1);

            AddColumn("Off by", "DeviationText", 0.7);
            // A catalogue holds every type the manufacturer offers, so most rows
            // are normally not in the model yet. Saying so up front beats a
            // surprise at insert time.
            AddColumn("In model", "LoadedText", 0.8);
        }

        private void AddColumn(string header, string path, double starWidth)
        {
            ResultsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = new DataGridLength(starWidth, DataGridLengthUnitType.Star)
            });
        }

        // ── Search ────────────────────────────────────────────────────────────

        private void OnSortChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsGrid != null && ResultsGrid.ItemsSource != null) RunSearch();
        }

        private void OnSearch(object sender, RoutedEventArgs e)
        {
            RunSearch();
        }

        private void RunSearch()
        {
            FamilyMapping mapping = Mapping;
            if (mapping == null)
            {
                Warn("Choose a fan family first. If the list is empty, set one up in Options.");
                return;
            }

            double airFlow;
            if (!RevitUnits.TryParse(_units, _airFlowSpec, AirFlowBox.Text, out airFlow) || airFlow <= 0.0)
            {
                Warn("Enter the air flow you need, in " +
                     Describe(_airFlowSpec) + ". It has to be greater than zero.");
                AirFlowBox.Focus();
                AirFlowBox.SelectAll();
                return;
            }

            double pressure;
            if (!RevitUnits.TryParse(_units, _pressureSpec, PressureBox.Text, out pressure) || pressure <= 0.0)
            {
                Warn("Enter the pressure you need, in " +
                     Describe(_pressureSpec) + ". It has to be greater than zero.");
                PressureBox.Focus();
                PressureBox.SelectAll();
                return;
            }

            double tolerance;
            if (!double.TryParse(ToleranceBox.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out tolerance)
                || tolerance <= 0.0 || tolerance > 100.0)
            {
                Warn("The tolerance has to be a percentage between 0 and 100, for example 10.");
                ToleranceBox.Focus();
                ToleranceBox.SelectAll();
                return;
            }

            SearchResult result = FanSearch.Run(_doc, mapping, airFlow, pressure, tolerance, SelectedSort());
            RequestedAirFlow = airFlow;

            BuildColumns(mapping);
            ResultsGrid.ItemsSource = result.Candidates;
            PreviewImage.Source = _familyImage;
            InsertButton.IsEnabled = false;

            if (!string.IsNullOrEmpty(result.Note))
                StatusText.Text = result.Note;
            else if (result.Candidates.Count == 0)
                StatusText.Text = "None of the " + result.RowsExamined + " types in the catalogue is within "
                                  + tolerance.ToString("0.#") + "% on both figures. Widen the tolerance.";
            else
                StatusText.Text = result.Candidates.Count + " of " + result.RowsExamined
                                  + " catalogue types are within " + tolerance.ToString("0.#") + "%."
                                  + (result.NotLoaded > 0
                                        ? "  " + result.NotLoaded + " of them are not in this model yet and "
                                          + "will be loaded when inserted."
                                        : string.Empty);

            if (result.Candidates.Count > 0) ResultsGrid.SelectedIndex = 0;
        }

        private FanSort SelectedSort()
        {
            switch (SortCombo.SelectedIndex)
            {
                case 1: return FanSort.LowestSfp;
                case 2: return FanSort.LowestSound;
                case 3: return FanSort.LowestPower;
                default: return FanSort.BestMatch;
            }
        }

        private string Describe(ForgeTypeId spec)
        {
            string symbol = RevitUnits.Symbol(_units, spec);
            return string.IsNullOrEmpty(symbol) ? "this project's units" : symbol;
        }

        // ── Results ───────────────────────────────────────────────────────────

        private void OnResultSelected(object sender, SelectionChangedEventArgs e)
        {
            InsertButton.IsEnabled = ResultsGrid.SelectedItem is FanCandidate;
        }

        private void OnResultDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is FanCandidate) Accept();
        }

        private void OnInsert(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        /// <summary>
        /// Hand the choice back and close. The instance is placed by the command,
        /// not here: picking a point needs the modal dialog out of the way.
        /// </summary>
        private void Accept()
        {
            var candidate = ResultsGrid.SelectedItem as FanCandidate;
            if (candidate == null) return;
            SelectedCandidate = candidate;
            SelectedMapping = Mapping;
            DialogResult = true;
        }

        // ── Options ───────────────────────────────────────────────────────────

        private void OnOptions(object sender, RoutedEventArgs e)
        {
            string previous = Mapping == null ? null : Mapping.FamilyName;

            var options = new OptionsWindow(_doc, _settings, null) { Owner = this };
            if (options.ShowDialog() != true) return;

            _settings = options.Settings;
            if (!_settings.Save())
                Warn("The options could not be saved to disk, so they apply to this Revit session only."
                     + "\n\nTried: " + FanSettings.CurrentLocation);

            ToleranceBox.Text = _settings.TolerancePercent.ToString("0.#", CultureInfo.CurrentCulture);
            LoadFamilies(previous);
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
