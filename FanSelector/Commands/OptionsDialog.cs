using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FanSelector.Core;
using FanSelector.UI;

namespace FanSelector.Commands
{
    /// <summary>
    /// Opening and saving the options. Not an IExternalCommand and not on the
    /// ribbon: the options are reached from the Fan Selector window, which is the
    /// only place anyone wants them from — a second entry point on the ribbon was
    /// just two doors into one room.
    /// </summary>
    internal static class OptionsDialog
    {
        /// <summary>
        /// Show the options dialog and save what the user accepted. Returns the
        /// saved options, or null if the dialog was cancelled. <paramref name="notice"/>
        /// explains why it opened by itself, when it did.
        /// </summary>
        public static FanSettings Show(ExternalCommandData commandData, Document doc, string notice)
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
