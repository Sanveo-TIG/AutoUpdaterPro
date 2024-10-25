
//
// (C) Copyright 2003-2019 by Autodesk, Inc.
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE. AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is subject to
// restrictions set forth in FAR 52.227-19 (Commercial Computer
// Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii)
// (Rights in Technical Data and Computer Software), as applicable.
//

using System;
using System.IO;
using System.Data;
using System.Reflection;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI.Selection;
using AutoUpdaterPro;

namespace Revit.SDK.Samples.AutoUpdaterPro.CS
{
    /// <summary>
    /// A class inherits IExternalApplication interface and provide an entry of the sample.
    /// It create a modeless dialog to track the changes.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    [Autodesk.Revit.Attributes.Journaling(Autodesk.Revit.Attributes.JournalingMode.NoCommandData)]

    public class ExternalApplication : IExternalApplication
    {
        public static System.Windows.Window window;
        List<Element> collection = null;

        #region  Class Member Variables
        /// <summary>
        /// A controlled application used to register the DocumentChanged event. Because all trigger points
        /// in this sample come from UI, the event must be registered to ControlledApplication. 
        /// If the trigger point is from API, user can register it to application 
        /// which can retrieve from ExternalCommand.
        /// </summary>
        private static ControlledApplication m_CtrlApp;

        /// <summary>
        /// data table for information windows.
        /// </summary>
        private static DataTable m_ChangesInfoTable;

        /// <summary>
        /// The window is used to show changes' information.
        /// </summary>
        private static ChangesInformationForm m_InfoForm;
        #endregion
        #region Class Static Property
        /// <summary>
        /// Property to get and set private member variables of changes log information.
        /// </summary>
        public static DataTable ChangesInfoTable
        {
            get { return m_ChangesInfoTable; }
            set { m_ChangesInfoTable = value; }
        }
        public static PushButton AutoConnectButton { get; set; }
        public static PushButton ToggleConPakToolsButton { get; set; }
        public static PushButton ToggleConPakToolsButtonSample { get; set; }
        public static bool isStatic {  get; set; }
        /// <summary>
        /// Property to get and set private member variables of info form.
        /// </summary>
        public static ChangesInformationForm InfoForm
        {
            get { return ExternalApplication.m_InfoForm; }
            set { ExternalApplication.m_InfoForm = value; }
        }
        #endregion
        #region IExternalApplication Members
        /// <summary>
        /// Implement this method to implement the external application which should be called when 
        /// Revit starts before a file or default template is actually loaded.
        /// </summary>
        /// <param name="application">An object that is passed to the external application 
        /// which contains the controlled application.</param> 
        /// <returns>Return the status of the external application. 
        /// A result of Succeeded means that the external application successfully started. 
        /// Cancelled can be used to signify that the user cancelled the external operation at 
        /// some point.
        /// If false is returned then Revit should inform the user that the external application 
        /// failed to load and the release the internal reference.</returns>
        public Result OnStartup(UIControlledApplication application)
        {
            OnButtonCreate(application);

            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(DocumentFormatAssemblyLoad);

            m_CtrlApp = application.ControlledApplication;
            application.Idling += OnIdling; //
            m_ChangesInfoTable = CreateChangeInfoTable();
            m_InfoForm = new ChangesInformationForm(ChangesInfoTable);
            m_InfoForm.Hide();
            return Result.Succeeded;
        }

        /// <summary>
        /// Implement this method to implement the external application which should be called when 
        /// Revit is about to exit,Any documents must have been closed before this method is called.
        /// </summary>
        /// <param name="application">An object that is passed to the external application 
        /// which contains the controlled application.</param>
        /// <returns>Return the status of the external application. 
        /// A result of Succeeded means that the external application successfully shutdown. 
        /// Cancelled can be used to signify that the user cancelled the external operation at 
        /// some point.
        /// If false is returned then the Revit user should be warned of the failure of the external 
        /// application to shut down correctly.</returns>

        public Result OnShutdown(UIControlledApplication application)
        {
            m_InfoForm = null;
            m_ChangesInfoTable = null;
            return Result.Succeeded;
        }
        #endregion

        #region Event handler
        /// <summary>
        /// This method is the event handler, which will dump the change information to tracking dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// 

