using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Structure;
using System.IO;

namespace TES_test2
{
    [Transaction(TransactionMode.Manual)]
    public class Main : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var form = new CatalogForm(commandData);
            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedParameter = form.SelectedParameter;
                if (selectedParameter != null)
                {
                    InsertFamilyInstance(commandData, selectedParameter, form.comboBoxFamily.SelectedItem.ToString());
                }
            }

            return Result.Succeeded;
        }

        private void InsertFamilyInstance(ExternalCommandData commandData, ElementParameter elementParameter, string selectedFamily)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Determine Revit version to set the fallback directory
            string revitVersion = commandData.Application.Application.VersionNumber;
            string fallbackDirectory = Path.Combine(@"C:\ProgramData\Autodesk\Revit\Addins", revitVersion, "TES");

            // Determine the primary directory
            string primaryDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            // Check if family file exists in the primary directory first
            string familyFilePath = Path.Combine(primaryDirectory, $"{selectedFamily}.rfa");
            if (!File.Exists(familyFilePath))
            {
                // If not found, check in the fallback directory
                familyFilePath = Path.Combine(fallbackDirectory, $"{selectedFamily}.rfa");
            }

            using (Transaction trans = new Transaction(doc, "Insert Family Instance"))
            {
                trans.Start();

                XYZ insertionPoint;
                try
                {
                    insertionPoint = uidoc.Selection.PickPoint("Select insertion point");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Error", "Click on active view before running the tool");
                    trans.RollBack();
                    return;
                }

                FamilySymbol familySymbol = GetFamilySymbolByTypeName(doc, selectedFamily, elementParameter.Size);

                if (familySymbol == null)
                {
                    if (File.Exists(familyFilePath))
                    {
                        TaskDialog.Show("Info", $"Loading family from {familyFilePath}");
                        if (!doc.LoadFamily(familyFilePath, new FamilyLoadOptions(), out Family family))
                        {
                            TaskDialog.Show("Error", $"Failed to load family from {familyFilePath}");
                            trans.RollBack();
                            return;
                        }

                        // After loading the family, try to get the family symbol again
                        familySymbol = GetFamilySymbolByTypeName(doc, selectedFamily, elementParameter.Size);

                        if (familySymbol == null)
                        {
                            TaskDialog.Show("Error", $"Family type {elementParameter.Size} not found in loaded family {selectedFamily}.");
                            trans.RollBack();
                            return;
                        }
                    }
                    else
                    {
                        TaskDialog.Show("Error", $"Family file not found at {familyFilePath}");
                        trans.RollBack();
                        return;
                    }
                }

                if (!familySymbol.IsActive)
                {
                    familySymbol.Activate();
                    doc.Regenerate();
                }

                // Get the level of the active view
                View activeView = doc.ActiveView;
                Level level = doc.GetElement(activeView.GenLevel.Id) as Level;

                if (level == null)
                {
                    TaskDialog.Show("Error", "Active view does not have an associated level.");
                    trans.RollBack();
                    return;
                }

                double elevationOffset = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Meters);
                XYZ adjustedInsertionPoint = new XYZ(insertionPoint.X, insertionPoint.Y, elevationOffset);
                FamilyInstance familyInstance = doc.Create.NewFamilyInstance(adjustedInsertionPoint, familySymbol, level, StructuralType.NonStructural);

                // Set the parameters
                SetParameter(familyInstance, "TES_Pressure", elementParameter.Pressure, doc);
                SetParameter(familyInstance, "TES_MotorPower", elementParameter.Power, doc);
                SetParameter(familyInstance, "TES_RPM", elementParameter.RPM, doc);
                SetParameter(familyInstance, "Override AirFlow", elementParameter.AirFlow, doc); // Set the Override AirFlow parameter

                trans.Commit();
            }
        }

        private FamilySymbol GetFamilySymbolByTypeName(Document doc, string familyName, string typeName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment);

            foreach (FamilySymbol familySymbol in collector)
            {
                if (familySymbol.Family.Name.Equals(familyName, StringComparison.InvariantCultureIgnoreCase) &&
                    familySymbol.Name.Equals(typeName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return familySymbol;
                }
            }

            return null;
        }

        private void SetParameter(FamilyInstance instance, string paramName, object value, Document doc)
        {
            Parameter param = instance.LookupParameter(paramName);

            if (param != null && value != null)
            {
                if (param.StorageType == StorageType.Double)
                {
                    if (paramName == "TES_Pressure")
                    {
                        // Convert pressure from Pascals (Pa) to Revit internal units
                        double paValue = Convert.ToDouble(value);
                        double internalValue = UnitUtils.ConvertToInternalUnits(paValue, UnitTypeId.Pascals);
                        param.Set(internalValue);
                    }
                    else if (paramName == "TES_MotorPower")
                    {
                        // Convert power from Kilowatts to Revit internal units
                        double kWValue = Convert.ToDouble(value);
                        double internalValue = UnitUtils.ConvertToInternalUnits(kWValue, UnitTypeId.Kilowatts);
                        param.Set(internalValue);
                    }
                    else if (paramName == "Override AirFlow")
                    {
                        // Convert Air Flow from m³/h to Revit internal units (assuming Cubic Meters per Hour)
                        double airFlowValue = Convert.ToDouble(value);
                        double internalValue = UnitUtils.ConvertToInternalUnits(airFlowValue, UnitTypeId.CubicMetersPerHour);
                        param.Set(internalValue);
                    }
                }
                else if (param.StorageType == StorageType.String)
                {
                    param.Set(value.ToString());
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    if (paramName == "TES_RPM")
                    {
                        if (int.TryParse(value.ToString(), out int intValue))
                        {
                            param.Set(intValue);
                        }
                    }
                }
            }
        }

        public class FamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true; // Change this to false if you do not want to overwrite parameter values
                return true; // Return true to load the family even if it already exists
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family; // Choose whether to load the family from the file or the existing project
                overwriteParameterValues = true; // Change this to false if you do not want to overwrite parameter values
                return true; // Return true to load the shared family even if it already exists
            }
        }
    }
}
