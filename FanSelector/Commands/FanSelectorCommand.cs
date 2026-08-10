using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FanSelector.Core;
using FanSelector.UI;

namespace FanSelector.Commands
{
    /// <summary>
    /// Search the chosen fan family for a type that delivers the requested duty,
    /// then place it.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FanSelectorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document. Open a project first.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            FanSettings settings = FanSettings.Load();

            // First run: there is nothing to search until the user has said which
            // parameter of their family carries air flow and which carries pressure.
            if (settings.Families.Count == 0)
            {
                settings = OptionsCommand.Show(commandData, doc,
                    "No fan family has been set up yet.\n"
                    + "Pick one of the families loaded in this project and map its parameters, then continue.");
                if (settings == null) return Result.Cancelled;
            }

            var window = new MainWindow(doc, settings);
            WindowSupport.OwnedByRevit(window, commandData.Application.MainWindowHandle);

            if (window.ShowDialog() != true) return Result.Cancelled;

            PlacementResult placement = FanPlacer.Place(
                uidoc,
                window.SelectedCandidate,
                window.SelectedMapping,
                window.CurrentSettings.MountingOffsetFt);

            if (placement.Cancelled) return Result.Cancelled;

            if (!string.IsNullOrEmpty(placement.Message))
                TaskDialog.Show(Brand.FullProductName, placement.Message);

            return placement.Placed ? Result.Succeeded : Result.Failed;
        }
    }
}
