using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using System.Diagnostics;

namespace TES_test2
{
    [Transaction(TransactionMode.Manual)]
    public class OpenWebPage : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string url = "https://docs.google.com/document/d/1OtpHfr_rhbv46bVQ9rjPHYkKFwZnNHrUrVYX2EyTL5w/edit?usp=sharing";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return Result.Succeeded;
        }
    }
}
