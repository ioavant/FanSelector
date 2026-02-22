using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TES_test2
{
    public partial class CatalogForm : System.Windows.Forms.Form
    {
        public List<ElementParameter> Catalog { get; set; }
        public ElementParameter SelectedParameter { get; private set; }
        private ExternalCommandData _commandData;
        private string primaryDirectory;
        private string fallbackDirectory = @"C:/ProgramData/Autodesk/Revit/Addins/2026/TES"; // Update as necessary

        public CatalogForm(ExternalCommandData commandData)
        {
            InitializeComponent();
            _commandData = commandData;

            // Determine the primary directory
            primaryDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            LoadFamilies();
            LoadDefaultImage();
        }

        private void LoadFamilies()
        {
            var familyFiles = GetFilesFromDirectories("*.rfa");
            var catalogFiles = GetFilesFromDirectories("*.csv");

            var families = familyFiles.Select(Path.GetFileNameWithoutExtension)
                .Intersect(catalogFiles.Select(Path.GetFileNameWithoutExtension))
                .ToList();

            comboBoxFamily.Items.AddRange(families.ToArray());
        }

        private void LoadDefaultImage()
        {
            string defaultImagePath = GetResourcePath("Images", "image4.png");
            if (defaultImagePath != null)
            {
                pictureBoxFamily.Image = Image.FromFile(defaultImagePath);
            }
        }

        private void comboBoxFamily_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedFamily = comboBoxFamily.SelectedItem.ToString().ToLower();
            string imageFileName = selectedFamily.Contains("axial") ? "image1.png" :
                                   selectedFamily.Contains("centrifugal") ? "image2.png" :
                                   selectedFamily.Contains("line") ? "image3.png" : "image4.png";

            string imagePath = GetResourcePath("Images", imageFileName);
            if (imagePath != null)
            {
                pictureBoxFamily.Image = Image.FromFile(imagePath);
            }
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            if (comboBoxFamily.SelectedItem == null)
            {
                MessageBox.Show("Please select a family.");
                return;
            }

            string selectedFamily = comboBoxFamily.SelectedItem.ToString();
            string filePath = GetResourcePath(null, $"{selectedFamily}.csv");

            if (filePath != null)
            {
                Catalog = CsvReader.ReadCsv(filePath);

                if (int.TryParse(txtSearch1.Text, out int searchValue1) && int.TryParse(txtSearch2.Text, out int searchValue2))
                {
                    var filteredCatalog = Catalog.Where(p =>
                        Math.Abs(p.AirFlow - searchValue1) <= (numericUpDown1.Value * searchValue1 / 100) &&
                        Math.Abs(p.Pressure - searchValue2) <= (numericUpDown1.Value * searchValue2 / 100)
                    ).ToList();

                    dataGridViewCatalog.DataSource = filteredCatalog;
                }
                else
                {
                    MessageBox.Show("Please enter valid integer values in the search boxes.");
                }
            }
            else
            {
                MessageBox.Show($"Catalog file not found for the selected family: {selectedFamily}");
            }
        }

        private void btnUpdateRevit_Click(object sender, EventArgs e)
        {
            if (dataGridViewCatalog.SelectedRows.Count > 0)
            {
                var selectedRow = dataGridViewCatalog.SelectedRows[0];
                SelectedParameter = (ElementParameter)selectedRow.DataBoundItem;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Please select a row from the catalog.");
            }
        }

        private void CatalogForm_Load(object sender, EventArgs e)
        {
        }

        private List<string> GetFilesFromDirectories(string searchPattern)
        {
            var files = new List<string>();

            // Check in primary directory
            if (Directory.Exists(primaryDirectory))
            {
                files.AddRange(Directory.GetFiles(primaryDirectory, searchPattern));
            }

            // Check in fallback directory
            if (Directory.Exists(fallbackDirectory))
            {
                files.AddRange(Directory.GetFiles(fallbackDirectory, searchPattern));
            }

            return files;
        }

        private string GetResourcePath(string folderName, string fileName)
        {
            // Check if the file exists in the primary directory
            string primaryPath = Path.Combine(primaryDirectory, folderName ?? "", fileName);
            if (File.Exists(primaryPath))
            {
                return primaryPath;
            }

            // Check if the file exists in the fallback directory
            string fallbackPath = Path.Combine(fallbackDirectory, folderName ?? "", fileName);
            if (File.Exists(fallbackPath))
            {
                return fallbackPath;
            }

            // File not found
            return null;
        }
    }
}
