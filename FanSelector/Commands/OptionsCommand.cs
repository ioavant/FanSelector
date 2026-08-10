using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FanSelector.Core;
using FanSelector.UI;

namespace FanSelector.Commands
{
    /// <summary>
    /// The parameter mapping and the search defaults. They live in a file next to
    /// the DLL, so nothing in the document changes and no transaction is needed.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class OptionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "No active document. Open a project first.";
                return Result.Failed;
            }

            return Show(commandData, uidoc.Document, null) == null ? Result.Cancelled : Result.Succeeded;
        }

        /// <summary>
        /// Show the options dialog and save what the user accepted. Returns the
        /// saved options, or null if the dialog was cancelled. Also used by the
        /// selection command when no family has been mapped yet, in which case
        /// <paramref name="notice"/> explains why it opened by itself.
        /// </summary>
        internal static FanSettings Show(ExternalCommandData commandData, Document doc, string notice)
        {
            var window = new OptionsWindow(doc, FanSettings.Load(), notice);
            WindowSupport.OwnedByRevit(window, commandData.Application.MainWindowHandle);

            if (window.ShowDialog() != true) return null;

            if (!window.Settings.Save())
                TaskDialog.Show(Brand.FullProductName,
                    "The options could not be saved to disk, so they apply to this Revit session "
                    + "only.\n\nTried: " + FanSettings.CurrentLocation);

            return window.Settings;
        }
    }
}