        public static void Toggle()
        {
            try
            {
                string s = ToggleConPakToolsButton.ItemText;
                BitmapImage OffLargeImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/off-red-32X32.png"));
                BitmapImage OnLargeImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/on-green-32X32.png"));

                BitmapImage OnImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/on-green-16X16.png"));
                BitmapImage OffImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/off-red-16X16.png"));
                if (s == "AutoUpdate OFF")
                {
                    ProjectParameterHandler projectParameterHandler = new ProjectParameterHandler();
                    ExternalEvent Event = ExternalEvent.Create(projectParameterHandler);
                    Event.Raise();
                    ToggleConPakToolsButton.LargeImage = OnLargeImage;
                    ToggleConPakToolsButton.Image = OnImage;

                    ToggleConPakToolsButtonSample.Enabled = false;
                }
                else
                {
                    ToggleConPakToolsButton.LargeImage = OffLargeImage;
                    ToggleConPakToolsButton.Image = OffImage;

                    ToggleConPakToolsButtonSample.Enabled = true;
                }
                ToggleConPakToolsButton.ItemText = s.Equals("AutoUpdate OFF") ? "AutoUpdate ON" : "AutoUpdate OFF";
            }
            catch (Exception)
            {
            }
        }
        private void OnButtonCreate(UIControlledApplication application)
        {
            string executableLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string dllLocation = Path.Combine(executableLocation, "AutoUpdaterPro.dll");

            PushButtonData buttondata = new PushButtonData("ModifierBtn", "AutoUpdate OFF", dllLocation, "Revit.SDK.Samples.AutoUpdaterPro.CS.Command");

            BitmapImage pb1Image = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/off-red-32X32.png"));
            buttondata.LargeImage = pb1Image;

            BitmapImage pb1Image2 = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/off-red-16X16.png"));

            buttondata.Image = pb1Image2;
            buttondata.AvailabilityClassName = "Revit.SDK.Samples.AutoUpdaterPro.CS.Availability";

            #region Sample PushButton 
            PushButtonData buttondataSample1 = new PushButtonData("ModifierBtnCommandAutoConnect", "AutoUpdater", dllLocation, "AutoUpdaterPro.AutoUpdaterCommand");
            BitmapImage pb1ImageSample11 = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/on-green-16X16.png"));
            buttondataSample1.LargeImage = pb1ImageSample11;
            BitmapImage pb1ImageSample12 = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/on-green-16X16.png"));
            buttondataSample1.Image = pb1ImageSample12;
            #endregion

            var ribbonPanel = RibbonPanel(application);
            if (ribbonPanel != null)
            {
                ToggleConPakToolsButton = ribbonPanel.AddItem(buttondata) as PushButton;
                ToggleConPakToolsButtonSample = ribbonPanel.AddItem(buttondataSample1) as PushButton;
            }
        }
        public static Assembly DocumentFormatAssemblyLoad(object sender, ResolveEventArgs args)
        {
            if (args.Name.Contains("resources"))
            {
                return null;
            }
            if (args.Name.Contains("TIGUtility"))
            {
                string assemblyPath = $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}\\TIGUtility.dll";
                var assembly = Assembly.Load(assemblyPath);
                return assembly;
            }
            return null;
        }
        public Autodesk.Revit.UI.RibbonPanel RibbonPanel(UIControlledApplication a)
        {
            string tab = "Sanveo Tools"; // Archcorp
            string ribbonPanelText = "Auto Updater"; // Architecture

            // Empty ribbon panel 
            Autodesk.Revit.UI.RibbonPanel ribbonPanel = null;
            // Try to create ribbon tab. 
            try
            {
                a.CreateRibbonTab(tab);
            }
            catch { }
            // Try to create ribbon panel.
            try
            {
                Autodesk.Revit.UI.RibbonPanel panel = a.CreateRibbonPanel(tab, ribbonPanelText);
            }
            catch { }
            // Search existing tab for your panel.
            List<Autodesk.Revit.UI.RibbonPanel> panels = a.GetRibbonPanels(tab);
            foreach (Autodesk.Revit.UI.RibbonPanel p in panels)
            {
                if (p.Name == ribbonPanelText)
                {
                    ribbonPanel = p;
                }
            }
            //return panel 
            return ribbonPanel;
        }
        private void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            try
            {
                if (ToggleConPakToolsButton.ItemText == "AutoUpdate ON" || !ToggleConPakToolsButtonSample.Enabled)
                {
                    List<Element> SelectedElements = new List<Element>();
                    UIApplication uiApp = sender as UIApplication;
                    UIDocument uiDoc = uiApp.ActiveUIDocument;
                    Document doc = uiDoc.Document;
                    if (doc != null && !doc.IsReadOnly)
                    {
                        //Select conduit
                        Selection selection = uiDoc.Selection;
                        List<ElementId> selectedIds = selection.GetElementIds().ToList();
                        foreach (ElementId elementID in selectedIds)
                        {
                            if (doc.GetElement(elementID).Category != null)
                            {
                                if (doc.GetElement(elementID).Category.Name == "Conduits")
                                {
                                    SelectedElements.Add(doc.GetElement(elementID));
                                    ChangesInformationForm.instance._selectedElements.Add(elementID);
                                }
                            }
                        }
                        if (selectedIds.Any())
                        {
                            if (doc.GetElement(selectedIds.FirstOrDefault()).Category != null)
                            {
                                if (doc.GetElement(selectedIds.FirstOrDefault()).Category.Name == "Conduits")
                                {
                                    if (window == null)
                                    {
                                        if (SelectedElements != null && SelectedElements.Count > 0)
                                        {
                                            //Updater to be triggered only if no ends open
                                            List<Element> elementlist = new List<Element>();
                                            foreach (ElementId id in SelectedElements.Select(x => x.Id))
                                            {
                                                Element elem = doc.GetElement(id);
                                                if (elem.Category != null && elem.Category.Name == "Conduits")
                                                {
                                                    elementlist.Add(elem);
                                                }
                                            }
                                            List<ElementId> FittingElem = new List<ElementId>();
                                            for (int i = 0; i < elementlist.Count; i++)
                                            {
                                                ConnectorSet connector = GetConnectorSet(elementlist[i]);
                                                List<ElementId> Icollect = new List<ElementId>();
                                                foreach (Connector connect in connector)
                                                {
                                                    ConnectorSet cs1 = connect.AllRefs;
                                                    foreach (Connector c in cs1)
                                                    {
                                                        Icollect.Add(c.Owner.Id);
                                                    }
                                                    foreach (ElementId eid in Icollect)
                                                    {
                                                        if (doc.GetElement(eid) != null && (doc.GetElement(eid).Category != null && doc.GetElement(eid).Category.Name == "Conduit Fittings"))
                                                        {
                                                            FittingElem.Add(eid);
                                                        }
                                                    }
                                                }
                                            }
                                            List<ElementId> FittingElements = new List<ElementId>();
                                            FittingElements = FittingElem.Distinct().ToList();
                                            if (FittingElements.Count == (2 * (elementlist.Count)) && !isStatic)
                                            {
                                                window = new MainWindow();
                                                MainWindow.Instance.firstElement = new List<Element>();
                                                MainWindow.Instance.firstElement.AddRange(SelectedElements);
                                                MainWindow.Instance._document = doc;
                                                MainWindow.Instance._uiDocument = uiDoc;
                                                MainWindow.Instance._uiApplication = uiApp;
                                                window.Show();
                                            }
                                            /*else if(FittingElements.Count != (2 * (elementlist.Count)) && !isStatic)
                                             {
                                                 uiDoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                                 System.Windows.MessageBox.Show("Please select the conduits and ensure they have fittings on both sides.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                                             }*/
                                        }
                                        else
                                        {
                                            uiDoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                        }
                                    }
                                }
                            }
                        }
                    }
                    try
                    {
                        List<Element> elementlist = new List<Element>();
                        List<ElementId> rvConduitlist = new List<ElementId>();
                        string value = string.Empty;
                        foreach (ElementId id in SelectedElements.Select(x => x.Id))
                        {
                            Element elem = doc.GetElement(id);
                            if (elem.Category != null && elem.Category.Name == "Conduits")
                            {
                                elementlist.Add(elem);
                            }
                        }
                        ChangesInformationForm.instance.MidSaddlePt = elementlist.Distinct().ToList();
                        ChangesInformationForm.instance._elemIdone.Clear();
                        ChangesInformationForm.instance._elemIdtwo.Clear();
                        List<ElementId> FittingElem = new List<ElementId>();
                        for (int i = 0; i < elementlist.Count; i++)
                        {
                            ConnectorSet connector = GetConnectorSet(elementlist[i]);
                            List<ElementId> Icollect = new List<ElementId>();
                            foreach (Connector connect in connector)
                            {
                                ConnectorSet cs1 = connect.AllRefs;
                                foreach (Connector c in cs1)
                                {
                                    Icollect.Add(c.Owner.Id);
                                }
                                foreach (ElementId eid in Icollect)
                                {
                                    if (doc.GetElement(eid) != null && (doc.GetElement(eid).Category != null && doc.GetElement(eid).Category.Name == "Conduit Fittings"))
                                    {
                                        FittingElem.Add(eid);
                                    }
                                }
                            }
                        }
                        List<ElementId> FittingElements = new List<ElementId>();
                        FittingElements = FittingElem.Distinct().ToList();
                        List<Element> BendElements = new List<Element>();
                        foreach (ElementId id in FittingElements)
                        {
                            BendElements.Add(doc.GetElement(id));
                        }
                        if (MainWindow.Instance != null)
                        {
                            MainWindow.Instance._bendElements = BendElements;
                        }
                        List<ElementId> Icollector = new List<ElementId>();
                        for (int i = 0; i < BendElements.Count; i++)
                        {
                            ConnectorSet connector = GetConnectorSet(BendElements[i]);
                            foreach (Connector connect in connector)
                            {
                                ConnectorSet cs1 = connect.AllRefs;
                                foreach (Connector c in cs1)
                                {
                                    Icollector.Add(c.Owner.Id);
                                }
                            }
                        }
                        foreach (ElementId eid in Icollector)
                        {
                            if (doc.GetElement(eid) != null && (doc.GetElement(eid).Category != null && doc.GetElement(eid).Category.Name == "Conduits"))
                            {
                                ChangesInformationForm.instance._selectedElements.Add(eid);
                            }
                        }
                        List<Element> elementtwo = new List<Element>();
                        List<ElementId> RefID = new List<ElementId>();

                        for (int i = 0; i < BendElements.Count; i++)
                        {
                            for (int j = i + 1; j < BendElements.Count; j++)
                            {
                                Element elemOne = BendElements[i];
                                Element elemTwo = BendElements[j];

                                if (elemOne != null)
                                {
                                    ConnectorSet firstconnector = GetConnectorSet(elemOne);
                                    ConnectorSet secondconnector = GetConnectorSet(elemTwo);
                                    try
                                    {
                                        List<ElementId> IDone = new List<ElementId>();
                                        foreach (Connector connector in firstconnector)
                                        {
                                            ConnectorSet cs1 = connector.AllRefs;
                                            foreach (Connector c in cs1)
                                            {
                                                IDone.Add(c.Owner.Id);
                                            }
                                            foreach (ElementId eid in IDone)
                                            {
                                                if (doc.GetElement(eid) != null && (doc.GetElement(eid).Category != null && doc.GetElement(eid).Category.Name == "Conduits"))
                                                {
                                                    ChangesInformationForm.instance._elemIdone.Add(eid);
                                                }
                                            }
                                        }
                                        List<ElementId> IDtwo = new List<ElementId>();
                                        foreach (Connector connector in secondconnector)
                                        {
                                            ConnectorSet cs1 = connector.AllRefs;
                                            foreach (Connector c in cs1)
                                            {
                                                IDtwo.Add(c.Owner.Id);
                                            }
                                            foreach (ElementId eid in IDtwo)
                                            {
                                                if (doc.GetElement(eid) != null && (doc.GetElement(eid).Category != null && doc.GetElement(eid).Category.Name == "Conduits"))
                                                {
                                                    ChangesInformationForm.instance._elemIdtwo.Add(eid);
                                                    if (ChangesInformationForm.instance._elemIdone.Any(r => r == eid))
                                                    {
                                                        ChangesInformationForm.instance._deletedIds.Add(eid);
                                                        rvConduitlist.Add(eid);
                                                    }
                                                }
                                            }
                                        }
                                        ChangesInformationForm.instance._deletedIds.Add(elemOne.Id);
                                        ChangesInformationForm.instance._deletedIds.Add(elemTwo.Id);
                                        var l = rvConduitlist.Distinct();
                                        ChangesInformationForm.instance._selectedElements = ChangesInformationForm.instance._selectedElements.Except(l).ToList();
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {

                    }
                }
            }
            catch (Exception)
            {
            }
        }
        #endregion

        #region Class Methods
        /// <summary>
        /// This method is used to retrieve the changed element and add row to data table.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="doc"></param>
        /// <param name="changeType"></param>


        public static ConnectorSet GetConnectorSet(Autodesk.Revit.DB.Element Ele)
        {
            ConnectorSet result = null;
            if (Ele is Autodesk.Revit.DB.FamilyInstance)
            {
                MEPModel mEPModel = ((Autodesk.Revit.DB.FamilyInstance)Ele).MEPModel;
                if (mEPModel != null && mEPModel.ConnectorManager != null)
                {
                    result = mEPModel.ConnectorManager.Connectors;
                }
            }
            else if (Ele is MEPCurve)
            {
                result = ((MEPCurve)Ele).ConnectorManager.Connectors;
            }

            return result;
        }

        /// <summary>
        /// Generate a data table with five columns for display in window
        /// </summary>
        /// <returns>The DataTable to be displayed in window</returns>

        private DataTable CreateChangeInfoTable()
        {
            // create a new dataTable
            DataTable changesInfoTable = new DataTable("ChangesInfoTable");

            // Create a "ChangeType" column. It will be "Added", "Deleted" and "Modified".
            DataColumn styleColumn = new DataColumn("ChangeType", typeof(System.String));
            styleColumn.Caption = "ChangeType";
            changesInfoTable.Columns.Add(styleColumn);

            // Create a "Id" column. It will be the Element ID
            DataColumn idColum = new DataColumn("Id", typeof(System.String));
            idColum.Caption = "Id";
            changesInfoTable.Columns.Add(idColum);

            // Create a "Name" column. It will be the Element Name
            DataColumn nameColum = new DataColumn("Name", typeof(System.String));
            nameColum.Caption = "Name";
            changesInfoTable.Columns.Add(nameColum);

            // Create a "Category" column. It will be the Category Name of the element.
            DataColumn categoryColum = new DataColumn("Category", typeof(System.String));
            categoryColum.Caption = "Category";
            changesInfoTable.Columns.Add(categoryColum);

            // Create a "Document" column. It will be the document which own the changed element.
            DataColumn docColum = new DataColumn("Document", typeof(System.String));
            docColum.Caption = "Document";
            changesInfoTable.Columns.Add(docColum);

            // return this data table 
            return changesInfoTable;
        }
        #endregion
    }

    /// <summary>
    /// This class inherits IExternalCommand interface and used to retrieve the dialog again.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]

    public class Command : IExternalCommand
    {
        public List<ElementId> _deletedIds = new List<ElementId>();
        #region IExternalCommand Members
        /// <summary>
        /// Implement this method as an external command for Revit.
        /// </summary>
        /// <param name="commandData">An object that is passed to the external application
        /// which contains data related to the command,
        /// such as the application object and active view.</param>
        /// <param name="message">A message that can be set by the external application
        /// which will be displayed if a failure or cancellation is returned by
        /// the external command.</param>
        /// <param name="elements">A set of elements to which the external application
        /// can add elements that are to be highlighted in case of failure or cancellation.</param>
        /// <returns>Return the status of the external command.
        /// A result of Succeeded means that the API external method functioned as expected.
        /// Cancelled can be used to signify that the user cancelled the external operation 
        /// at some point. Failure should be returned if the application is unable to proceed with
        /// the operation.</returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            /*if (ExternalApplication.InfoForm == null)
            {
                ExternalApplication.InfoForm = new ChangesInformationForm(ExternalApplication.ChangesInfoTable);
            }
            ExternalApplication.InfoForm.Show();*/
            ExternalApplication.Toggle();
            ProjectParameterHandler projectParameterHandler = new ProjectParameterHandler();
            ExternalEvent Event = ExternalEvent.Create(projectParameterHandler);
            Event.Raise();
            UIDocument uIDocument = commandData.Application.ActiveUIDocument;
            Document doc = uIDocument.Document;
            if (doc.IsReadOnly)
            {
                MessageBox.Show("doc is read Only");
            }

            return Result.Succeeded;
        }
        #endregion
    }

}




