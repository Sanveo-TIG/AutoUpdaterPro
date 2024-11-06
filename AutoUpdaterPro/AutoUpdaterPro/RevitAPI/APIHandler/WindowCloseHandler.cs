using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.SDK.Samples.AutoUpdaterPro.CS;
using System;
using System.Collections.Generic;

namespace AutoUpdaterPro
{
    public class WindowCloseHandler : IExternalEventHandler
    {
        DateTime startDate = DateTime.UtcNow;
        UIDocument _uiDoc = null;
        Document _doc = null;
        public void Execute(UIApplication uiApp)
        {
            _uiDoc = uiApp.ActiveUIDocument;
            _doc = _uiDoc.Document;
            Autodesk.Revit.DB.View activeView = _doc.ActiveView;
            BoundingBoxXYZ bb1 = activeView.get_BoundingBox(activeView);
            int.TryParse(uiApp.Application.VersionNumber, out int RevitVersion);
            string offsetVariable = RevitVersion < 2020 ? "Offset" : "Middle Elevation";
            try
            {
                MainWindow.Instance.Close();
                ExternalApplication.window = null;
                _uiDoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
            }
            catch (Exception)
            {
            }
        }
        public string GetName()
        {
            return "Revit Addin";
        }
    }
}


