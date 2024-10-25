using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Microsoft.Office.Interop.Excel;
using Newtonsoft.Json;
using Revit.SDK.Samples.AutoUpdaterPro.CS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using TIGUtility;

namespace AutoUpdaterPro
{
    [Transaction(TransactionMode.Manual)]
    public class ProjectParameterHandler : IExternalEventHandler
    {
        UIDocument uiDoc = null;
        Document doc = null;
        public void Execute(UIApplication UiApp)
        {
            uiDoc = UiApp.ActiveUIDocument;
            doc = uiDoc.Document;
            try
            {
                using Transaction tx = new Transaction(doc);
                tx.Start("Project Parameter");
                string tempfilePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                DirectoryInfo di = new DirectoryInfo(tempfilePath);
                string tempfileName = System.IO.Path.Combine(di.FullName, "AutoUpdate_Parameter.txt");
                UiApp.Application.SharedParametersFilename = tempfileName;
                DefinitionFile defFile = UiApp.Application.OpenSharedParameterFile();


                CategorySet catSet_FORCUID = UiApp.Application.Create.NewCategorySet();


                Categories categories = doc.Settings.Categories;


                Category conduit
                 = categories.get_Item(
                   BuiltInCategory.OST_Conduit);
                catSet_FORCUID.Insert(conduit);
                List<string> listGUID = new List<string>();
                ParameterSet pS = doc.ProjectInformation.Parameters as ParameterSet;
                if (pS != null)
                {
                    foreach (Autodesk.Revit.DB.Parameter p in pS)
                    {
                        if (p.IsShared)
                        {
                            string str = p.GUID.ToString();
                            listGUID.Add(str);
                        }
                    }
                }

                if (defFile != null)
                {
                    foreach (DefinitionGroup dG in defFile.Groups)
                    {
                        foreach (Definition def in dG.Definitions)
                        {
                            string s = def.ToString();
                            if (def.Name == "AutoUpdater BendAngle")
                            {
                                ExternalDefinition definition = dG.Definitions.get_Item(def.Name) as ExternalDefinition;
                                if (!listGUID.Any(x => x == definition.GUID.ToString()))
                                {
                                    Autodesk.Revit.DB.Binding binding = UiApp.Application.Create.NewInstanceBinding(catSet_FORCUID);
                                    BindingMap map = (new UIApplication(UiApp.Application)).ActiveUIDocument.Document.ParameterBindings;
                                    map.Insert(def, binding, def.Name == "AutoUpdater BendAngle" ? BuiltInParameterGroup.PG_CONSTRAINTS : def.ParameterGroup);
                                }
                                else
                                {
                                }
                            }
                        }
                    }
                }

                tx.Commit();
            }
            catch
            {
            }
        }


        public string GetName()
        {
            return "Revit Addin";
        }
    }

}
