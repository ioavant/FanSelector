using Autodesk.Revit.UI;
using System;
using System.Configuration.Assemblies;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace FanSelector
{
    public class App : IExternalApplication
    {
        private string primaryDirectory;
        private string fallbackDirectory;

        public Result OnStartup(UIControlledApplication application)
        {
            // Determine the primary and fallback directories
            primaryDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "TES",
                "FanSelector"
            );

            // Create a custom ribbon tab
            string tabName = "TES";
            CreateTabIfNotExists(application, tabName);

            // Create a ribbon panel within the custom tab
            RibbonPanel ribbonPanel = application.CreateRibbonPanel(tabName, "Tools");

            // Create a push button to trigger the main command
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            //PushButtonData buttonData1 = new PushButtonData("btnRunApp", "Fan Selector", thisAssemblyPath, "TES_test2.Main");
            PushButtonData buttonData1 = new PushButtonData(
                "btnRunApp",
                "Fan Selector",
                thisAssemblyPath,
                "TES_test2.Main"
            );


            // Optionally add an icon for the main command
            PushButton pushButton1 = ribbonPanel.AddItem(buttonData1) as PushButton;
            pushButton1.ToolTip = "Run the TES Fan Selector application. Specify Airflow and Pressure value and press search!";
            
            
            string iconPath1 = GetResourcePath("Resources", "icon.png");
            if (iconPath1 != null)
            {
                Uri uriImage1 = new Uri(iconPath1, UriKind.Absolute);
                BitmapImage largeImage1 = new BitmapImage(uriImage1);
                pushButton1.LargeImage = largeImage1;
            }

            // Create a push button to open the web page
            PushButtonData buttonData2 = new PushButtonData("btnOpenWebPage", "TES BIM Guide", thisAssemblyPath, "TES_test2.OpenWebPage");

            // Optionally add an icon for the web page command
            PushButton pushButton2 = ribbonPanel.AddItem(buttonData2) as PushButton;
            pushButton2.ToolTip = "Open the TES BIM standart web page.";
            string iconPath2 = GetResourcePath("Resources", "web_icon.png");
            if (iconPath2 != null)
            {
                Uri uriImage2 = new Uri(iconPath2, UriKind.Absolute);
                BitmapImage largeImage2 = new BitmapImage(uriImage2);
                pushButton2.LargeImage = largeImage2;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Nothing to clean up in this simple case
            return Result.Succeeded;
        }

        private string GetResourcePath(string folderName, string fileName)
        {
            // Check if the file exists in the primary directory
            string primaryPath = Path.Combine(primaryDirectory, folderName, fileName);
            if (File.Exists(primaryPath))
            {
                return primaryPath;
            }

            // Check if the file exists in the fallback directory
            string fallbackPath = Path.Combine(fallbackDirectory, folderName, fileName);
            if (File.Exists(fallbackPath))
            {
                return fallbackPath;
            }

            // File not found
            return null;
        }

        private void CreateTabIfNotExists(UIControlledApplication app, string tabName)
        {
            try
            {
                app.CreateRibbonTab(tabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tab already exists → OK
            }
        }
    }
}
