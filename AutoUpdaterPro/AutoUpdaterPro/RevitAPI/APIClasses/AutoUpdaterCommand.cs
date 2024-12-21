using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.SDK.Samples.AutoUpdaterPro.CS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TIGUtility;

namespace AutoUpdaterPro
{
    [Transaction(TransactionMode.Manual)]
    public class AutoUpdaterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (ExternalApplication.ToggleConPakToolsButtonSample != null && ExternalApplication.ToggleConPakToolsButtonSample.Enabled)
                {
                    if(Utility.HasValidLicense("Public"))
                    {
                        if (Utility.ReadPremiumLicense("AutoUpdate"))
                        {
                            CustomUIApplication customUIApplication = new CustomUIApplication
                            {
                                CommandData = commandData
                            };
                            System.Windows.Window window = new MainWindow();
                            window.Show();
                            window.Closed += OnClosing;

                            ExternalApplication.isStatic = true;

                            if (ExternalApplication.ToggleConPakToolsButtonSample != null)
                                ExternalApplication.ToggleConPakToolsButtonSample.Enabled = false;
                        }
                    }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        public void OnClosing(object senTagProToolr, EventArgs e)
        {
            try
            {
                ExternalApplication.isStatic = false;
                if (ExternalApplication.ToggleConPakToolsButtonSample != null)
                    ExternalApplication.ToggleConPakToolsButtonSample.Enabled = true;
            }
            catch (Exception)
            {
            }
        }
    }
}
