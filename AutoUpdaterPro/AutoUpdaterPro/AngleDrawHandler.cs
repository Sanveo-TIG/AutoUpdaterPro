using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json;
using Revit.SDK.Samples.AutoUpdaterPro.CS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using TIGUtility;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Line = Autodesk.Revit.DB.Line;

namespace AutoUpdaterPro
{
    [Transaction(TransactionMode.Manual)]
    public class AngleDrawHandler : IExternalEventHandler
    {
        public List<FamilyInstance> unusedfittings = new List<FamilyInstance>();
        public List<Element> _deleteElements = new List<Element>();
        List<Element> SelectedElements = new List<Element>();
        List<Element> DistanceElements = new List<Element>();
        public bool successful;
        bool _isfirst;
        public void Execute(UIApplication uiApp)
        {
            UIDocument _uiDoc = uiApp.ActiveUIDocument;
            Document _doc = _uiDoc.Document;
            try
            {
                successful = false;
                _isfirst = false;
                List<Element> SelectedElements = new List<Element>();
                using Transaction transaction = new Transaction(_doc);
                transaction.Start("Delete Element");

                if (MainWindow.Instance != null && MainWindow.Instance.angleDegree != null && MainWindow.Instance.angleDegree != 0)
                {
                    foreach (ElementId element in ChangesInformationForm.instance._selectedElements.Distinct())
                    {
                        SelectedElements.Add(_doc.GetElement(element));
                    }
                    if (SelectedElements != null && SelectedElements.Count() != 0)
                    {
                        AutoUpdater(uiApp, SelectedElements);
                    }
                    var delete = ChangesInformationForm.instance._deletedIds.Distinct().ToList();
                    if (successful && delete != null)
                    {
                        _doc.Delete(delete);
                    }
                    delete.Clear();
                }
                transaction.Commit();
                ChangesInformationForm.instance._deletedIds.Clear();
                ChangesInformationForm.instance._selectedElements.Clear();
                SelectedElements.Clear();
                MainWindow.Instance.angleDegree = null;
                ChangesInformationForm.instance._refConduitKick.Clear();
                ChangesInformationForm.instance.MidSaddlePt = null;
            }
            catch
            {
                return;
            }
        }

