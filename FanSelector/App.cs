using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
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
            string panelName = "Tools";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // вкладка уже существует
            }

            RibbonPanel ribbonPanel = GetOrCreatePanel(application, tabName, panelName);

            // Create a push button to trigger the main command
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData1 = new PushButtonData(
                "btnRunApp",
                "Fan Selector",
                thisAssemblyPath,
                "TES_test2.Main"
            );

            // Optionally add an icon for the main command
            PushButton pushButton1 = ribbonPanel.AddItem(buttonData1) as PushButton;
            pushButton1.ToolTip = "Run the TES Fan Selector application. Specify Airflow and Pressure value and press search!";

                pushButton1.LargeImage = LoadImage("icon.png");

            try
            {
                // Create a ribbon panel within the custom tab
                string AboutpanelName = "About";
                RibbonPanel aboutPanel = GetOrCreatePanel(application, tabName, AboutpanelName);

                // Create a push button to open the web page
                PushButtonData webButtonData = new PushButtonData("btnOpenWebPage", "TES\nBIM Guide", Assembly.GetExecutingAssembly().Location, "TES_test2.OpenWebPage");

                // Optionally add an icon for the web page command
                PushButton webButton = aboutPanel.AddItem(webButtonData) as PushButton;
                webButton.ToolTip = "Open the TES BIM standart web page.";
                webButton.LargeImage = LoadImage("web_icon.png");
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Button already exists → OK
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Nothing to clean up in this simple case
            return Result.Succeeded;
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

        private RibbonPanel GetOrCreatePanel(UIControlledApplication app, string tabName, string panelName)
        {
            // Получаем все панели вкладки
            IList<RibbonPanel> panels = app.GetRibbonPanels(tabName);

            // Ищем нужную
            foreach (RibbonPanel panel in panels)
            {
                if (panel.Name == panelName)
                    return panel;
            }

            // Если не нашли — создаем
            return app.CreateRibbonPanel(tabName, panelName);
        }

        // Хелпер — грузим иконки из ресурсов сборки
        private BitmapImage LoadImage(string resourceName)
        {
            string fullName = $"FanSelector.Resources.{resourceName}";
            var stream = Assembly.GetExecutingAssembly()
                                 .GetManifestResourceStream(fullName);
            if (stream == null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            return image;
        }
    }
}