        public void AutoUpdater(UIApplication _uiapp, List<Element> SelectedElements)
        {
            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Application app = _uiapp.Application;
            Document doc = uidoc.Document;
            int.TryParse(_uiapp.Application.VersionNumber, out int RevitVersion);
            string offsetVariable = RevitVersion < 2020 ? "Offset" : "Middle Elevation";

            try
            {
                if (MainWindow.Instance != null)
                {
                    List<Element> SelectElements = new List<Element>();
                    Selection selection = uidoc.Selection;
                    List<ElementId> selectedIds = selection.GetElementIds().ToList();
                    foreach (ElementId elementID in selectedIds)
                    {
                        if (doc.GetElement(elementID).Category != null)
                        {
                            if (doc.GetElement(elementID).Category.Name == "Conduits")
                            {
                                SelectElements.Add(doc.GetElement(elementID));
                            }
                        }
                    }
                    if (MainWindow.Instance._bendElements.Count != 2 * (SelectElements.Count))
                    {
                        SelectedElements.Clear();
                        uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                    }
                }
                if (SelectedElements.Count <= 0)
                {
                    if (SelectedElements == null)
                    {
                        return;
                    }
                    if (SelectedElements.Count() == 0)
                    {
                        System.Windows.MessageBox.Show("Please select the conduits and ensure they have fittings on both sides.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                    }
                }
                if (SelectedElements.Count() != 0)
                {
                    List<Element> conduitCollection = new List<Element>();

                    using (SubTransaction tx = new SubTransaction(doc))
                    {
                        tx.Start();

                        var CongridDictionary1 = Utility.GroupByElements(SelectedElements);
                        Dictionary<double, List<Element>> group = new Dictionary<double, List<Element>>();

                        if (CongridDictionary1.Count == 2)
                        {
                            Dictionary<double, List<Element>> groupPrimary = Utility.GroupByElementsWithElevation(CongridDictionary1.First().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                            Dictionary<double, List<Element>> groupSecondary = Utility.GroupByElementsWithElevation(CongridDictionary1.Last().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                            foreach (var elem in groupPrimary)
                            {
                                foreach (var elem2 in elem.Value)
                                {
                                    DistanceElements.Add(elem2);
                                }
                            }
                            if (groupPrimary.Count == groupSecondary.Count)
                            {
                                for (int i = 0; i < groupPrimary.Count; i++)
                                {

                                    List<Element> primarySortedElementspre = SortbyPlane(doc, groupPrimary.ElementAt(i).Value);

                                    List<Element> secondarySortedElementspre = SortbyPlane(doc, groupSecondary.ElementAt(i).Value);


                                    bool isNotStaright = ReverseingConduits(doc, ref primarySortedElementspre, ref secondarySortedElementspre);

                                    //defind the primary and secondary sets 
                                    double conduitlengthone = primarySortedElementspre[0].LookupParameter("Length").AsDouble();
                                    double conduitlengthsecond = secondarySortedElementspre[0].LookupParameter("Length").AsDouble();
                                    List<Element> primarySortedElements = new List<Element>();
                                    List<Element> secondarySortedElements = new List<Element>();
                                    if (conduitlengthone < conduitlengthsecond)
                                    {
                                        primarySortedElements = primarySortedElementspre;
                                        secondarySortedElements = secondarySortedElementspre;
                                    }
                                    else
                                    {
                                        primarySortedElements = secondarySortedElementspre;
                                        secondarySortedElements = primarySortedElementspre;
                                    }

                                    if (primarySortedElements.Count == secondarySortedElements.Count)
                                    {
                                        Element primaryFirst = primarySortedElements.First();
                                        Element secondaryFirst = secondarySortedElements.First();
                                        Element primaryLast = primarySortedElements.Last();
                                        Element secondaryLast = secondarySortedElements.Last();

                                        XYZ priFirstDir = ((primaryFirst.Location as LocationCurve).Curve as Line).Direction;
                                        XYZ priLastDir = ((primaryLast.Location as LocationCurve).Curve as Line).Direction;
                                        XYZ secFirstDir = ((secondaryFirst.Location as LocationCurve).Curve as Line).Direction;
                                        XYZ secLastDir = ((secondaryLast.Location as LocationCurve).Curve as Line).Direction;

                                        bool isSamDireFirst = Utility.IsSameDirectionWithRoundOff(priFirstDir, secFirstDir, 3) || Utility.IsSameDirectionWithRoundOff(priFirstDir, secLastDir, 3);
                                        bool isSamDireLast = Utility.IsSameDirectionWithRoundOff(priLastDir, secFirstDir, 3) || Utility.IsSameDirectionWithRoundOff(priLastDir, secLastDir, 3);
                                        //Same Elevations 
                                        bool isSamDir = !isNotStaright || isSamDireFirst && isSamDireLast;
                                        if (!isSamDir)
                                        {
                                            Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                            Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                            Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                            Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);

                                            XYZ firstInte = MultiConnectFindIntersectionPoint(priFirst, secFirst);
                                            if (firstInte != null)
                                            {
                                                firstInte = MultiConnectFindIntersectionPoint(priFirst, secLast);

                                                if (firstInte != null)
                                                {
                                                    isSamDir = false;
                                                }
                                            }
                                        }
                                        if (!isSamDir && Math.Round(groupPrimary.ElementAt(i).Key, 4) == Math.Round(groupSecondary.ElementAt(i).Key, 4))
                                        {
                                            //Multi connect
                                            System.Windows.MessageBox.Show("Warning. \n" + "Please use Multi Connect tool for the selected group of conduits to connect", "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                                            successful = false;
                                            return;
                                        }
                                        else if (isSamDir && Math.Round(groupPrimary.ElementAt(i).Key, 4) == Math.Round(groupSecondary.ElementAt(i).Key, 4))
                                        {

                                            Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                            Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                            Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                            Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);
                                            ConnectorSet firstConnectors = null;
                                            ConnectorSet secondConnectors = null;
                                            firstConnectors = Utility.GetConnectors(primaryFirst);
                                            secondConnectors = Utility.GetConnectors(secondaryFirst);
                                            Utility.GetClosestConnectors(firstConnectors, secondConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                            Line checkline = Line.CreateBound(ConnectorOne.Origin, new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z));
                                            XYZ p1 = new XYZ(Math.Round(priFirst.Direction.X, 2), Math.Round(priFirst.Direction.Y, 2), 0);
                                            XYZ p2 = new XYZ(Math.Round(checkline.Direction.X, 2), Math.Round(checkline.Direction.Y, 2), 0);
                                            bool isSamDirecheckline = new XYZ(Math.Abs(p1.X), Math.Abs(p1.Y), 0).IsAlmostEqualTo(new XYZ(Math.Abs(p2.X), Math.Abs(p2.Y), 0));
                                            if (isSamDirecheckline)
                                            {
                                                //Extend
                                                XYZ pickpoint = null;
                                                if (ChangesInformationForm.instance.MidSaddlePt != null)
                                                {
                                                    // ElementId midelm= ChangesInformationForm.instance.MidSaddlePt.Owner.Id;
                                                    var CongridDictionary = Utility.GroupByElements(ChangesInformationForm.instance.MidSaddlePt);
                                                    Dictionary<double, List<Element>> grPrimary = Utility.GroupByElementsWithElevation(CongridDictionary.First().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                                                    Dictionary<double, List<Element>> grSecondary = Utility.GroupByElementsWithElevation(CongridDictionary.Last().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                                                    if (grPrimary.Count == grSecondary.Count)
                                                    {


                                                        List<Element> primarySortedElem = SortbyPlane(doc, grPrimary.ElementAt(i).Value);

                                                        List<Element> secondarySortedElem = SortbyPlane(doc, grSecondary.ElementAt(i).Value);
                                                        ConnectorSet connectorSetOne = Utility.GetConnectors(primarySortedElem.FirstOrDefault());
                                                        ConnectorSet connectorSetTwo = Utility.GetConnectors(secondarySortedElem.FirstOrDefault());
                                                        foreach (Connector connector in connectorSetOne)
                                                        {
                                                            foreach (Connector connector2 in connectorSetTwo)
                                                            {
                                                                ConnectorSet cs = connector.AllRefs;
                                                                ConnectorSet cs2 = connector2.AllRefs;
                                                                foreach (Connector c in cs)
                                                                {
                                                                    foreach (Connector c2 in cs2)
                                                                    {
                                                                        if (c.Owner.Id == c2.Owner.Id)
                                                                        {
                                                                            XYZ ElbowLocationPoint = (doc.GetElement(c.Owner.Id).Location as LocationPoint).Point;
                                                                            pickpoint = ElbowLocationPoint;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }

                                                    }
                                                }
                                                else
                                                {
                                                    pickpoint = Utility.PickPoint(uidoc);
                                                }

                                                if (pickpoint != null)
                                                    ThreePtSaddleExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, pickpoint);

                                            }
                                            else
                                            {
                                                //Hoffset //if
                                                HoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                            }
                                        }
                                        else
                                        {
                                            Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                            Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                            Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                            Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);
                                            ConnectorSet firstConnectors = null;
                                            ConnectorSet secondConnectors = null;
                                            firstConnectors = Utility.GetConnectors(primaryFirst);
                                            secondConnectors = Utility.GetConnectors(secondaryFirst);
                                            Utility.GetClosestConnectors(firstConnectors, secondConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                            Line checkline = Line.CreateBound(ConnectorOne.Origin, new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z));
                                            XYZ p1 = new XYZ(Math.Round(priFirst.Direction.X, 2), Math.Round(priFirst.Direction.Y, 2), 0);
                                            XYZ p2 = new XYZ(Math.Round(checkline.Direction.X, 2), Math.Round(checkline.Direction.Y, 2), 0);
                                            bool isSamDirecheckline = new XYZ(Math.Abs(p1.X), Math.Abs(p1.Y), 0).IsAlmostEqualTo(new XYZ(Math.Abs(p2.X), Math.Abs(p2.Y), 0));

                                            double priSlope = -Math.Round(priFirst.Direction.X, 6) / Math.Round(priFirst.Direction.Y, 6);
                                            double SecSlope = -Math.Round(secFirst.Direction.X, 6) / Math.Round(secFirst.Direction.Y, 6);

                                            if ((priSlope == -1 && SecSlope == 0) || Math.Round((Math.Round(priSlope, 5)) * (Math.Round(SecSlope, 5)), 4) == -1 || Math.Round((Math.Round(priSlope, 5)) * (Math.Round(SecSlope, 5)), 4).ToString() == double.NaN.ToString())
                                            {

                                                //kick
                                                KickExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, i);

                                            }
                                            else if (isSamDirecheckline)
                                            {
                                                //Voffset
                                                VoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                            }
                                            else
                                            {
                                                //Roffset
                                                try
                                                {
                                                    using (SubTransaction trx = new SubTransaction(doc))
                                                    {
                                                        trx.Start();
                                                        List<ElementId> unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Left-Down");
                                                        if (unwantedids.Count > 0)
                                                        {
                                                            foreach (ElementId id in unwantedids)
                                                            {
                                                                doc.Delete(id);
                                                            }
                                                            unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Right-Down");

                                                            if (unwantedids.Count > 0)
                                                            {
                                                                foreach (ElementId id in unwantedids)
                                                                {
                                                                    doc.Delete(id);
                                                                }
                                                                unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Left-Up");

                                                                if (unwantedids.Count > 0)
                                                                {
                                                                    foreach (ElementId id in unwantedids)
                                                                    {
                                                                        doc.Delete(id);
                                                                    }
                                                                    unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Bottom-Left");

                                                                    if (unwantedids.Count > 0)
                                                                    {
                                                                        foreach (ElementId id in unwantedids)
                                                                        {
                                                                            doc.Delete(id);
                                                                        }
                                                                        unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Top-Right");

                                                                        foreach (ElementId id in unwantedids)
                                                                        {
                                                                            doc.Delete(id);
                                                                        }
                                                                    }

                                                                }
                                                            }
                                                        }
                                                        trx.Commit();
                                                        successful = true;
                                                        unusedfittings = unusedfittings.Where(x => (x as FamilyInstance).MEPModel.ConnectorManager.UnusedConnectors.Size == 2).ToList();
                                                        doc.Delete(unusedfittings.Select(r => r.Id).ToList());
                                                    }
                                                    using (SubTransaction txs = new SubTransaction(doc))
                                                    {
                                                        txs.Start();
                                                        Utility.ApplySync(primarySortedElements, _uiapp);
                                                        txs.Commit();
                                                    }
                                                }
                                                catch
                                                {

                                                }

                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (CongridDictionary1.Count == 1)
                        {
                            Utility.GroupByElevation(SelectedElements, offsetVariable, ref group);


                            Dictionary<double, List<Element>> groupPrimary = new Dictionary<double, List<Element>>();
                            Dictionary<double, List<Element>> groupSecondary = new Dictionary<double, List<Element>>();
                            int k = group.Count / 2;
                            for (int i = 0; i < group.Count(); i++)
                            {
                                if (i >= k)
                                {
                                    groupSecondary.Add(group.ElementAt(i).Key, group.ElementAt(i).Value);
                                }
                                else
                                {
                                    groupPrimary.Add(group.ElementAt(i).Key, group.ElementAt(i).Value);
                                }

                            }

                            /////////
                            if (groupPrimary.Count == groupSecondary.Count)
                            {

                                for (int i = 0; i < groupPrimary.Count; i++)
                                {

                                    List<Element> primarySortedElementspre = SortbyPlane(doc, groupPrimary.ElementAt(i).Value);

                                    List<Element> secondarySortedElementspre = SortbyPlane(doc, groupSecondary.ElementAt(i).Value);


                                    bool isNotStaright = ReverseingConduits(doc, ref primarySortedElementspre, ref secondarySortedElementspre);

                                    //defind the primary and secondary sets 
                                    double conduitlengthone = primarySortedElementspre[0].LookupParameter("Length").AsDouble();
                                    double conduitlengthsecond = secondarySortedElementspre[0].LookupParameter("Length").AsDouble();
                                    List<Element> primarySortedElements = new List<Element>();
                                    List<Element> secondarySortedElements = new List<Element>();
                                    if (conduitlengthone < conduitlengthsecond)
                                    {
                                        primarySortedElements = primarySortedElementspre;
                                        secondarySortedElements = secondarySortedElementspre;
                                    }
                                    else
                                    {
                                        primarySortedElements = secondarySortedElementspre;
                                        secondarySortedElements = primarySortedElementspre;
                                    }

                                    if (primarySortedElements.Count == secondarySortedElements.Count)
                                    {
                                        Element primaryFirst = primarySortedElements.First();
                                        Element secondaryFirst = secondarySortedElements.First();
                                        Element primaryLast = primarySortedElements.Last();
                                        Element secondaryLast = secondarySortedElements.Last();

                                        XYZ priFirstDir = ((primaryFirst.Location as LocationCurve).Curve as Line).Direction;
                                        XYZ priLastDir = ((primaryLast.Location as LocationCurve).Curve as Line).Direction;
                                        XYZ secFirstDir = ((secondaryFirst.Location as LocationCurve).Curve as Line).Direction;
                                        XYZ secLastDir = ((secondaryLast.Location as LocationCurve).Curve as Line).Direction;

                                        bool isSamDireFirst = Utility.IsSameDirectionWithRoundOff(priFirstDir, secFirstDir, 3) || Utility.IsSameDirectionWithRoundOff(priFirstDir, secLastDir, 3);
                                        bool isSamDireLast = Utility.IsSameDirectionWithRoundOff(priLastDir, secFirstDir, 3) || Utility.IsSameDirectionWithRoundOff(priLastDir, secLastDir, 3);
                                        //Same Elevations 
                                        bool isSamDir = !isNotStaright || isSamDireFirst && isSamDireLast;
                                        if (!isSamDir)
                                        {
                                            Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                            Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                            Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                            Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);

                                            XYZ firstInte = MultiConnectFindIntersectionPoint(priFirst, secFirst);
                                            if (firstInte != null)
                                            {
                                                firstInte = MultiConnectFindIntersectionPoint(priFirst, secLast);

                                                if (firstInte != null)
                                                {
                                                    isSamDir = false;
                                                }
                                            }
                                        }
                                        if (!isSamDir && Math.Round(groupPrimary.ElementAt(i).Key, 4) == Math.Round(groupSecondary.ElementAt(i).Key, 4))
                                        {
                                            //Multi connect
                                            System.Windows.MessageBox.Show("Warning. \n" + "Please use Multi Connect tool for the selected group of conduits to connect", "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                                            return;
                                        }
                                        else if (isSamDir && Math.Round(groupPrimary.ElementAt(i).Key, 4) == Math.Round(groupSecondary.ElementAt(i).Key, 4))
                                        {
                                            Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                            Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                            Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                            Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);
                                            ConnectorSet firstConnectors = null;
                                            ConnectorSet secondConnectors = null;
                                            firstConnectors = Utility.GetConnectors(primaryFirst);
                                            secondConnectors = Utility.GetConnectors(secondaryFirst);
                                            Utility.GetClosestConnectors(firstConnectors, secondConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                            Line checkline = Line.CreateBound(ConnectorOne.Origin, new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z));
                                            XYZ p1 = new XYZ(Math.Round(priFirst.Direction.X, 2), Math.Round(priFirst.Direction.Y, 2), 0);
                                            XYZ p2 = new XYZ(Math.Round(checkline.Direction.X, 2), Math.Round(checkline.Direction.Y, 2), 0);
                                            bool isSamDirecheckline = new XYZ(Math.Abs(p1.X), Math.Abs(p1.Y), 0).IsAlmostEqualTo(new XYZ(Math.Abs(p2.X), Math.Abs(p2.Y), 0));
                                            if (isSamDirecheckline)
                                            {
                                                //Extend
                                                XYZ pickpoint = new XYZ();
                                                if (ChangesInformationForm.instance.MidSaddlePt != null)
                                                {
                                                    // ElementId midelm= ChangesInformationForm.instance.MidSaddlePt.Owner.Id;
                                                    var CongridDictionary = Utility.GroupByElements(ChangesInformationForm.instance.MidSaddlePt);
                                                    Dictionary<double, List<Element>> grPrimary = Utility.GroupByElementsWithElevation(CongridDictionary.First().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                                                    Dictionary<double, List<Element>> grSecondary = Utility.GroupByElementsWithElevation(CongridDictionary.Last().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                                                    if (grPrimary.Count == grSecondary.Count)
                                                    {
                                                        for (int j = 0; j < grPrimary.Count; j++)
                                                        {

                                                            List<Element> primarySortedElem = SortbyPlane(doc, grPrimary.ElementAt(j).Value);

                                                            List<Element> secondarySortedElem = SortbyPlane(doc, grSecondary.ElementAt(j).Value);
                                                            ConnectorSet connectorSetOne = Utility.GetConnectors(primarySortedElem.FirstOrDefault());
                                                            ConnectorSet connectorSetTwo = Utility.GetConnectors(secondarySortedElem.FirstOrDefault());
                                                            foreach (Connector connector in connectorSetOne)
                                                            {
                                                                foreach (Connector connector2 in connectorSetTwo)
                                                                {
                                                                    ConnectorSet cs = connector.AllRefs;
                                                                    ConnectorSet cs2 = connector2.AllRefs;
                                                                    foreach (Connector c in cs)
                                                                    {
                                                                        foreach (Connector c2 in cs2)
                                                                        {
                                                                            if (c.Owner.Id == c2.Owner.Id)
                                                                            {
                                                                                //List<XYZ> StEn = Utility.GetFittingStartAndEndPoint(doc.GetElement(c.Owner.Id) as FamilyInstance);
                                                                                // Line li = Line.CreateBound(StEn[0], StEn[1]);
                                                                                //XYZ m = Utility.GetMidPoint(li);
                                                                                // XYZ cross = li.Direction.CrossProduct(XYZ.BasisZ);
                                                                                // XYZ newStart = m + cross.Multiply(1);
                                                                                // XYZ newEnd = m - cross.Multiply(1);
                                                                                // Line verticalLine = Line.CreateBound(newStart, newEnd);
                                                                                // XYZ interSectionPoint = Utility.FindIntersectionPoint(verticalLine,li);
                                                                                // MessageBox.Show(interSectionPoint.ToString());
                                                                                XYZ ElbowLocationPoint = (doc.GetElement(c.Owner.Id).Location as LocationPoint).Point;
                                                                                pickpoint = ElbowLocationPoint;//new XYZ(c.Origin.X, c.Origin.Y, 0);
                                                                                break;
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }


                                                        }
                                                    }


                                                }
                                                else
                                                {
                                                    pickpoint = Utility.PickPoint(uidoc);
                                                }
                                                if (pickpoint != null)
                                                    ThreePtSaddleExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, pickpoint);

                                            }
                                            else
                                            {
                                                //Hoffset //else if
                                                HoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                            }
                                        }
                                        else
                                        {
                                            Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                            Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                            Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                            Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);
                                            ConnectorSet firstConnectors = null;
                                            ConnectorSet secondConnectors = null;
                                            firstConnectors = Utility.GetConnectors(primaryFirst);
                                            secondConnectors = Utility.GetConnectors(secondaryFirst);
                                            Utility.GetClosestConnectors(firstConnectors, secondConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                            Line checkline = Line.CreateBound(ConnectorOne.Origin, new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z));
                                            XYZ p1 = new XYZ(Math.Round(priFirst.Direction.X, 2), Math.Round(priFirst.Direction.Y, 2), 0);
                                            XYZ p2 = new XYZ(Math.Round(checkline.Direction.X, 2), Math.Round(checkline.Direction.Y, 2), 0);
                                            bool isSamDirecheckline = new XYZ(Math.Abs(p1.X), Math.Abs(p1.Y), 0).IsAlmostEqualTo(new XYZ(Math.Abs(p2.X), Math.Abs(p2.Y), 0));

                                            double priSlope = -Math.Round(priFirst.Direction.X, 6) / Math.Round(priFirst.Direction.Y, 6);
                                            double SecSlope = -Math.Round(secFirst.Direction.X, 6) / Math.Round(secFirst.Direction.Y, 6);

                                            if ((priSlope == -1 && SecSlope == 0) || Math.Round((Math.Round(priSlope, 5)) * (Math.Round(SecSlope, 5)), 4) == -1 || Math.Round((Math.Round(priSlope, 5)) * (Math.Round(SecSlope, 5)), 4).ToString() == double.NaN.ToString())
                                            {
                                                //kick

                                                KickExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, i);
                                            }
                                            else if (isSamDirecheckline)
                                            {
                                                //Voffset
                                                VoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                            }
                                            else
                                            {
                                                //Roffset
                                                try
                                                {
                                                    using (SubTransaction trx = new SubTransaction(doc))
                                                    {
                                                        trx.Start();
                                                        List<ElementId> unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Left-Down");
                                                        if (unwantedids.Count > 0)
                                                        {
                                                            foreach (ElementId id in unwantedids)
                                                            {
                                                                doc.Delete(id);
                                                            }
                                                            unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Right-Down");

                                                            if (unwantedids.Count > 0)
                                                            {
                                                                foreach (ElementId id in unwantedids)
                                                                {
                                                                    doc.Delete(id);
                                                                }
                                                                unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Left-Up");

                                                                if (unwantedids.Count > 0)
                                                                {
                                                                    foreach (ElementId id in unwantedids)
                                                                    {
                                                                        doc.Delete(id);
                                                                    }
                                                                    unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Bottom-Left");

                                                                    if (unwantedids.Count > 0)
                                                                    {
                                                                        foreach (ElementId id in unwantedids)
                                                                        {
                                                                            doc.Delete(id);
                                                                        }
                                                                        unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Top-Right");

                                                                        foreach (ElementId id in unwantedids)
                                                                        {
                                                                            doc.Delete(id);
                                                                        }
                                                                    }

                                                                }
                                                            }
                                                        }
                                                        trx.Commit();
                                                        unusedfittings = unusedfittings.Where(x => (x as FamilyInstance).MEPModel.ConnectorManager.UnusedConnectors.Size == 2).ToList();
                                                        doc.Delete(unusedfittings.Select(r => r.Id).ToList());
                                                    }
                                                    using (SubTransaction txs = new SubTransaction(doc))
                                                    {
                                                        txs.Start();
                                                        Utility.ApplySync(primarySortedElements, _uiapp);
                                                        txs.Commit();
                                                    }
                                                }
                                                catch
                                                {

                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {

                            //  trns.RollBack();

                        }
                        tx.Commit();

                    }
                }
                SelectedElements.Clear();
                uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

            }
            finally
            {
            }
        }

        #region connectors
        public void HoffsetExecute(UIApplication uiapp, ref List<Element> PrimaryElements, ref List<Element> SecondaryElements)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            int.TryParse(uiapp.Application.VersionNumber, out int RevitVersion);
            string offsetVariable = RevitVersion < 2020 ? "Offset" : "Middle Elevation";
            DateTime startDate = DateTime.UtcNow;
            ElementsFilter filter = new ElementsFilter("Conduits");
            double angle = Convert.ToDouble(MainWindow.Instance.angleDegree) * (Math.PI / 180);
            try
            {
                List<Element> thirdElements = new List<Element>();

                bool isVerticalConduits = false;
                using (SubTransaction tx = new SubTransaction(doc))
                {
                    tx.Start();
                    Line refloineforanglecheck = null;
                    for (int i = 0; i < PrimaryElements.Count; i++)
                    {
                        Element firstElement = PrimaryElements[i];
                        Element secondElement = SecondaryElements[i];
                        Line firstLine = (firstElement.Location as LocationCurve).Curve as Line;
                        Line secondLine = (secondElement.Location as LocationCurve).Curve as Line;
                        Line newLine = Utility.GetParallelLine(firstElement, secondElement, ref isVerticalConduits);
                        double elevation = firstElement.LookupParameter(offsetVariable).AsDouble();
                        XYZ newlineSeconpoint = newLine.GetEndPoint(0) + newLine.Direction.Multiply(20);
                        Conduit thirdConduit = Utility.CreateConduit(doc, firstElement as Conduit, newLine.GetEndPoint(0), newLine.GetEndPoint(1));
                        Element thirdElement = doc.GetElement(thirdConduit.Id);
                        thirdElements.Add(thirdElement);
                        if (i == 0)
                        {
                            refloineforanglecheck = newLine;
                        }

                    }
                    //Rotate Elements at Once
                    Element ElementOne = PrimaryElements[0];
                    Element ElementTwo = SecondaryElements[0];
                    Utility.GetClosestConnectors(ElementOne, ElementTwo, out Connector ConnectorOne, out Connector ConnectorTwo);
                    XYZ axisStart = ConnectorOne.Origin;
                    XYZ axisEnd = new XYZ(axisStart.X, axisStart.Y, axisStart.Z + 10);
                    Line axisLine = Line.CreateBound(axisStart, axisEnd);
                    if (isVerticalConduits)
                    {
                        axisEnd = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z);
                        axisLine = Line.CreateBound(axisStart, axisEnd);
                        XYZ dir = axisLine.Direction;
                        dir = new XYZ(dir.X, dir.Y, 0);
                        XYZ cross = dir.CrossProduct(XYZ.BasisZ);
                        Element ele = thirdElements[0];
                        LocationCurve newconcurve = ele.Location as LocationCurve;
                        Line ncl1 = newconcurve.Curve as Line;
                        XYZ MidPoint = ncl1.GetEndPoint(0);
                        axisLine = Line.CreateBound(MidPoint, MidPoint + cross.Multiply(10));
                        ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, -angle);
                    }
                    else
                    {
                        ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, angle);

                        //find right angle
                        Conduit refcondone = SecondaryElements[0] as Conduit;
                        Line refcondoneline = (refcondone.Location as LocationCurve).Curve as Line;
                        XYZ refcondonelinedir = refcondoneline.Direction;
                        XYZ refcondonelinemidept = (refcondoneline.GetEndPoint(0) + refcondoneline.GetEndPoint(1)) / 2;
                        XYZ addedpt1 = refcondonelinemidept + refcondonelinedir.Multiply(250);
                        XYZ addedpt2 = refcondonelinemidept - refcondonelinedir.Multiply(250);
                        Line addedline = Line.CreateBound(addedpt1, addedpt2);

                        Conduit refcondtwo = thirdElements[0] as Conduit;
                        Line refcondtwoline = (refcondtwo.Location as LocationCurve).Curve as Line;
                        XYZ newlineSeconpoint = refcondtwoline.GetEndPoint(0) + refcondtwoline.Direction.Multiply(20);
                        addedpt1 = new XYZ(addedpt1.X, addedpt1.Y, newlineSeconpoint.Z);
                        addedpt2 = new XYZ(addedpt2.X, addedpt2.Y, newlineSeconpoint.Z);
                        refcondtwoline = Line.CreateBound(refcondtwoline.GetEndPoint(0), newlineSeconpoint);
                        addedline = Line.CreateBound(addedpt1, addedpt2);


                        XYZ intersectionpoint = Utility.GetIntersection(addedline, refcondtwoline);

                        if (intersectionpoint == null)
                        {
                            ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, -2 * angle);
                        }
                    }


                    for (int i = 0; i < PrimaryElements.Count; i++)
                    {
                        Element firstElement = PrimaryElements[i];
                        Element secondElement = SecondaryElements[i];
                        Element thirdElement = thirdElements[i];
                        Utility.CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp);
                        Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                    }
                    tx.Commit();
                    successful = true;
                }
            }
            catch
            {
                try
                {
                    List<Element> thirdElements = new List<Element>();

                    bool isVerticalConduits = false;
                    using (SubTransaction tx = new SubTransaction(doc))
                    {
                        tx.Start();
                        for (int i = 0; i < PrimaryElements.Count; i++)
                        {
                            Element firstElement = PrimaryElements[i];
                            Element secondElement = SecondaryElements[i];
                            Line firstLine = (firstElement.Location as LocationCurve).Curve as Line;
                            Line secondLine = (secondElement.Location as LocationCurve).Curve as Line;
                            Line newLine = Utility.GetParallelLine(firstElement, secondElement, ref isVerticalConduits);
                            double elevation = firstElement.LookupParameter(offsetVariable).AsDouble();
                            Conduit thirdConduit = Utility.CreateConduit(doc, firstElement as Conduit, newLine.GetEndPoint(0), newLine.GetEndPoint(1));
                            Element thirdElement = doc.GetElement(thirdConduit.Id);
                            thirdElements.Add(thirdElement);
                        }
                        //Rotate Elements at Once

                        Element ElementOne = PrimaryElements[0];
                        Element ElementTwo = SecondaryElements[0];
                        Utility.GetClosestConnectors(ElementOne, ElementTwo, out Connector ConnectorOne, out Connector ConnectorTwo);
                        XYZ axisStart = ConnectorOne.Origin;
                        XYZ axisEnd = new XYZ(axisStart.X, axisStart.Y, axisStart.Z + 10);
                        Line axisLine = Line.CreateBound(axisStart, axisEnd);
                        if (isVerticalConduits)
                        {
                            axisEnd = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z);
                            axisLine = Line.CreateBound(axisStart, axisEnd);
                            XYZ dir = axisLine.Direction;
                            dir = new XYZ(dir.X, dir.Y, 0);
                            XYZ cross = dir.CrossProduct(XYZ.BasisZ);
                            Element ele = thirdElements[0];
                            LocationCurve newconcurve = ele.Location as LocationCurve;
                            Line ncl1 = newconcurve.Curve as Line;
                            XYZ MidPoint = ncl1.GetEndPoint(0);
                            axisLine = Line.CreateBound(MidPoint, MidPoint + cross.Multiply(10));
                            ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, angle);
                            Line rotatedLine = Line.CreateBound(ncl1.GetEndPoint(0) - ncl1.Direction.Multiply(10), ncl1.GetEndPoint(1) + ncl1.Direction.Multiply(10));
                        }
                        else
                        {
                            ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, -angle);
                        }
                        for (int i = 0; i < PrimaryElements.Count; i++)
                        {
                            Element firstElement = PrimaryElements[i];
                            Element secondElement = SecondaryElements[i];
                            Element thirdElement = thirdElements[i];
                            Utility.CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp);
                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                        }
                        tx.Commit();
                        successful = true;
                    }
                }
                catch
                {
                    try
                    {
                        List<Element> thirdElements = new List<Element>();

                        bool isVerticalConduits = false;
                        using (SubTransaction tx = new SubTransaction(doc))
                        {
                            tx.Start();
                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                Element firstElement = PrimaryElements[i];
                                Element secondElement = SecondaryElements[i];
                                Line firstLine = (firstElement.Location as LocationCurve).Curve as Line;
                                Line secondLine = (secondElement.Location as LocationCurve).Curve as Line;
                                Line newLine = Utility.GetParallelLine(firstElement, secondElement, ref isVerticalConduits);
                                double elevation = firstElement.LookupParameter(offsetVariable).AsDouble();
                                Conduit thirdConduit = Utility.CreateConduit(doc, firstElement as Conduit, newLine.GetEndPoint(0), newLine.GetEndPoint(1));
                                Element thirdElement = doc.GetElement(thirdConduit.Id);
                                thirdElements.Add(thirdElement);
                            }
                            //Rotate Elements at Once

                            Element ElementOne = PrimaryElements[0];
                            Element ElementTwo = SecondaryElements[0];
                            Utility.GetClosestConnectors(ElementOne, ElementTwo, out Connector ConnectorOne, out Connector ConnectorTwo);
                            XYZ axisStart = ConnectorOne.Origin;
                            XYZ axisEnd = new XYZ(axisStart.X, axisStart.Y, axisStart.Z + 10);
                            Line axisLine = Line.CreateBound(axisStart, axisEnd);
                            if (isVerticalConduits)
                            {
                                axisEnd = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z);
                                axisLine = Line.CreateBound(axisStart, axisEnd);
                                XYZ dir = axisLine.Direction;
                                dir = new XYZ(dir.X, dir.Y, 0);
                                XYZ cross = dir.CrossProduct(XYZ.BasisZ);
                                Element ele = thirdElements[0];
                                LocationCurve newconcurve = ele.Location as LocationCurve;
                                Line ncl1 = newconcurve.Curve as Line;
                                XYZ MidPoint = ncl1.GetEndPoint(0);
                                axisLine = Line.CreateBound(MidPoint, MidPoint + cross.Multiply(10));
                                ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, angle);
                                Line rotatedLine = Line.CreateBound(ncl1.GetEndPoint(0) - ncl1.Direction.Multiply(10), ncl1.GetEndPoint(1) + ncl1.Direction.Multiply(10));
                            }
                            else
                            {
                                ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, angle);
                            }
                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                Element firstElement = PrimaryElements[i];
                                Element secondElement = SecondaryElements[i];
                                Element thirdElement = thirdElements[i];
                                //Utility.CreateElbowFittings(thirdElement, firstElement, doc, PrimaryElements[i], true);
                                Utility.CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp);
                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                            }
                            tx.Commit();
                            successful = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show("Warning. \n" + ex.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                        successful = false;
                    }
                }
            }


        }
        public void VoffsetExecute(UIApplication uiapp, ref List<Element> PrimaryElements, ref List<Element> SecondaryElements)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            int.TryParse(uiapp.Application.VersionNumber, out int RevitVersion);
            List<Element> _deleteElements = new List<Element>();
            List<Element> SelectedElements = new List<Element>();
            ElementsFilter filter = new ElementsFilter("Conduits");

            try
            {

                //PrimaryElements = Elements.GetElementsByReference(PrimaryReference, doc);
                //SecondaryElements = Elements.GetElementsByReference(SecondaryReference, doc);
                PrimaryElements = GetElementsByOder(PrimaryElements);
                SecondaryElements = GetElementsByOder(SecondaryElements);
                List<Element> thirdElements = new List<Element>();
                bool isVerticalConduits = false;
                // Modify document within a transaction
                try
                {
                    using (SubTransaction tx = new SubTransaction(doc))
                    {
                        ConnectorSet PrimaryConnectors = null;
                        ConnectorSet SecondaryConnectors = null;
                        Connector ConnectorOne = null;
                        Connector ConnectorTwo = null;
                        tx.Start();
                        double l_angle = Convert.ToDouble(MainWindow.Instance.angleDegree) * (Math.PI / 180);
                        for (int i = 0; i < PrimaryElements.Count; i++)
                        {
                            List<XYZ> ConnectorPoints = new List<XYZ>();
                            PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                            SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                            Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out ConnectorOne, out ConnectorTwo);
                            foreach (Connector con in PrimaryConnectors)
                            {
                                ConnectorPoints.Add(con.Origin);
                            }
                            XYZ newenpt = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z);
                            Conduit newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, ConnectorOne.Origin, newenpt);
                            if (Utility.IsXYTrue(ConnectorPoints.FirstOrDefault(), ConnectorPoints.LastOrDefault()))
                            {
                                isVerticalConduits = true;
                            }
                            Element e = doc.GetElement(newCon.Id);
                            thirdElements.Add(e);
                            //RetainParameters(PrimaryElements[i], SecondaryElements[i]);
                            //RetainParameters(PrimaryElements[i], e);
                        }
                        //Rotate Elements at Once
                        Element ElementOne = PrimaryElements[0];
                        Element ElementTwo = SecondaryElements[0];
                        Utility.GetClosestConnectors(ElementOne, ElementTwo, out ConnectorOne, out ConnectorTwo);
                        LocationCurve newconcurve = thirdElements[0].Location as LocationCurve;
                        Line ncl1 = newconcurve.Curve as Line;
                        XYZ direction = ncl1.Direction;
                        XYZ axisStart = ConnectorOne.Origin;
                        XYZ axisEnd = axisStart.Add(XYZ.BasisZ.CrossProduct(direction));
                        Line axisLine = Line.CreateBound(axisStart, axisEnd);
                        double PrimaryOffset = RevitVersion < 2020 ? PrimaryElements[0].LookupParameter("Offset").AsDouble() :
                                                 PrimaryElements[0].LookupParameter("Middle Elevation").AsDouble();
                        double SecondaryOffset = RevitVersion < 2020 ? SecondaryElements[0].LookupParameter("Offset").AsDouble() :
                                                  SecondaryElements[0].LookupParameter("Middle Elevation").AsDouble();
                        if (isVerticalConduits)
                        {
                            l_angle = (Math.PI / 2) - l_angle;
                        }
                        if (PrimaryOffset > SecondaryOffset)
                        {
                            //rotate down
                            l_angle = -l_angle;
                        }
                        try
                        {
                            ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, -l_angle);
                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                Element firstElement = PrimaryElements[i];
                                Element secondElement = SecondaryElements[i];
                                Element thirdElement = thirdElements[i];
                                Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                Utility.AutoRetainParameters(PrimaryElements[i], thirdElement, doc, uiapp);
                                try
                                {

                                    Utility.CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp);
                                    Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                }
                                catch
                                {
                                    Utility.CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp);
                                    Utility.CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp);

                                }
                            }
                        }
                        catch (Exception)
                        {

                            ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, l_angle * 2 + Math.PI);

                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                Element firstElement = PrimaryElements[i];
                                Element secondElement = SecondaryElements[i];
                                Element thirdElement = thirdElements[i];
                                Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                Utility.AutoRetainParameters(PrimaryElements[i], thirdElement, doc, uiapp);
                                //Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                //Utility.CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp);
                                try
                                {
                                    _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));
                                    _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                }
                                catch
                                {
                                    try
                                    {

                                        _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                        _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));
                                    }
                                    catch
                                    {

                                        try
                                        {

                                            _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));
                                            _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                        }
                                        catch
                                        {
                                            try
                                            {

                                                _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                                _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));

                                            }
                                            catch
                                            {
                                                try
                                                {

                                                    _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));
                                                    _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));

                                                }
                                                catch
                                                {
                                                    try
                                                    {

                                                        _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));
                                                        _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));
                                                    }
                                                    catch
                                                    {
                                                        try
                                                        {

                                                            _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                                            _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                                        }
                                                        catch
                                                        {
                                                            try
                                                            {
                                                                _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                                                _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                                            }
                                                            catch
                                                            {

                                                                string message = string.Format("Make sure conduits are having less overlap, if not please reduce the overlapping distance.");
                                                                System.Windows.MessageBox.Show("Warning. \n" + message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                                                                successful = false;
                                                                return;
                                                            }


                                                        }


                                                    }



                                                }
                                            }

                                        }
                                    }


                                }

                            }
                        }

                        tx.Commit();
                        successful = true;
                        doc.Regenerate();

                    }
                    using (SubTransaction tx = new SubTransaction(doc))
                    {
                        tx.Start();
                        Utility.ApplySync(PrimaryElements, uiapp);
                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    string message = string.Format("Make sure conduits are aligned to each other properly, if not please align primary conduit to secondary conduit. Error :{0}", ex.Message);
                    System.Windows.MessageBox.Show("Warning. \n" + message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                    successful = false;

                }
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show("Warning. \n" + exception.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                successful = false;
            }
        }
        public List<ElementId> RoffsetExecute(UIApplication uiapp, ref List<Element> PrimaryElements, ref List<Element> SecondaryElements, string l_direction)
        {

            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            double l_angle;
            int.TryParse(uiapp.Application.VersionNumber, out int RevitVersion);
            string offsetVariable = RevitVersion < 2020 ? "Offset" : "Middle Elevation";
            double elevationOne = PrimaryElements[0].LookupParameter(offsetVariable).AsDouble();
            double elevationTwo = SecondaryElements[0].LookupParameter(offsetVariable).AsDouble();
            l_angle = Convert.ToDouble(MainWindow.Instance.angleDegree);
            bool isRollUp = elevationOne < elevationTwo;
            List<ElementId> Unwantedids;
            if (isRollUp)
            {
                Unwantedids = RollUp(doc, uidoc, PrimaryElements, SecondaryElements, l_angle, l_direction, offsetVariable, uiapp);
            }
            else
            {
                Unwantedids = RollDown(doc, uidoc, PrimaryElements, SecondaryElements, l_angle, l_direction, offsetVariable, uiapp);
            }



            return Unwantedids;
        }
        public void KickExecute(UIApplication uiapp, ref List<Element> PrimaryElements, ref List<Element> SecondaryElements, int first)
        {

            DateTime startDate = DateTime.UtcNow;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            int.TryParse(uiapp.Application.VersionNumber, out int RevitVersion);
            string offsetVariable = RevitVersion < 2020 ? "Offset" : "Middle Elevation";
            try
            {
                Reference reference = null;
                if (first == 0)
                {
                    ElementsFilter filter = new ElementsFilter("Conduits");
                    //if (ChangesInformationForm.instance._refConduitKick == null)
                    //{
                    reference = uidoc.Selection.PickObject(ObjectType.Element, filter, "Please select the conduit in group to define 90 near and 90 far");
                    //}
                    //ElementId refId = ChangesInformationForm.instance._refConduitKick[0];
                    //if (!PrimaryElements.Any(e => e.Id == refId))
                    if (!PrimaryElements.Any(e => e.Id == doc.GetElement(reference.ElementId).Id))
                    {
                        var temp = PrimaryElements;
                        PrimaryElements = SecondaryElements;
                        SecondaryElements = temp;

                        _isfirst = true;
                    }
                }
                if (first > 0)
                {
                    if (_isfirst)
                    {
                        var temp = PrimaryElements;
                        PrimaryElements = SecondaryElements;
                        SecondaryElements = temp;
                    }
                }

                double l_angle;
                bool isUp = PrimaryElements.FirstOrDefault().LookupParameter(offsetVariable).AsDouble() <
                    SecondaryElements.FirstOrDefault().LookupParameter(offsetVariable).AsDouble();
                if (!isUp)
                {
                    l_angle = Convert.ToDouble(MainWindow.Instance.angleDegree) * (Math.PI / 180);
                    try
                    {
                        using (SubTransaction tx = new SubTransaction(doc))
                        {
                            ConnectorSet PrimaryConnectors = null;
                            ConnectorSet SecondaryConnectors = null;
                            ConnectorSet ThirdConnectors = null;
                            tx.Start();
                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                double elevation = PrimaryElements[i].LookupParameter(offsetVariable).AsDouble();
                                LocationCurve lc1 = PrimaryElements[i].Location as LocationCurve;
                                Line l1 = lc1.Curve as Line;
                                LocationCurve lc2 = SecondaryElements[i].Location as LocationCurve;
                                Line l2 = lc2.Curve as Line;
                                XYZ interSecPoint = Utility.FindIntersectionPoint(l1.GetEndPoint(0), l1.GetEndPoint(1), l2.GetEndPoint(0), l2.GetEndPoint(1));
                                PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                                SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                                Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                XYZ EndPoint = ConnectorTwo.Origin;
                                XYZ NewEndPoint = new XYZ(interSecPoint.X, interSecPoint.Y, EndPoint.Z);
                                Conduit newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, EndPoint, NewEndPoint);
                                newCon.LookupParameter(offsetVariable).Set(elevation);
                                Element e = doc.GetElement(newCon.Id);
                                LocationCurve newConcurve = newCon.Location as LocationCurve;
                                Line ncl1 = newConcurve.Curve as Line;
                                XYZ ncenpt = ncl1.GetEndPoint(1);
                                XYZ direction = ncl1.Direction;
                                XYZ midPoint = ncenpt;
                                XYZ midHigh = midPoint.Add(XYZ.BasisZ.CrossProduct(direction));
                                Line axisLine = Line.CreateBound(midPoint, midHigh);
                                newConcurve.Rotate(axisLine, -l_angle);
                                ThirdConnectors = Utility.GetConnectors(e);
                                Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                                Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp);
                                Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp);
                            }
                            tx.Commit();
                            successful = true;
                        }
                    }
                    catch
                    {
                        try
                        {
                            using (SubTransaction tx = new SubTransaction(doc))
                            {
                                ConnectorSet PrimaryConnectors = null;
                                ConnectorSet SecondaryConnectors = null;
                                ConnectorSet ThirdConnectors = null;

                                tx.Start();
                                for (int i = 0; i < PrimaryElements.Count; i++)
                                {
                                    double elevation = PrimaryElements[i].LookupParameter(offsetVariable).AsDouble();
                                    LocationCurve lc1 = PrimaryElements[i].Location as LocationCurve;
                                    Line l1 = lc1.Curve as Line;
                                    LocationCurve lc2 = SecondaryElements[i].Location as LocationCurve;
                                    Line l2 = lc2.Curve as Line;
                                    XYZ interSecPoint = Utility.FindIntersectionPoint(l1.GetEndPoint(0), l1.GetEndPoint(1), l2.GetEndPoint(0), l2.GetEndPoint(1));
                                    PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                                    SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                                    Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                    XYZ EndPoint = ConnectorTwo.Origin;
                                    XYZ NewEndPoint = new XYZ(interSecPoint.X, interSecPoint.Y, EndPoint.Z);
                                    Conduit newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, EndPoint, NewEndPoint);
                                    newCon.LookupParameter(offsetVariable).Set(elevation);
                                    Element e = doc.GetElement(newCon.Id);
                                    LocationCurve newConcurve = newCon.Location as LocationCurve;
                                    Line ncl1 = newConcurve.Curve as Line;
                                    XYZ ncenpt = ncl1.GetEndPoint(1);
                                    XYZ direction = ncl1.Direction;
                                    XYZ midPoint = ncenpt;
                                    XYZ midHigh = midPoint.Add(XYZ.BasisZ.CrossProduct(direction));
                                    Line axisLine = Line.CreateBound(midPoint, midHigh);
                                    newConcurve.Rotate(axisLine, l_angle);
                                    ThirdConnectors = Utility.GetConnectors(e);
                                    Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                    Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                                    Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp);
                                    Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp);
                                }
                                tx.Commit();
                                successful = true;
                            }
                        }
                        catch (Exception exception)
                        {
                            System.Windows.MessageBox.Show("Warning. \n" + exception.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                            successful = false;
                        }
                    }
                }
                if (isUp)
                {
                    l_angle = Convert.ToDouble(MainWindow.Instance.angleDegree) * (Math.PI / 180);
                    try
                    {

                        using (SubTransaction tx = new SubTransaction(doc))
                        {
                            ConnectorSet PrimaryConnectors = null;
                            ConnectorSet SecondaryConnectors = null;
                            ConnectorSet ThirdConnectors = null;

                            tx.Start();
                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                double elevation = PrimaryElements[i].LookupParameter(offsetVariable).AsDouble();
                                LocationCurve lc1 = PrimaryElements[i].Location as LocationCurve;
                                Line l1 = lc1.Curve as Line;
                                LocationCurve lc2 = SecondaryElements[i].Location as LocationCurve;
                                Line l2 = lc2.Curve as Line;
                                XYZ interSecPoint = Utility.FindIntersectionPoint(l1.GetEndPoint(0), l1.GetEndPoint(1), l2.GetEndPoint(0), l2.GetEndPoint(1));
                                PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                                SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                                Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                XYZ EndPoint = ConnectorTwo.Origin;
                                XYZ NewEndPoint = new XYZ(interSecPoint.X, interSecPoint.Y, EndPoint.Z);
                                Conduit newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, EndPoint, NewEndPoint);
                                newCon.LookupParameter(offsetVariable).Set(elevation);
                                Element e = doc.GetElement(newCon.Id);
                                LocationCurve newConcurve = newCon.Location as LocationCurve;
                                Line ncl1 = newConcurve.Curve as Line;
                                XYZ ncenpt = ncl1.GetEndPoint(1);
                                XYZ direction = ncl1.Direction;
                                XYZ midPoint = ncenpt;
                                XYZ midHigh = midPoint.Add(XYZ.BasisZ.CrossProduct(direction));
                                Line axisLine = Line.CreateBound(midPoint, midHigh);
                                newConcurve.Rotate(axisLine, l_angle);
                                ThirdConnectors = Utility.GetConnectors(e);
                                Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                                Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp);
                                Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp);
                            }
                            tx.Commit();
                            successful = true;

                        }
                    }
                    catch
                    {
                        try
                        {
                            using (SubTransaction tx = new SubTransaction(doc))
                            {
                                ConnectorSet PrimaryConnectors = null;
                                ConnectorSet SecondaryConnectors = null;
                                ConnectorSet ThirdConnectors = null;

                                tx.Start();
                                for (int i = 0; i < PrimaryElements.Count; i++)
                                {
                                    double elevation = PrimaryElements[i].LookupParameter(offsetVariable).AsDouble();
                                    LocationCurve lc1 = PrimaryElements[i].Location as LocationCurve;
                                    Line l1 = lc1.Curve as Line;
                                    LocationCurve lc2 = SecondaryElements[i].Location as LocationCurve;
                                    Line l2 = lc2.Curve as Line;
                                    XYZ interSecPoint = Utility.FindIntersectionPoint(l1.GetEndPoint(0), l1.GetEndPoint(1), l2.GetEndPoint(0), l2.GetEndPoint(1));
                                    PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                                    SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                                    Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                    XYZ EndPoint = ConnectorTwo.Origin;
                                    XYZ NewEndPoint = new XYZ(interSecPoint.X, interSecPoint.Y, EndPoint.Z);
                                    Conduit newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, EndPoint, NewEndPoint);
                                    newCon.LookupParameter(offsetVariable).Set(elevation);
                                    Element e = doc.GetElement(newCon.Id);
                                    LocationCurve newConcurve = newCon.Location as LocationCurve;
                                    Line ncl1 = newConcurve.Curve as Line;
                                    XYZ ncenpt = ncl1.GetEndPoint(1);
                                    XYZ direction = ncl1.Direction;
                                    XYZ midPoint = ncenpt;
                                    XYZ midHigh = midPoint.Add(XYZ.BasisZ.CrossProduct(direction));
                                    Line axisLine = Line.CreateBound(midPoint, midHigh);
                                    newConcurve.Rotate(axisLine, -l_angle);
                                    ThirdConnectors = Utility.GetConnectors(e);
                                    Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                    Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                                    Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp);
                                    Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp);
                                }
                                tx.Commit();
                                successful = true;
                            }
                        }
                        catch (Exception exception)
                        {
                            System.Windows.MessageBox.Show("Warning. \n" + exception.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                            successful = false;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show("Warning. \n" + exception.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                successful = false;
            }
        }
        public void ThreePtSaddleExecute(UIApplication uiapp, ref List<Element> PrimaryElements, ref List<Element> SecondaryElements, XYZ pickpoint)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            int.TryParse(uiapp.Application.VersionNumber, out int RevitVersion);
            ElementsFilter filter = new ElementsFilter("Conduit Tags");

            try
            {

                //PrimaryElements = Elements.GetElementsByReference(PrimaryReference, doc);
                //SecondaryElements = Elemen    ts.GetElementsByReference(SecondaryReference, doc);
                LocationCurve findDirec = PrimaryElements[0].Location as LocationCurve;
                Line n = findDirec.Curve as Line;
                XYZ dire = n.Direction;
                XYZ MPtAngle = new XYZ();
                Connector ConnectOne = null;
                Connector ConnectTwo = null;
                Utility.GetClosestConnectors(PrimaryElements[0], SecondaryElements[0], out ConnectOne, out ConnectTwo);
                XYZ ax = ConnectOne.Origin;
                Line pickline = null;

                pickline = Line.CreateBound(pickpoint, pickpoint + new XYZ(dire.X + 10, dire.Y, dire.Z));
                if (dire.X == 1 || dire.X == -1)
                {
                    if (pickline.Origin.Y < ax.Y)
                    {
                        PrimaryElements = GetElementsByOderDescending(PrimaryElements);
                        SecondaryElements = GetElementsByOderDescending(SecondaryElements);
                    }
                    else
                    {
                        PrimaryElements = GetElementsByOder(PrimaryElements);
                        SecondaryElements = GetElementsByOder(SecondaryElements);
                    }
                }
                else if (dire.Y == -1 || dire.Y == 1)
                {

                    if (pickline.Origin.X < ax.X)
                    {
                        PrimaryElements = GetElementsByOderDescending(PrimaryElements);
                        SecondaryElements = GetElementsByOderDescending(SecondaryElements);
                    }
                    else
                    {
                        PrimaryElements = GetElementsByOder(PrimaryElements);
                        SecondaryElements = GetElementsByOder(SecondaryElements);
                    }
                }
                else
                {
                    if (pickline.Origin.X < ax.X)
                    {
                        if (dire.X == -1)
                        {
                            PrimaryElements = GetElementsByOderDescending(PrimaryElements);
                            SecondaryElements = GetElementsByOderDescending(SecondaryElements);
                        }
                        else
                        {
                            PrimaryElements = GetElementsByOder(PrimaryElements);
                            SecondaryElements = GetElementsByOder(SecondaryElements);
                        }
                    }
                    else
                    {
                        if (pickline.Origin.X < ax.X)
                        {

                            PrimaryElements = GetElementsByOderDescending(PrimaryElements);
                            SecondaryElements = GetElementsByOderDescending(SecondaryElements);
                        }
                        else
                        {
                            PrimaryElements = GetElementsByOder(PrimaryElements);
                            SecondaryElements = GetElementsByOder(SecondaryElements);
                        }
                    }
                }


                List<Element> thirdElements = new List<Element>();
                List<Element> forthElements = new List<Element>();
                bool isVerticalConduits = false;
                // Modify document within a transaction
                try
                {
                    using (SubTransaction tx = new SubTransaction(doc))
                    {
                        ConnectorSet PrimaryConnectors = null;
                        ConnectorSet SecondaryConnectors = null;
                        Connector ConnectorOne = null;
                        Connector ConnectorTwo = null;

                        tx.Start();
                        double l_angle = Convert.ToDouble(MainWindow.Instance.angleDegree) * (Math.PI / 180);
                        double givendist = 0;
                        for (int i = 0; i < PrimaryElements.Count; i++)
                        {
                            List<XYZ> ConnectorPoints = new List<XYZ>();
                            PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                            SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                            Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out ConnectorOne, out ConnectorTwo);
                            foreach (Connector con in PrimaryConnectors)
                            {
                                ConnectorPoints.Add(con.Origin);
                            }
                            Element el = PrimaryElements[0];
                            LocationCurve findDirect = el.Location as LocationCurve;
                            Line ncDer = findDirect.Curve as Line;
                            XYZ dir = ncDer.Direction;
                            XYZ newenpt = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z);
                            XYZ newenpt2 = new XYZ(ConnectorOne.Origin.X, ConnectorOne.Origin.Y, ConnectorTwo.Origin.Z);

                            Conduit newConCopy = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, ConnectorOne.Origin, newenpt);
                            Conduit newCon2Copy = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, ConnectorTwo.Origin, newenpt2);
                            string offsetVariable = RevitVersion < 2020 ? "Offset" : "Middle Elevation";
                            Parameter parameter = newConCopy.LookupParameter(offsetVariable);
                            var middle = parameter.AsDouble();
                            XYZ Pri_mid = Utility.GetMidPoint(newConCopy);
                            XYZ Sec_mid = Utility.GetMidPoint(newCon2Copy);

                            double distance = 0;
                            DistanceElements = Utility.ConduitInOrder(DistanceElements);
                            LocationCurve newcurve = DistanceElements[0].Location as LocationCurve;
                            Line ncl = newcurve.Curve as Line;
                            XYZ direc = ncl.Direction;
                            if (DistanceElements.Count() >= 2)
                            {
                                LocationCurve newcurve2 = DistanceElements[1].Location as LocationCurve;
                                XYZ start1 = Utility.GetMidPoint(DistanceElements[0]);
                                Line cross = Utility.CrossProductLine(ncl, start1, 5, true);
                                start1 = new XYZ(start1.X, start1.Y, 0);
                                XYZ start2 = Utility.FindIntersection(DistanceElements[1], cross);
                                distance = start1.DistanceTo(start2);
                            }
                            Conduit newCon = null;
                            Conduit newCon2 = null;
                            var l = Utility.GetLineFromConduit(newConCopy);
                            MPtAngle = Utility.GetMidPoint(l);

                            if (IsHorizontal(ncDer))
                            {
                                if (pickline.Origin.Y < ax.Y)
                                {
                                    if (i == 0)
                                    {
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X + 3, direc.Y, direc.Z));
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X - 3, direc.Y, direc.Z));
                                    }
                                    else
                                    {
                                        givendist += distance;
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X, pickpoint.Y - givendist, pickpoint.Z), pickpoint + new XYZ(direc.X + 3, direc.Y - givendist, direc.Z));
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X, pickpoint.Y - givendist, pickpoint.Z), pickpoint + new XYZ(direc.X - 3, direc.Y - givendist, direc.Z));
                                    }
                                }
                                else
                                {
                                    if (i == 0)
                                    {
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X + 3, direc.Y, direc.Z));
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X - 3, direc.Y, direc.Z));

                                    }
                                    else
                                    {
                                        givendist += distance;
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X, pickpoint.Y + givendist, pickpoint.Z), pickpoint + new XYZ(direc.X + 3, direc.Y + givendist, direc.Z));
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X, pickpoint.Y + givendist, pickpoint.Z), pickpoint + new XYZ(direc.X - 3, direc.Y + givendist, direc.Z));
                                    }
                                }
                            }
                            else if (IsVertical(ncDer)) //vertical
                            {
                                if (pickline.Origin.X < ax.X)
                                {
                                    if (i == 0)
                                    {
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + direc.Multiply(.5));
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, pickpoint + direc.Multiply(-.5));
                                    }
                                    else
                                    {

                                        givendist += distance;
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X - givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X - givendist, direc.Y - .5, direc.Z));////
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X - givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X - givendist, direc.Y - .5, direc.Z));
                                    }
                                }
                                else //right
                                {
                                    if (i == 0)
                                    {
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + direc.Multiply(.5));
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, pickpoint + direc.Multiply(-.5));
                                    }
                                    else
                                    {

                                        givendist += distance;
                                        newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y + .5, direc.Z));
                                        newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y - .5, direc.Z));
                                    }
                                }
                            }
                            else //angled
                            {
                                if (dir.X > 0)
                                {
                                    if (pickline.Origin.X < MPtAngle.X) //left
                                    {
                                        if (i == 0)
                                        {
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X, direc.Y, direc.Z));
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X, direc.Y, direc.Z));
                                        }
                                        else
                                        {

                                            givendist += distance;
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));
                                        }
                                    }
                                    else //right
                                    {
                                        if (i == 0)
                                        {
                                            XYZ end = pickpoint + new XYZ(direc.X, direc.Y, direc.Z);
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X, direc.Y, direc.Z));
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, end);
                                        }
                                        else
                                        {

                                            givendist += distance;
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));////
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));
                                        }
                                    }
                                }
                                else
                                {
                                    if (pickline.Origin.X < MPtAngle.X) //left
                                    {
                                        if (i == 0)
                                        {
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X, direc.Y, direc.Z));
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X, direc.Y, direc.Z));
                                        }
                                        else
                                        {

                                            givendist += distance;
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));
                                        }
                                    }
                                    else //right
                                    {
                                        if (i == 0)
                                        {
                                            XYZ end = pickpoint + new XYZ(direc.X, direc.Y, direc.Z);
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, pickpoint, pickpoint + new XYZ(direc.X, direc.Y, direc.Z));
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, pickpoint, end);
                                        }
                                        else
                                        {

                                            givendist += distance;
                                            newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));////
                                            newCon2 = Utility.CreateConduit(doc, SecondaryElements[i] as Conduit, new XYZ(pickpoint.X + givendist, pickpoint.Y, pickpoint.Z), pickpoint + new XYZ(direc.X + givendist, direc.Y, direc.Z));
                                        }
                                    }
                                }
                            }


                            Parameter param = newCon.LookupParameter("Middle Elevation");
                            Parameter param2 = newCon2.LookupParameter("Middle Elevation");

                            param.Set(middle);
                            param2.Set(middle);

                            Utility.DeleteElement(doc, newConCopy.Id);
                            Utility.DeleteElement(doc, newCon2Copy.Id);

                            if (Utility.IsXYTrue(ConnectorPoints.FirstOrDefault(), ConnectorPoints.LastOrDefault()))
                            {
                                isVerticalConduits = true;
                            }
                            Element e = doc.GetElement(newCon.Id);
                            Element e2 = doc.GetElement(newCon2.Id);
                            thirdElements.Add(e);
                            forthElements.Add(e2);
                            //RetainParameters(PrimaryElements[i], SecondaryElements[i]);
                            //RetainParameters(PrimaryElements[i], e);
                        }
                        //Rotate Elements at Once
                        Element ElementOne = PrimaryElements[0];
                        Element ElementTwo = SecondaryElements[0];
                        Utility.GetClosestConnectors(ElementOne, ElementTwo, out ConnectorOne, out ConnectorTwo);
                        LocationCurve findDirection = ElementOne.Location as LocationCurve;
                        Line nc = findDirection.Curve as Line;
                        Curve refcurve = findDirection.Curve;
                        XYZ direct = nc.Direction;
                        LocationCurve findDirection2 = ElementTwo.Location as LocationCurve;
                        Line nc2 = findDirection2.Curve as Line;
                        XYZ directDown = nc2.Direction;
                        //primary
                        LocationCurve newconcurve = thirdElements[0].Location as LocationCurve;
                        Line ncl1 = newconcurve.Curve as Line;
                        XYZ direction = ncl1.Direction;
                        XYZ axisStart = null;
                        axisStart = pickpoint;
                        XYZ axisSt = ConnectorOne.Origin;
                        XYZ axisEnd = axisStart.Add(XYZ.BasisZ.CrossProduct(direction));
                        Line axisLine = Line.CreateBound(axisStart, axisEnd);
                        //secondary
                        LocationCurve newconcurve2 = forthElements[0].Location as LocationCurve;
                        Line ncl2 = newconcurve2.Curve as Line;
                        XYZ direction2 = ncl2.Direction;
                        XYZ axisStart2 = null;
                        axisStart2 = pickpoint;
                        XYZ axisEnd2 = axisStart2.Add(XYZ.BasisZ.CrossProduct(direction2));
                        Line axisLine2 = Line.CreateBound(axisStart2, axisEnd2);
                        Line pickedline = null;

                        pickedline = Line.CreateBound(pickpoint, pickpoint + new XYZ(direction.X + 10, direction.Y, direction.Z));
                        Curve cu = pickedline as Curve;

                        double PrimaryOffset = RevitVersion < 2020 ? PrimaryElements[0].LookupParameter("Offset").AsDouble() :
                                                 PrimaryElements[0].LookupParameter("Middle Elevation").AsDouble();
                        double SecondaryOffset = RevitVersion < 2020 ? SecondaryElements[0].LookupParameter("Offset").AsDouble() :
                                                  SecondaryElements[0].LookupParameter("Middle Elevation").AsDouble();
                        if (isVerticalConduits)
                        {
                            l_angle = (Math.PI / 2) - l_angle;
                        }
                        if (PrimaryOffset > SecondaryOffset)
                        {
                            //rotate down
                            l_angle = -l_angle;
                        }
                        try
                        {
                            if (IsHorizontal(nc))
                            {
                                if (pickedline.Origin.Y < axisSt.Y)
                                {
                                    XYZ end = new XYZ(axisStart.X, axisStart.Y, axisStart.Z + 10);
                                    Line l1 = Line.CreateBound(axisStart, end);
                                    ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), l1, l_angle);
                                }
                                else
                                {
                                    XYZ end = new XYZ(axisStart.X, axisStart.Y, axisStart.Z - 10);
                                    Line l1 = Line.CreateBound(axisStart, end);
                                    ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), l1, l_angle);
                                }
                            }
                            else if (IsVertical(nc))
                            {
                                if (pickedline.Origin.X < axisSt.X)
                                {
                                    //left in vertical
                                    XYZ end = new XYZ(axisStart.X, axisStart.Y, axisStart.Z - 10);
                                    Line l1 = Line.CreateBound(axisStart, end);
                                    ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), l1, -l_angle);
                                }
                                else
                                {
                                    //right in vertical
                                    XYZ end = new XYZ(axisStart.X, axisStart.Y, axisStart.Z + 10);/////////////////////
                                    Line l1 = Line.CreateBound(axisStart, end);
                                    ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), l1, -l_angle);
                                }
                            }
                            else //angle conduit
                            {
                                if (pickedline.Origin.X < axisSt.X) //left
                                {
                                    XYZ end = new XYZ(axisStart.X, axisStart.Y, axisStart.Z - 10);
                                    Line l1 = Line.CreateBound(axisStart, end);
                                    ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), l1, -l_angle);
                                }
                                else //right
                                {
                                    XYZ end = new XYZ(axisStart.X, axisStart.Y, axisStart.Z + 10);
                                    Line l1 = Line.CreateBound(axisStart, end);
                                    ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), l1, -l_angle);
                                }
                            }
                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                Element firstElement = PrimaryElements[i];
                                Element secondElement = SecondaryElements[i];
                                Element thirdElement = thirdElements[i];
                                Element forthElement = forthElements[i];
                                Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                Utility.AutoRetainParameters(PrimaryElements[i], thirdElement, doc, uiapp);

                                if (IsVertical(refcurve))
                                {
                                    if (pickline.Origin.X < ax.X) //left
                                    {
                                        if (direct.Y == -1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        else if (direct.Y == 1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        else if (direct.Y == -1 && directDown.Y == -1)
                                        {
                                            //Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                            Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else if (direct.Y == 1 && directDown.Y == -1)
                                        {
                                            if (direct.X < 0 || directDown.X < 0)
                                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                    }
                                    else
                                    {
                                        if (direct.Y == -1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        else if (direct.Y == 1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        else if (direct.Y == -1 && directDown.Y == -1)
                                        {
                                            //Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                            Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else if (direct.Y == 1 && directDown.Y == -1)
                                        {
                                            if (direct.X < 0 || directDown.X < 0)
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                    }

                                }
                                else if (Math.Round(direct.X) == Math.Round(directDown.X) && Math.Round(direct.Y) == Math.Round(directDown.Y) && direct.X != 1 && direct.Y != 1 && direct.X != -1 && direct.Y != -1)
                                {
                                    if (direct.X > 0 && directDown.X > 0)
                                    {
                                        if (pickedline.Origin.X < MPtAngle.X) //left
                                        {
                                            //Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        }
                                        else //right
                                        {
                                            if (direct.Z == 0)
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        }
                                    }
                                    else
                                    {
                                        if (pickedline.Origin.X < MPtAngle.X) //left
                                        {
                                            Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else //right
                                        {
                                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                        }
                                    }
                                }
                                else //horizontal
                                {
                                    if (pickedline.Origin.Y > axisSt.Y)
                                    {
                                        if (direct.X == -1 && directDown.X == 1)
                                        {
                                            if (direct.Y < 0 || directDown.Y < 0)
                                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else if (direct.X == 1 && directDown.X == 1)
                                        {
                                            if (Math.Round(direct.Y) == 0)
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else if (direct.X == -1 && directDown.X == -1)
                                        {
                                            if (Math.Round(direct.Y, 2) == Math.Round(directDown.Y, 2))
                                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                    }
                                    else
                                    {
                                        if (direct.X == -1 && directDown.X == 1)
                                        {
                                            if (direct.Y < 0 || directDown.Y < 0)
                                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else if (direct.X == 1 && directDown.X == 1)
                                            Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        else if (direct.X == -1 && directDown.X == -1)
                                        {
                                            if (Math.Round(direct.Y, 2) == Math.Round(directDown.Y, 2))
                                                Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(secondElement, thirdElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                    }
                                }
                            }

                            if (IsHorizontal(nc))
                            {
                                if (pickedline.Origin.Y < axisSt.Y)
                                {
                                    XYZ end2 = new XYZ(axisStart2.X, axisStart2.Y, axisStart2.Z + 10);
                                    Line l2 = Line.CreateBound(axisStart2, end2);
                                    ElementTransformUtils.RotateElements(doc, forthElements.Select(r => r.Id).ToList(), l2, -l_angle);
                                }

                                else
                                {
                                    XYZ end = new XYZ(axisStart2.X, axisStart2.Y, axisStart2.Z - 10);
                                    Line l1 = Line.CreateBound(axisStart2, end);
                                    ElementTransformUtils.RotateElements(doc, forthElements.Select(r => r.Id).ToList(), l1, -l_angle);
                                }
                            }
                            else if (IsVertical(nc))
                            {
                                if (pickedline.Origin.X < axisSt.X)
                                {
                                    //left in vertical
                                    XYZ end2 = new XYZ(axisStart2.X, axisStart2.Y, axisStart2.Z - 10);
                                    Line l2 = Line.CreateBound(axisStart2, end2);
                                    ElementTransformUtils.RotateElements(doc, forthElements.Select(r => r.Id).ToList(), l2, l_angle);
                                }

                                else
                                {
                                    //right in vertical
                                    XYZ end = new XYZ(axisStart2.X, axisStart2.Y, axisStart2.Z + 10);/////////////
                                    Line l1 = Line.CreateBound(axisStart2, end);
                                    ElementTransformUtils.RotateElements(doc, forthElements.Select(r => r.Id).ToList(), l1, l_angle);
                                }
                            }
                            else
                            {
                                if (pickedline.Origin.X < axisSt.X) //left
                                {
                                    XYZ end = new XYZ(axisStart2.X, axisStart2.Y, axisStart2.Z - 10);
                                    Line l1 = Line.CreateBound(axisStart2, end);
                                    ElementTransformUtils.RotateElements(doc, forthElements.Select(r => r.Id).ToList(), l1, l_angle);
                                }
                                else //right
                                {

                                    XYZ end2 = new XYZ(axisStart2.X, axisStart2.Y, axisStart2.Z - 10);
                                    Line l2 = Line.CreateBound(axisStart2, end2);
                                    ElementTransformUtils.RotateElements(doc, forthElements.Select(r => r.Id).ToList(), l2, -l_angle);
                                }
                            }



                            for (int i = 0; i < SecondaryElements.Count; i++)
                            {
                                Element firstElement = PrimaryElements[i];
                                Element secondElement = SecondaryElements[i];
                                Element thirdElement = thirdElements[i];
                                Element forthElement = forthElements[i];
                                Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                Utility.AutoRetainParameters(PrimaryElements[i], thirdElement, doc, uiapp);

                                if (IsVertical(refcurve))
                                {
                                    if (pickline.Origin.X < ax.X) //left
                                    {
                                        if (direct.Y == -1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        else if (direct.Y == 1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        else if (direct.Y == -1 && directDown.Y == -1)
                                        {
                                            //Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                            Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else if (direct.Y == 1 && directDown.Y == -1)
                                        {
                                            if (direct.X < 0 || directDown.X < 0)
                                                Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                    }
                                    else
                                    {
                                        if (direct.Y == -1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        else if (direct.Y == 1 && directDown.Y == 1)
                                            Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        else if (direct.Y == -1 && directDown.Y == -1)
                                        {
                                            //Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                            Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else if (direct.Y == 1 && directDown.Y == -1)
                                        {
                                            if (direct.X < 0 || directDown.X < 0)
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                    }

                                }
                                else if (Math.Round(direct.X) == Math.Round(directDown.X) && Math.Round(direct.Y) == Math.Round(directDown.Y) && direct.X != 1 && direct.Y != 1 && direct.X != -1 && direct.Y != -1)
                                {
                                    if (direct.X > 0 && directDown.X > 0)
                                    {
                                        if (pickedline.Origin.X < MPtAngle.X) //left
                                        {
                                            //Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                            Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        }
                                        else //right
                                        {
                                            if (direct.Z == 0)
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        }
                                    }
                                    else
                                    {
                                        if (pickedline.Origin.X < MPtAngle.X) //left
                                        {
                                            Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else //right
                                        {
                                            Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                        }
                                    }
                                }
                                else
                                {
                                    if (pickedline.Origin.Y > axisSt.Y)
                                    {
                                        if (direct.X == -1 && directDown.X == 1)
                                        {
                                            if (direct.Y < 0 || directDown.Y < 0)
                                                Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else if (direct.X == 1 && directDown.X == 1)
                                        {
                                            if (Math.Round(direct.Y) == 0)
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else if (direct.X == -1 && directDown.X == -1)
                                        {
                                            if (Math.Round(direct.Y, 2) == Math.Round(directDown.Y, 2))
                                                Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                    }
                                    else
                                    {

                                        if (direct.X == -1 && directDown.X == 1)
                                        {
                                            if (direct.Y < 0 || directDown.Y < 0)
                                                Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else if (direct.X == 1 && directDown.X == 1)
                                            Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        else if (direct.X == -1 && directDown.X == -1)
                                        {
                                            if (Math.Round(direct.Y, 2) == Math.Round(directDown.Y, 2))
                                                Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                            else
                                                Utility.CreateElbowFittings(firstElement, forthElement, doc, uiapp);
                                        }
                                        else
                                            Utility.CreateElbowFittings(SecondaryElements[i], forthElement, doc, uiapp);
                                    }
                                }
                            }
                            for (int i = 0; i < thirdElements.Count; i++)
                            {
                                try
                                {
                                    Utility.CreateElbowFittings(thirdElements[i], forthElements[i], doc, uiapp);
                                }
                                catch (Exception)
                                {
                                    try
                                    {
                                        Utility.CreateElbowFittings(forthElements[i], thirdElements[i], doc, uiapp);
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show(ex.Message);
                                        return;
                                    }
                                }
                            }


                        }
                        catch (Exception)
                        {

                            ElementTransformUtils.RotateElements(doc, thirdElements.Select(r => r.Id).ToList(), axisLine, l_angle * 2 + Math.PI);

                            for (int i = 0; i < PrimaryElements.Count; i++)
                            {
                                Element firstElement = PrimaryElements[i];
                                Element secondElement = SecondaryElements[i];
                                Element thirdElement = thirdElements[i];
                                Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                                Utility.AutoRetainParameters(PrimaryElements[i], thirdElement, doc, uiapp);
                                //Utility.CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp);
                                //Utility.CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp);
                                try
                                {
                                    _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));
                                    _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                }
                                catch
                                {
                                    try
                                    {

                                        _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                        _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));
                                    }
                                    catch
                                    {

                                        try
                                        {

                                            _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));
                                            _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                        }
                                        catch
                                        {
                                            try
                                            {

                                                _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                                _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));

                                            }
                                            catch
                                            {
                                                try
                                                {

                                                    _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));
                                                    _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));

                                                }
                                                catch
                                                {
                                                    try
                                                    {

                                                        _deleteElements.Add(CreateElbowFittings(SecondaryElements[i], thirdElement, doc, uiapp));
                                                        _deleteElements.Add(CreateElbowFittings(PrimaryElements[i], thirdElement, doc, uiapp));
                                                    }
                                                    catch
                                                    {
                                                        try
                                                        {

                                                            _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                                            _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                                        }
                                                        catch
                                                        {
                                                            try
                                                            {
                                                                _deleteElements.Add(CreateElbowFittings(thirdElement, PrimaryElements[i], doc, uiapp));
                                                                _deleteElements.Add(CreateElbowFittings(thirdElement, SecondaryElements[i], doc, uiapp));
                                                            }
                                                            catch
                                                            {

                                                                string message = string.Format("Make sure conduits are having less overlap, if not please reduce the overlapping distance.");
                                                                System.Windows.MessageBox.Show("Warning. \n" + message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                                                                return;
                                                            }


                                                        }


                                                    }



                                                }
                                            }

                                        }
                                    }


                                }

                            }
                        }

                        doc.Delete(_deleteElements.Select(x => x.Id).ToList());
                        tx.Commit();
                        doc.Regenerate();
                        successful = true;
                        // _ = Utility.UserActivityLog(System.Reflection.Assembly.GetExecutingAssembly(), uiapp, Util.ApplicationWindowTitle, startDate, "Completed", "Vertical Offset", Util.ProductVersion, "Connect");

                    }
                    using (SubTransaction tx = new SubTransaction(doc))
                    {
                        tx.Start();
                        Utility.ApplySync(PrimaryElements, uiapp);
                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    string message = string.Format("Make sure conduits are aligned to each other properly, if not please align primary conduit to secondary conduit. Error :{0}", ex.Message);
                    System.Windows.MessageBox.Show("Warning. \n" + message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                    // _ = Utility.UserActivityLog(System.Reflection.Assembly.GetExecutingAssembly(), uiapp, Util.ApplicationWindowTitle, startDate, "Failed", "Vertical Offset", Util.ProductVersion, "Connect");
                    successful = false;
                }
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show("Warning. \n" + exception.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                successful = false;
                // _ = Utility.UserActivityLog(System.Reflection.Assembly.GetExecutingAssembly(), uiapp, Util.ApplicationWindowTitle, startDate, "Failed", "Vertical Offset", Util.ProductVersion, "Connect");
            }
        }
        #endregion
        #region funct
        private List<ElementId> RollUp(Document doc, UIDocument uidoc, List<Element> PrimaryElements, List<Element> SecondaryElements, double l_angle, string l_direction, string offsetVariable, UIApplication uiapp)
        {
            List<ElementId> unwantedIds = new List<ElementId>();
            Dictionary<double, List<Element>> groupedFirstElements = new Dictionary<double, List<Element>>();
            Dictionary<double, List<Element>> groupedSecondElements = new Dictionary<double, List<Element>>();
            Utility.GroupByElevation(PrimaryElements, offsetVariable, ref groupedFirstElements);
            Utility.GroupByElevation(SecondaryElements, offsetVariable, ref groupedSecondElements);

            int j = 0;
            foreach (KeyValuePair<double, List<Element>> valuePair in groupedFirstElements)
            {
                PrimaryElements = valuePair.Value.ToList();
                SecondaryElements = groupedSecondElements.Values.ElementAt(j).ToList();
                double zSpace = groupedFirstElements.FirstOrDefault().Key - valuePair.Key;
                Line refLine = (PrimaryElements[0].Location as LocationCurve).Curve as Line;
                XYZ refDirection = refLine.Direction;
                XYZ refCross = refDirection.CrossProduct(XYZ.BasisZ);
                Line perdicularLine = Line.CreateBound(refLine.Origin, refLine.Origin + refCross.Multiply(10));
                for (int i = 0; i < PrimaryElements.Count; i++)
                {
                    double elevationOne = PrimaryElements[i].LookupParameter(offsetVariable).AsDouble();
                    double elevationTwo = SecondaryElements[i].LookupParameter(offsetVariable).AsDouble();
                    LocationCurve lc1 = PrimaryElements[i].Location as LocationCurve;
                    Line lineOne = lc1.Curve as Line;
                    LocationCurve lc2 = SecondaryElements[i].Location as LocationCurve;
                    Line lineTwo = lc2.Curve as Line;
                    XYZ sectionPoint = Utility.FindIntersectionPoint(lineOne.GetEndPoint(0), lineOne.GetEndPoint(1), perdicularLine.GetEndPoint(0), perdicularLine.GetEndPoint(1));
                    XYZ OriginWithOutZAxis = new XYZ(refLine.Origin.X, refLine.Origin.Y, 0);
                    double space = OriginWithOutZAxis.DistanceTo(sectionPoint);
                    double l_Angle = l_angle * Math.PI / 180;
                    space = Math.Tan(l_Angle / 2.5) * space;
                    zSpace = Math.Tan(l_Angle / 2) * zSpace;
                    ConnectorSet PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                    ConnectorSet SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                    Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                    XYZ primaryLineDirection = lineTwo.Direction;
                    XYZ cross = primaryLineDirection.CrossProduct(XYZ.BasisZ);
                    Line lineThree = Line.CreateBound(ConnectorTwo.Origin, ConnectorTwo.Origin + cross.Multiply(ConnectorOne.Origin.DistanceTo(ConnectorTwo.Origin)));
                    XYZ interSecPoint = Utility.FindIntersectionPoint(lineOne.GetEndPoint(0), lineOne.GetEndPoint(1), lineThree.GetEndPoint(0), lineThree.GetEndPoint(1));

                    XYZ newenpt = new XYZ(interSecPoint.X, interSecPoint.Y, ConnectorOne.Origin.Z);
                    Line newLine = Line.CreateBound(ConnectorOne.Origin, newenpt);

                    XYZ newStartPoint = (l_direction.Contains("Left-Down") || l_direction.Contains("Right-Down") || l_direction.Contains("Top-Right") || l_direction.Contains("Bottom-Right")) ?
                        newLine.Origin - (newLine.Direction * (space + zSpace)) : newLine.Origin + (newLine.Direction * (space + zSpace));

                    Conduit newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, newStartPoint, newenpt);
                    newCon.LookupParameter(offsetVariable).Set(elevationOne);
                    XYZ direction = ((newCon.Location as LocationCurve).Curve as Line).Direction;
                    LocationCurve curve = newCon.Location as LocationCurve;
                    Curve line = curve.Curve;

                    //RetainParameters(PrimaryElements[i], SecondaryElements[i], doc);
                    //RetainParameters(PrimaryElements[i], newCon as Element, doc);

                    try
                    {
                        if (curve != null)
                        {
                            XYZ aa = newStartPoint;
                            XYZ cc = new XYZ(aa.X, aa.Y, aa.Z + 10);
                            Line axisLine = Line.CreateBound(aa, cc);
                            double l_offSet = elevationOne < elevationTwo ? (elevationTwo - elevationOne) : (elevationOne - elevationTwo);
                            XYZ EndPointwithoutZ = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, 0);
                            double l_rollOffset = EndPointwithoutZ.DistanceTo(interSecPoint);
                            double rollAngle = Math.Atan2(l_offSet, l_rollOffset);
                            if (l_direction.Contains("Left-Up") || l_direction.Contains("Right-Down")
                                || l_direction.Contains("Top-Right")
                                || l_direction.Contains("Bottom-Left"))
                            {
                                curve.Rotate(axisLine, -l_angle * (Math.PI / 180));
                                curve.Rotate(newLine, -rollAngle);
                            }
                            else
                            {
                                curve.Rotate(axisLine, l_angle * (Math.PI / 180));
                                curve.Rotate(newLine, rollAngle);
                            }
                        }
                        Element e = doc.GetElement(newCon.Id);
                        ConnectorSet ThirdConnectors = Utility.GetConnectors(e);
                        Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                        Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                        unusedfittings.Add(Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp));
                        unusedfittings.Add(Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp));
                    }
                    catch
                    {
                        try
                        {

                            Element e = doc.GetElement(newCon.Id);
                            XYZ aa = (newStartPoint + newenpt) / 2;
                            XYZ cc = new XYZ(aa.X, aa.Y, aa.Z + 10);
                            Line axisLine = Line.CreateBound(aa, cc);
                            ElementTransformUtils.RotateElement(doc, e.Id, axisLine, Math.PI);
                            ConnectorSet ThirdConnectors = Utility.GetConnectors(e);
                            Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                            Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                            try
                            {
                                unusedfittings.Add(Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp));
                                unusedfittings.Add(Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp));
                            }
                            catch
                            {
                                unusedfittings.Add(Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp));
                                unusedfittings.Add(Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp));


                            }


                        }
                        catch
                        {
                            unwantedIds.Add(newCon.Id);
                        }



                    }

                }
                j++;
            }

            return unwantedIds;
        }
        private List<ElementId> RollDown(Document doc, UIDocument uidoc, List<Element> PrimaryElements, List<Element> SecondaryElements, double l_angle, string l_direction, string offsetVariable, UIApplication uiapp)
        {
            List<ElementId> unwantedIds = new List<ElementId>();
            Dictionary<double, List<Element>> groupedFirstElements = new Dictionary<double, List<Element>>();
            Dictionary<double, List<Element>> groupedSecondElements = new Dictionary<double, List<Element>>();
            Utility.GroupByElevation(PrimaryElements, offsetVariable, ref groupedFirstElements);
            Utility.GroupByElevation(SecondaryElements, offsetVariable, ref groupedSecondElements);


            int j = 0;
            foreach (KeyValuePair<double, List<Element>> valuePair in groupedFirstElements)
            {
                PrimaryElements = valuePair.Value.ToList();
                SecondaryElements = groupedSecondElements.Values.ElementAt(j).ToList();
                double zSpace = groupedFirstElements.FirstOrDefault().Key - valuePair.Key;
                Line refLine = (PrimaryElements[0].Location as LocationCurve).Curve as Line;
                XYZ refDirection = refLine.Direction;
                XYZ refCross = refDirection.CrossProduct(XYZ.BasisZ);
                Line perdicularLine = Line.CreateBound(refLine.Origin, (refLine.Origin + refCross.Multiply(10)));
                for (int i = 0; i < PrimaryElements.Count; i++)
                {
                    double elevationOne = PrimaryElements[i].LookupParameter(offsetVariable).AsDouble();
                    double elevationTwo = SecondaryElements[i].LookupParameter(offsetVariable).AsDouble();
                    LocationCurve lc1 = PrimaryElements[i].Location as LocationCurve;
                    Line lineOne = lc1.Curve as Line;
                    LocationCurve lc2 = SecondaryElements[i].Location as LocationCurve;
                    Line lineTwo = lc2.Curve as Line;
                    XYZ sectionPoint = Utility.FindIntersectionPoint(lineOne.GetEndPoint(0), lineOne.GetEndPoint(1), perdicularLine.GetEndPoint(0), perdicularLine.GetEndPoint(1));
                    XYZ OriginWithOutZAxis = new XYZ(refLine.Origin.X, refLine.Origin.Y, 0);
                    double space = OriginWithOutZAxis.DistanceTo(sectionPoint);
                    double l_Angle = l_angle * Math.PI / 180;
                    space = Math.Tan(l_Angle / 2.5) * space;
                    zSpace = Math.Tan(l_Angle / 2) * zSpace;
                    ConnectorSet PrimaryConnectors = Utility.GetConnectors(PrimaryElements[i]);
                    ConnectorSet SecondaryConnectors = Utility.GetConnectors(SecondaryElements[i]);
                    Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                    XYZ primaryLineDirection = lineTwo.Direction;
                    XYZ cross = primaryLineDirection.CrossProduct(XYZ.BasisZ);
                    Line lineThree = Line.CreateBound(ConnectorTwo.Origin, ConnectorTwo.Origin + cross.Multiply(ConnectorOne.Origin.DistanceTo(ConnectorTwo.Origin)));
                    XYZ interSecPoint = Utility.FindIntersectionPoint(lineOne.GetEndPoint(0), lineOne.GetEndPoint(1), lineThree.GetEndPoint(0), lineThree.GetEndPoint(1));

                    XYZ newenpt = new XYZ(interSecPoint.X, interSecPoint.Y, ConnectorOne.Origin.Z);
                    Line newLine = Line.CreateBound(ConnectorOne.Origin, newenpt);

                    XYZ newStartPoint = (l_direction.Contains("Left-Down") || l_direction.Contains("Right-Down") || l_direction.Contains("Top-Right") || l_direction.Contains("Bottom-Right")) ?
                        newLine.Origin - (newLine.Direction * (space + zSpace)) : newLine.Origin + (newLine.Direction * (space + zSpace));

                    Conduit newCon = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, newStartPoint, newenpt);
                    newCon.LookupParameter(offsetVariable).Set(elevationOne);
                    XYZ direction = ((newCon.Location as LocationCurve).Curve as Line).Direction;
                    LocationCurve curve = newCon.Location as LocationCurve;
                    Curve line = curve.Curve;

                    //RetainParameters(PrimaryElements[i], SecondaryElements[i], doc);
                    //RetainParameters(PrimaryElements[i], newCon as Element, doc);

                    try
                    {
                        if (curve != null)
                        {
                            XYZ aa = newStartPoint;
                            XYZ cc = new XYZ(aa.X, aa.Y, aa.Z + 10);
                            Line axisLine = Line.CreateBound(aa, cc);
                            double l_offSet = elevationOne < elevationTwo ? (elevationTwo - elevationOne) : (elevationOne - elevationTwo);
                            XYZ EndPointwithoutZ = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, 0);
                            double l_rollOffset = EndPointwithoutZ.DistanceTo(interSecPoint);
                            double rollAngle = Math.Atan2(l_offSet, l_rollOffset);
                            if (l_direction.Contains("Left-Up") || l_direction.Contains("Right-Down") || l_direction.Contains("Top-Right")
                                || l_direction.Contains("Bottom-Left"))
                            {
                                curve.Rotate(axisLine, -l_angle * (Math.PI / 180));
                                curve.Rotate(newLine, rollAngle);
                            }
                            else
                            {
                                curve.Rotate(axisLine, l_angle * (Math.PI / 180));
                                curve.Rotate(newLine, -rollAngle);
                            }
                        }
                        Element e = doc.GetElement(newCon.Id);
                        ConnectorSet ThirdConnectors = Utility.GetConnectors(e);
                        Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                        Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                        unusedfittings.Add(Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp));
                        unusedfittings.Add(Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp));
                    }
                    catch
                    {
                        try
                        {

                            Element e = doc.GetElement(newCon.Id);
                            XYZ aa = (newStartPoint + newenpt) / 2;
                            XYZ cc = new XYZ(aa.X, aa.Y, aa.Z + 10);
                            Line axisLine = Line.CreateBound(aa, cc);
                            ConnectorSet ThirdConnectors = Utility.GetConnectors(e);
                            ElementTransformUtils.RotateElement(doc, e.Id, axisLine, Math.PI);
                            Utility.AutoRetainParameters(PrimaryElements[i], SecondaryElements[i], uidoc, uiapp);
                            Utility.AutoRetainParameters(PrimaryElements[i], e, doc, uiapp);
                            try
                            {
                                unusedfittings.Add(Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp));
                                unusedfittings.Add(Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp));
                            }
                            catch
                            {
                                unusedfittings.Add(Utility.CreateElbowFittings(PrimaryElements[i], e, doc, uiapp));
                                unusedfittings.Add(Utility.CreateElbowFittings(SecondaryElements[i], e, doc, uiapp));

                            }

                        }
                        catch
                        {

                            unwantedIds.Add(newCon.Id);
                        }

                    }

                }
                j++;




            }

            return unwantedIds;
        }
        public bool IsVertical(Curve curve)
        {
            // Implement your logic to check if the curve is vertical
            return Math.Abs(curve.GetEndPoint(0).X - curve.GetEndPoint(1).X) < 0.001;
        }

        public bool IsHorizontal(Curve curve)
        {
            // Implement your logic to check if the curve is horizontal
            return Math.Abs(curve.GetEndPoint(0).Y - curve.GetEndPoint(1).Y) < 0.001;
        }

        public bool IsAngled(Curve curve)
        {
            // Implement your logic to check if the curve is angled
            double angle = Math.Atan2(curve.GetEndPoint(1).Y - curve.GetEndPoint(0).Y, curve.GetEndPoint(1).X - curve.GetEndPoint(0).X);
            double angleInDegrees = angle * (180 / Math.PI);

            // Set a threshold for what you consider as "angled" (e.g., 10 degrees)
            return Math.Abs(angleInDegrees) > 10;
        }
        public static List<Element> conduit_order(Document doc, List<Element> conduits, Element grid)
        {
            List<double> distance_collection = new List<double>();
            List<Element> conduit_order = new List<Element>();
            Line grid_line = (grid.Location as LocationCurve).Curve as Line;
            XYZ grid_midpoint = (grid_line.GetEndPoint(0) + grid_line.GetEndPoint(1)) / 2;
            XYZ direction_grid = grid_line.Direction;
            XYZ cross = direction_grid.CrossProduct(XYZ.BasisZ);
            XYZ newpoint1 = grid_midpoint + cross.Multiply(1000);
            XYZ newpoint2 = grid_midpoint - cross.Multiply(1000);
            Line grid_perdicular_line = Line.CreateBound(newpoint1, newpoint2);

            foreach (Element cond in conduits)
            {
                LocationCurve locur = cond.Location as LocationCurve;
                Line conduit_line = locur.Curve as Line;
                XYZ intersectionpoint = Utility.FindIntersectionPoint(grid_perdicular_line.GetEndPoint(0), grid_perdicular_line.GetEndPoint(1), conduit_line.GetEndPoint(0), conduit_line.GetEndPoint(1));
                double distance = (Math.Pow(grid_midpoint.X - intersectionpoint.X, 2) + Math.Pow(grid_midpoint.Y - intersectionpoint.Y, 2));
                distance = Math.Abs(Math.Sqrt(distance));
                distance_collection.Add(distance);
            }
            distance_collection.Sort();
            foreach (double dou in distance_collection)
            {
                foreach (Element cond in conduits)
                {
                    LocationCurve locur = cond.Location as LocationCurve;
                    Line conduit_line = locur.Curve as Line;
                    XYZ intersectionpoint = Utility.FindIntersectionPoint(grid_perdicular_line.GetEndPoint(0), grid_perdicular_line.GetEndPoint(1), conduit_line.GetEndPoint(0), conduit_line.GetEndPoint(1));
                    double distance = (Math.Pow(grid_midpoint.X - intersectionpoint.X, 2) + Math.Pow(grid_midpoint.Y - intersectionpoint.Y, 2));
                    distance = Math.Abs(Math.Sqrt(distance));
                    if (distance == dou)
                    {
                        conduit_order.Add(cond);
                    }
                }
            }


            return conduit_order;
        }
        private static Line AlignElement(Element pickedElement, XYZ refPoint, Document doc)
        {
            Line NewLine = null;
            using (SubTransaction subTx = new SubTransaction(doc))
            {
                subTx.Start();
                Line firstLine = (pickedElement.Location as LocationCurve).Curve as Line;
                XYZ startPoint = firstLine.GetEndPoint(0);
                XYZ endPoint = firstLine.GetEndPoint(1);
                LocationCurve curve = pickedElement.Location as LocationCurve;
                XYZ normal = firstLine.Direction;
                XYZ cross = normal.CrossProduct(XYZ.BasisZ);
                XYZ newEndPoint = refPoint + cross.Multiply(5);
                Line boundLine = Line.CreateBound(refPoint, newEndPoint);
                XYZ interSecPoint = Utility.FindIntersectionPoint(firstLine.GetEndPoint(0), firstLine.GetEndPoint(1), boundLine.GetEndPoint(0), boundLine.GetEndPoint(1));
                interSecPoint = new XYZ(interSecPoint.X, interSecPoint.Y, startPoint.Z);
                ConnectorSet connectorSet = Utility.GetUnusedConnectors(pickedElement);
                if (connectorSet.Size == 2)
                {
                    if (startPoint.DistanceTo(interSecPoint) > endPoint.DistanceTo(interSecPoint))
                    {
                        NewLine = Line.CreateBound(startPoint, interSecPoint);
                    }
                    else
                    {
                        NewLine = Line.CreateBound(interSecPoint, endPoint);
                    }
                }
                else
                {
                    connectorSet = Utility.GetConnectors(pickedElement);
                    foreach (Connector con in connectorSet)
                    {
                        if (con.IsConnected)
                        {
                            if (Utility.IsXYZTrue(con.Origin, startPoint))
                            {
                                NewLine = Line.CreateBound(con.Origin, interSecPoint);
                                break;
                            }
                            if (Utility.IsXYZTrue(con.Origin, endPoint))
                            {
                                NewLine = Line.CreateBound(interSecPoint, con.Origin);
                                break;
                            }
                        }
                    }
                }
                subTx.Commit();
            }
            return NewLine;
        }

        private static List<Element> SortbyPlane(Document doc, List<Element> arrelements)
        {
            List<Element> conduitCollection = new List<Element>();

            //ascending conduits based on the intersection
            Dictionary<double, Conduit> dictcond = new Dictionary<double, Conduit>();
            View view = doc.ActiveView;
            XYZ vieworgin = view.Origin;
            XYZ viewdirection = view.ViewDirection;

            Line CondutitLine1 = (arrelements.First().Location as LocationCurve).Curve as Line;
            XYZ vieworgin1 = CondutitLine1.Origin;

            foreach (Conduit c in arrelements)
            {
                conduitCollection.Clear();
                Line CondutitLine = (c.Location as LocationCurve).Curve as Line;

                SketchPlane sp = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(CondutitLine1.Direction, vieworgin1));
                double denominator = CondutitLine.Direction.Normalize().DotProduct(sp.GetPlane().Normal);
                double numerator = (sp.GetPlane().Origin - CondutitLine.GetEndPoint(0)).DotProduct(sp.GetPlane().Normal);
                double parameter = numerator / denominator;
                XYZ intersectionPoint = CondutitLine.GetEndPoint(0) + parameter * CondutitLine.Direction;
                double xdirection = Math.Round(CondutitLine.Direction.X, 6);
                double ydirection = Math.Round(CondutitLine.Direction.Y, 6);
                double zdirection = Math.Round(CondutitLine.Direction.Z, 6);

                if (ydirection == -1 || ydirection == 1)
                {
                    dictcond.Add(intersectionPoint.X, c);
                }
                else
                {
                    dictcond.Add(intersectionPoint.Y, c);
                }


            }
            conduitCollection = dictcond.OrderBy(x => x.Key).Select(x => x.Value as Element).ToList();

            return conduitCollection;
        }

        public static XYZ MultiConnectFindIntersectionPoint(Line lineOne, Line lineTwo)
        {
            return MultiConnectFindIntersectionPoint(lineOne.GetEndPoint(0), lineOne.GetEndPoint(1), lineTwo.GetEndPoint(0), lineTwo.GetEndPoint(1));
        }

        public static XYZ MultiConnectFindIntersectionPoint(XYZ s1, XYZ e1, XYZ s2, XYZ e2)
        {
            s1 = Utility.XYZroundOf(s1, 5);
            e1 = Utility.XYZroundOf(e1, 5);
            s2 = Utility.XYZroundOf(s2, 5);
            e2 = Utility.XYZroundOf(e2, 5);

            double a1 = e1.Y - s1.Y;
            double b1 = s1.X - e1.X;
            double c1 = a1 * s1.X + b1 * s1.Y;

            double a2 = e2.Y - s2.Y;
            double b2 = s2.X - e2.X;
            double c2 = a2 * s2.X + b2 * s2.Y;

            double delta = a1 * b2 - a2 * b1;
            //If lines are parallel, the result will be (NaN, NaN).
            return delta == 0 || Convert.ToString(delta).Contains("E") == true ? null
                : new XYZ((b2 * c1 - b1 * c2) / delta, (a1 * c2 - a2 * c1) / delta, 0);
        }
        private bool ReverseingConduits(Document doc, ref List<Element> primaryElements, ref List<Element> secondaryElements)
        {
            Line priFirst = ((primaryElements.First().Location as LocationCurve).Curve as Line);
            Line prilast = ((primaryElements.Last().Location as LocationCurve).Curve as Line);
            Line secFirst = ((secondaryElements.First().Location as LocationCurve).Curve as Line);
            Line seclast = ((secondaryElements.Last().Location as LocationCurve).Curve as Line);

            XYZ firstinter = MultiConnectFindIntersectionPoint(priFirst, secFirst);
            XYZ lastinter = MultiConnectFindIntersectionPoint(prilast, seclast);
            if (firstinter == null || lastinter == null)
            {
                return false;
            }
            priFirst = AlignElement(primaryElements.First(), firstinter, doc);
            secFirst = AlignElement(secondaryElements.First(), firstinter, doc);
            prilast = AlignElement(primaryElements.Last(), lastinter, doc);
            seclast = AlignElement(secondaryElements.Last(), lastinter, doc);

            Line primFirstextentionline = Line.CreateBound(new XYZ(priFirst.GetEndPoint(0).X, priFirst.GetEndPoint(0).Y, 0), new XYZ(priFirst.GetEndPoint(1).X, priFirst.GetEndPoint(1).Y, 0));
            Line secoFirstnextentionline = Line.CreateBound(new XYZ(secFirst.GetEndPoint(0).X, secFirst.GetEndPoint(0).Y, 0), new XYZ(secFirst.GetEndPoint(1).X, secFirst.GetEndPoint(1).Y, 0));
            Line primLastextentionline = Line.CreateBound(new XYZ(prilast.GetEndPoint(0).X, prilast.GetEndPoint(0).Y, 0), new XYZ(prilast.GetEndPoint(1).X, prilast.GetEndPoint(1).Y, 0));
            Line secoLastnextentionline = Line.CreateBound(new XYZ(seclast.GetEndPoint(0).X, seclast.GetEndPoint(0).Y, 0), new XYZ(seclast.GetEndPoint(1).X, seclast.GetEndPoint(1).Y, 0));

            XYZ interpointset1 = Utility.GetIntersection(primFirstextentionline, secoLastnextentionline);
            XYZ interpointset2 = Utility.GetIntersection(secoFirstnextentionline, primLastextentionline);
            if (interpointset1 == null || interpointset2 == null)
            {
                secondaryElements.Reverse();
            }
            if (interpointset1 == null && interpointset2 == null)
            {
                primaryElements.Reverse();
            }
            return true;
        }

        public static Autodesk.Revit.DB.FamilyInstance CreateElbowFittings(ConnectorSet PrimaryConnectors, ConnectorSet SecondaryConnectors, Document Doc)
        {
            Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out var ConnectorOne, out var ConnectorTwo);
            return Doc.Create.NewElbowFitting(ConnectorOne, ConnectorTwo);
        }

        public static Autodesk.Revit.DB.FamilyInstance CreateElbowFittings(Autodesk.Revit.DB.Element One, Autodesk.Revit.DB.Element Two, Document doc, Autodesk.Revit.UI.UIApplication uiApp)
        {
            ConnectorSet connectorSet = GetConnectorSet(One);
            ConnectorSet connectorSet2 = GetConnectorSet(Two);
            Utility.AutoRetainParameters(One, Two, doc, uiApp);
            return CreateElbowFittings(connectorSet, connectorSet2, doc);
        }
        public static ConnectorSet GetConnectorSet(Autodesk.Revit.DB.Element Ele)
        {
            ConnectorSet result = null;
            if (Ele is Autodesk.Revit.DB.FamilyInstance)
            {
                MEPModel mEPModel = ((Autodesk.Revit.DB.FamilyInstance)Ele).MEPModel;
                if (mEPModel != null && mEPModel.ConnectorManager != null)
                {
                    result = mEPModel.ConnectorManager.UnusedConnectors;
                }
            }
            else if (Ele is MEPCurve)
            {
                result = ((MEPCurve)Ele).ConnectorManager.UnusedConnectors;
            }

            return result;
        }

        public static List<Element> GetElementsByOderDescending(List<Element> a_PrimaryElements)
        {
            List<Element> PrimaryElements = new List<Element>();
            XYZ PrimaryDirection = ((a_PrimaryElements.FirstOrDefault().Location as LocationCurve).Curve as Line).Direction;
            if (Math.Abs(PrimaryDirection.Z) != 1)
            {
                PrimaryElements = a_PrimaryElements.OrderByDescending(x => ((((x.Location as LocationCurve).Curve as Line).GetEndPoint(0) + ((x.Location as LocationCurve).Curve as Line).GetEndPoint(1)) / 2).Y).ToList();
                if (PrimaryDirection.Y == 1 || PrimaryDirection.Y == -1)
                {
                    PrimaryElements = a_PrimaryElements.OrderByDescending(x => ((((x.Location as LocationCurve).Curve as Line).GetEndPoint(0) + ((x.Location as LocationCurve).Curve as Line).GetEndPoint(1)) / 2).X).ToList();
                }
            }
            else
            {
                PrimaryElements = a_PrimaryElements.OrderByDescending(x => ((((x.Location as LocationCurve).Curve as Line).GetEndPoint(0) + ((x.Location as LocationCurve).Curve as Line).GetEndPoint(1)) / 2).X).ToList();
            }
            return PrimaryElements;
        }
        public static List<Element> GetElementsByOder(List<Element> a_PrimaryElements)
        {
            List<Element> PrimaryElements = new List<Element>();
            XYZ PrimaryDirection = ((a_PrimaryElements.LastOrDefault().Location as LocationCurve).Curve as Line).Direction;
            if (Math.Abs(PrimaryDirection.Z) != 1)
            {
                PrimaryElements = a_PrimaryElements.OrderBy(x => ((((x.Location as LocationCurve).Curve as Line).GetEndPoint(0) + ((x.Location as LocationCurve).Curve as Line).GetEndPoint(1)) / 2).Y).ToList();
                if (PrimaryDirection.Y == 1 || PrimaryDirection.Y == -1)
                {
                    PrimaryElements = a_PrimaryElements.OrderBy(x => ((((x.Location as LocationCurve).Curve as Line).GetEndPoint(0) + ((x.Location as LocationCurve).Curve as Line).GetEndPoint(1)) / 2).X).ToList();
                }
            }
            else
            {
                PrimaryElements = a_PrimaryElements.OrderBy(x => ((((x.Location as LocationCurve).Curve as Line).GetEndPoint(0) + ((x.Location as LocationCurve).Curve as Line).GetEndPoint(1)) / 2).X).ToList();
            }
            return PrimaryElements;
        }
        #endregion
        public string GetName()
        {
            return "Revit Addin";
        }
    }
}
