using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Office.Interop.Excel;
using Newtonsoft.Json;
using Revit.SDK.Samples.AutoUpdaterPro.CS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Line = Autodesk.Revit.DB.Line;

namespace AutoUpdaterPro
{
    [Transaction(TransactionMode.Manual)]
    public class AngleDrawHandler : IExternalEventHandler
    {
        DateTime startDate = DateTime.UtcNow;
        public List<FamilyInstance> unusedfittings = new List<FamilyInstance>();
        public List<Element> _deleteElements = new List<Element>();
        List<Element> SelectedElements = new List<Element>();
        List<Element> DistanceElements = new List<Element>();
        public bool successful;
        bool _isfirst;
        bool iswindowClose = false;
        public static XYZ _previousXYZ = null;
        Dictionary<int, List<Element>> _dictReorder = new Dictionary<int, List<Element>>();
        Dictionary<int, List<Element>> _dictReorderStub = new Dictionary<int, List<Element>>();
        public static Conduit otherConduit = null;
        public bool isStubCreate = true;
        bool isoffsetwindowClose = false;

        bool isfar;
        double angle = double.MaxValue;
        bool isCatch = false;


        List<Element> primarySortedElements = new List<Element>();
        List<Element> secondarySortedElements = new List<Element>();

        bool _AscendingElementwithPositiveAngle = false;
        bool _DescendingElementwithPositiveAngle = false;
        bool _AscendingElementwithNegativeAngle = false;
        bool _DescendingElementwithNegativeAngle = false;

        List<Element> OrderPrimary = new List<Element>();
        List<Element> OrderSecondary = new List<Element>();

        public bool iswhenReloadTool = false;
        public bool isOffsetTool = true;
        public int GroupPrimaryCount;

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
                    try
                    {
                        if (successful && delete != null)
                        {
                            _doc.Delete(delete);
                        }
                    }
                    catch
                    {

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
        public class XYZComparer : IComparer<XYZ>
        {
            public int Compare(XYZ a, XYZ b)
            {
                if (a.X != b.X)
                    return a.X.CompareTo(b.X);
                if (a.Y != b.Y)
                    return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            }
        }
        private static XYZ GetConnectorOrigin(Connector connector)
        {
            return connector?.CoordinateSystem?.Origin;
        }
        private static ConnectorSet GetConnectors(Conduit conduit)
        {
            return conduit?.ConnectorManager?.Connectors;
        }
        public static void GetClosestConnectorJoin(Conduit horizotalConduit, Conduit verticalConduit, out Connector connector1, out Connector connector2)
        {
            connector1 = null;
            connector2 = null;
            var connectors1 = GetConnectors(horizotalConduit);
            var connectors2 = GetConnectors(verticalConduit);
            double minDistance = double.MaxValue;
            foreach (Connector conn1 in connectors1)
            {
                foreach (Connector conn2 in connectors2)
                {
                    XYZ origin1 = GetConnectorOrigin(conn1);
                    XYZ origin2 = GetConnectorOrigin(conn2);
                    if (origin1 == null || origin2 == null)
                        continue;
                    double distance = origin1.DistanceTo(origin2);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        connector1 = conn1;
                        connector2 = conn2;
                    }
                }
            }
        }
        private static bool HasDuplicateXCoordinates(List<XYZ> points)
        {
            HashSet<double> uniqueXCoordinates = new HashSet<double>();

            foreach (var point in points)
            {
                if (!uniqueXCoordinates.Add(point.X))
                {
                    return true; // Duplicate X found
                }
            }
            return false; // No duplicates found
        }

        public List<Element> ArrangeConduits(Document doc, Conduit singleConduit, List<Element> groupConduits)
        {
            List<Element> sortedConduits = new List<Element>();
            sortedConduits.Add(singleConduit);
            if (singleConduit == null || groupConduits == null)
            {
                throw new ArgumentException("A valid single conduit and exactly three group conduits are required.");
            }
            XYZ singleConduitOrigin = Utility.GetXYvalue(Utility.GetLineFromConduit(singleConduit).Origin);
            Dictionary<Element, double> conduitDistances = new Dictionary<Element, double>();
            foreach (Conduit conduit in groupConduits)
            {
                XYZ groupConduitOrigin = Utility.GetXYvalue(Utility.GetLineFromConduit(conduit).Origin);
                double distance = singleConduitOrigin.DistanceTo(groupConduitOrigin);
                conduitDistances[conduit] = distance;
            }
            sortedConduits.AddRange(conduitDistances.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToList());
            return sortedConduits;
        }
        public void ApplyKick(Document doc, UIApplication uiApp, List<Element> PrimaryElements, List<Element> SecondaryElements, string offSetVar)
        {
            try
            {
                angle = Convert.ToDouble(MainWindow.Instance.angleDegree) * (Math.PI / 180);
                Element angledElement = null;
                XYZ Pickpoint = ((SecondaryElements[0].Location as LocationCurve).Curve as Line).Direction;
                XYZ origin = null;
                foreach (Element item in PrimaryElements)
                {
                    ConnectorSet PrimaryConnectors = Utility.GetUnusedConnectors(item);
                    if (PrimaryConnectors.Size == 1)
                    {
                        foreach (Connector con in PrimaryConnectors)
                        {
                            origin = con.Origin;
                            break;
                        }
                        break;
                    }
                }
                if (origin != null)
                {
                    Autodesk.Revit.DB.Line line = Utility.CrossProductLine(PrimaryElements[0], Pickpoint, 1, true);
                    line = Utility.CrossProductLine(line, Pickpoint, 1, true);
                    Autodesk.Revit.DB.Line line1 = Utility.CrossProductLine(PrimaryElements[0], Utility.GetXYvalue(origin), 1, true);
                    XYZ ip = Utility.FindIntersectionPoint(line, line1);
                    if (ip != null)
                        Pickpoint = ip;
                }
                List<ConnectorSet> css = new List<ConnectorSet>();
                List<Element> reversePrimaryElements = new List<Element>();
                if (GroupPrimaryCount == 1)
                {
                    css = new List<ConnectorSet>();
                    Dictionary<double, Element> lengthElementDict = new Dictionary<double, Element>();
                    Dictionary<double, Element> DUPlengthElementDict = new Dictionary<double, Element>();
                    Dictionary<double, Element> sortedLengthElementDict = new Dictionary<double, Element>();
                    Dictionary<double, Element> DUPsortedLengthElementDict = new Dictionary<double, Element>();
                    foreach (Element ele in PrimaryElements)
                    {
                        ConnectorSet child = Utility.GetConnectorSet(ele);
                        ConnectorSet parent = Utility.GetConnectorSet(SecondaryElements.FirstOrDefault());
                        Utility.GetClosestConnectors(parent, child, out Connector c1a, out Connector c2a);
                        Line l1 = Line.CreateBound(Utility.GetXYvalue(c1a.Origin), Utility.GetXYvalue(c2a.Origin));
                        double length = l1.Length;
                        if (!lengthElementDict.ContainsKey(length))
                        {
                            lengthElementDict.Add(length, ele);
                        }
                        else
                        {
                            DUPlengthElementDict.Add(length, ele);
                        }
                    }
                    sortedLengthElementDict = lengthElementDict.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    DUPsortedLengthElementDict = DUPlengthElementDict.OrderByDescending(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    foreach (var kvp in sortedLengthElementDict)
                    {
                        double sortedLength = kvp.Key;
                        Element sortedElement = kvp.Value;
                        ConnectorSet reversePrimaryConnectors = Utility.GetConnectorSet(sortedElement);
                        css.Add(reversePrimaryConnectors);
                    }
                    foreach (var kvp in DUPsortedLengthElementDict)
                    {
                        double sortedLength = kvp.Key;
                        Element sortedElement = kvp.Value;
                        ConnectorSet reversePrimaryConnectors = Utility.GetConnectorSet(sortedElement);
                        css.Add(reversePrimaryConnectors);
                    }
                    reversePrimaryElements = new List<Element>();
                    for (int a = PrimaryElements.Count - 1; a >= 0; a--)
                    {
                        reversePrimaryElements.Add(PrimaryElements[a]);
                    }
                }
                for (int i = 0; i < PrimaryElements.Count; i++)
                {
                    double elevation = SecondaryElements[i].LookupParameter(offSetVar).AsDouble();
                    ConnectorSet PrimaryConnectors = Utility.GetConnectorSet(PrimaryElements[i]);
                    ConnectorSet SecondaryConnectors = Utility.GetConnectorSet(SecondaryElements[i]);
                    Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                    Conduit FirstConduit = PrimaryElements[i] as Conduit;
                    Autodesk.Revit.DB.Line firstLine = (FirstConduit.Location as LocationCurve).Curve as Autodesk.Revit.DB.Line;
                    Autodesk.Revit.DB.Line secondLine = (SecondaryElements[i].Location as LocationCurve).Curve as Autodesk.Revit.DB.Line;
                    XYZ firstLineDirection = firstLine.Direction;
                    XYZ firstLineCross = firstLineDirection.CrossProduct(XYZ.BasisZ);
                    XYZ secondConduitFirstPt = ConnectorTwo.Origin;
                    XYZ secondConduitSecondPt = ConnectorTwo.Origin + firstLineCross.Multiply(1);
                    XYZ stPt = firstLine.GetEndPoint(0);
                    XYZ edPt = firstLine.GetEndPoint(1);
                    Conduit newConduit = null;
                    Line axisLine = null;
                    FamilyInstance f1 = null;

                    if (isfar)
                    {
                        try
                        {
                            Line horizontalLine = Line.CreateBound(ConnectorOne.Origin, (stPt + edPt) / 2);
                            XYZ thirdStartPoint = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z);
                            XYZ thirdEndPoind = thirdStartPoint + horizontalLine.Direction.Multiply(5);
                            newConduit = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, thirdStartPoint, thirdEndPoind);
                            axisLine = Line.CreateBound(thirdStartPoint, new XYZ(thirdStartPoint.X, thirdStartPoint.Y, thirdStartPoint.Z + 10));
                            if (isCatch)
                            {
                                ElementTransformUtils.RotateElement(doc, newConduit.Id, axisLine, -angle);
                            }
                            else
                            {
                                ElementTransformUtils.RotateElement(doc, newConduit.Id, axisLine, angle);
                            }
                            Element newElement = doc.GetElement(newConduit.Id);
                            ConnectorSet ThirdConnectors = Utility.GetConnectorSet(newElement);
                            Utility.RetainParameters(PrimaryElements[i], SecondaryElements[i], uiApp);
                            Utility.RetainParameters(PrimaryElements[i], newElement, uiApp);
                            f1 = Utility.CreateElbowFittings(SecondaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                            Utility.CreateElbowFittings(PrimaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                        }
                        catch
                        {
                            isCatch = true;
                            if (newConduit != null)
                                doc.Delete(newConduit.Id);
                            if (f1 != null)
                                doc.Delete(f1.Id);
                            Line horizontalLine = Line.CreateBound(ConnectorOne.Origin, (stPt + edPt) / 2);
                            XYZ thirdStartPoint = new XYZ(ConnectorTwo.Origin.X, ConnectorTwo.Origin.Y, ConnectorOne.Origin.Z);
                            XYZ thirdEndPoind = thirdStartPoint + horizontalLine.Direction.Multiply(5);
                            newConduit = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, thirdStartPoint, thirdEndPoind);
                            axisLine = Line.CreateBound(thirdStartPoint, new XYZ(thirdStartPoint.X, thirdStartPoint.Y, thirdStartPoint.Z + 10));
                            Element newElement = doc.GetElement(newConduit.Id);
                            ElementTransformUtils.RotateElement(doc, newConduit.Id, axisLine, -angle);
                            ConnectorSet ThirdConnectors = Utility.GetConnectorSet(newElement);
                            Utility.RetainParameters(PrimaryElements[i], SecondaryElements[i], uiApp);
                            Utility.RetainParameters(PrimaryElements[i], newElement, uiApp);
                            f1 = Utility.CreateElbowFittings(SecondaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                            Utility.CreateElbowFittings(PrimaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                        }
                    }
                    else
                    {
                        try
                        {
                            Line verticalLine = Line.CreateBound(new XYZ(ConnectorOne.Origin.X, ConnectorOne.Origin.Y, ConnectorTwo.Origin.Z),
                                       new XYZ(ConnectorOne.Origin.X, ConnectorOne.Origin.Y, ConnectorTwo.Origin.Z) + secondLine.Direction.CrossProduct(XYZ.BasisZ).Multiply(10));
                            XYZ IP = Utility.FindIntersection(SecondaryElements[i], verticalLine);
                            XYZ thirdStartPoint = new XYZ(IP.X, IP.Y, ConnectorOne.Origin.Z);
                            XYZ thirdEndPoind = new XYZ(IP.X, IP.Y, ConnectorTwo.Origin.Z);
                            newConduit = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, thirdStartPoint, thirdEndPoind);
                            axisLine = secondLine;
                            if (isCatch)
                            {
                                ElementTransformUtils.RotateElement(doc, newConduit.Id, axisLine, -angle);
                            }
                            else
                            {
                                ElementTransformUtils.RotateElement(doc, newConduit.Id, axisLine, angle);
                            }
                            Element newElement = doc.GetElement(newConduit.Id);
                            ConnectorSet ThirdConnectors = Utility.GetConnectorSet(newElement);
                            Utility.RetainParameters(PrimaryElements[i], SecondaryElements[i], uiApp);
                            Utility.RetainParameters(PrimaryElements[i], newElement, uiApp);
                            f1 = Utility.CreateElbowFittings(SecondaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                            Utility.CreateElbowFittings(PrimaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                        }
                        catch
                        {
                            isCatch = true;
                            if (newConduit != null)
                                doc.Delete(newConduit.Id);
                            if (f1 != null)
                                doc.Delete(f1.Id);
                            Line verticalLine = Line.CreateBound(new XYZ(ConnectorOne.Origin.X, ConnectorOne.Origin.Y, ConnectorTwo.Origin.Z),
                                       new XYZ(ConnectorOne.Origin.X, ConnectorOne.Origin.Y, ConnectorTwo.Origin.Z) + secondLine.Direction.CrossProduct(XYZ.BasisZ).Multiply(10));
                            XYZ IP = Utility.FindIntersection(SecondaryElements[i], verticalLine);
                            XYZ thirdStartPoint = new XYZ(IP.X, IP.Y, ConnectorOne.Origin.Z);
                            XYZ thirdEndPoind = new XYZ(IP.X, IP.Y, ConnectorTwo.Origin.Z);
                            newConduit = Utility.CreateConduit(doc, PrimaryElements[i] as Conduit, thirdStartPoint, thirdEndPoind);
                            axisLine = secondLine;
                            ElementTransformUtils.RotateElement(doc, newConduit.Id, axisLine, -angle);
                            Element newElement = doc.GetElement(newConduit.Id);
                            ConnectorSet ThirdConnectors = Utility.GetConnectorSet(newElement);
                            Utility.RetainParameters(PrimaryElements[i], SecondaryElements[i], uiApp);
                            Utility.RetainParameters(PrimaryElements[i], newElement, uiApp);
                            f1 = Utility.CreateElbowFittings(SecondaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                            Utility.CreateElbowFittings(PrimaryConnectors, ThirdConnectors, doc, uiApp, PrimaryElements[i], true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Warning. \n" + ex.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        public List<Element> FindCornerConduitsInclinedVerticalConduits(Dictionary<XYZ, Element> multilayerdPS, List<XYZ> xyzPS, Document doc, int verticalLayerCount, List<Element> previousListofElement)
        {
            List<Element> GroupedElement = new List<Element>();
            using (SubTransaction trans = new SubTransaction(doc))
            {
                trans.Start();
                double maxDistance = 0;
                XYZ firstCorner = null;
                XYZ secondCorner = null;
                for (int a = 0; a < xyzPS.Count; a++)
                {
                    for (int j = a + 1; j < xyzPS.Count; j++)
                    {
                        double distance = xyzPS[a].DistanceTo(xyzPS[j]);
                        if (distance > maxDistance)
                        {
                            maxDistance = distance;
                            firstCorner = xyzPS[a];
                            secondCorner = xyzPS[j];
                        }
                    }
                }
                List<XYZ> remainingPoints = xyzPS.Where(p => p != firstCorner && p != secondCorner).ToList();
                List<XYZ> otherCorners = remainingPoints.OrderByDescending(p => DistanceToLine(firstCorner, secondCorner, p)).Take(2).ToList();
                List<XYZ> cornerPoints = new List<XYZ> { firstCorner, secondCorner };
                Line PCl1 = null;
                Line PCl2 = null;
                Line PCl3 = null;
                Dictionary<double, List<XYZ>> linesWithLengths = new Dictionary<double, List<XYZ>>();

                if (otherCorners.Count >= 2 && (Math.Round(cornerPoints[0].Y, 5) != Math.Round(cornerPoints[1].Y, 5)))
                {
                    if (verticalLayerCount != multilayerdPS.Count)
                    {
                        cornerPoints.AddRange(otherCorners);

                        //Change the corner as it near by previous list of element
                        Dictionary<Element, XYZ> cornerwithElement = new Dictionary<Element, XYZ>();
                        foreach (XYZ xyz in cornerPoints)
                        {
                            Element elecor = multilayerdPS
                                                .Where(kvp => xyz == (kvp.Key))
                                                .Select(kvp => kvp.Value).FirstOrDefault();
                            cornerwithElement.Add(elecor, xyz);
                        }
                        if (previousListofElement.Count > 0)
                        {
                            List<Element> cornerElements = multilayerdPS
                                                     .Where(kvp => cornerPoints.Contains(kvp.Key))
                                                     .Select(kvp => kvp.Value)
                                                     .ToList();
                            Dictionary<double, XYZ> orderCornerLength = new Dictionary<double, XYZ>();
                            foreach (KeyValuePair<Element, XYZ> kvp in cornerwithElement)
                            {
                                XYZ cornerOrigin = Utility.GetLineFromConduit(kvp.Key).Origin;
                                XYZ previousOrigin = Utility.GetLineFromConduit(previousListofElement[0]).Origin;
                                Line checkline = Line.CreateBound(Utility.GetXYvalue(cornerOrigin), Utility.GetXYvalue(previousOrigin));
                                //doc.Create.NewDetailCurve(doc.ActiveView, checkline);
                                orderCornerLength.Add(checkline.Length, kvp.Value);
                            }
                            orderCornerLength = orderCornerLength.OrderBy(x => x.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                            cornerPoints = orderCornerLength.Values.ToList();
                        }


                        List<XYZ> cornerPointsBackup = cornerPoints;
                        double commonZ = xyzPS[0].Z;
                        double minX = xyzPS.Min(p => p.X);
                        double minY = xyzPS.Min(p => p.Y);
                        double maxX = xyzPS.Max(p => p.X);
                        double maxY = xyzPS.Max(p => p.Y);
                        XYZ topLeft = new XYZ(minX, maxY, commonZ);      // (minX, maxY)
                        XYZ topRight = new XYZ(maxX, maxY, commonZ);     // (maxX, maxY)
                        XYZ bottomLeft = new XYZ(minX, minY, commonZ);   // (minX, minY)
                        XYZ bottomRight = new XYZ(maxX, minY, commonZ);  // (maxX, minY)
                        List<XYZ> _cornerPoints = new List<XYZ> { topLeft, topRight, bottomLeft, bottomRight };

                        if (_previousXYZ != null)
                        {
                            XYZ[] cp = cornerPoints.ToArray();
                            XYZ minDistanceCorner = FindMinimumDistance(_previousXYZ, cp);
                            cornerPoints = new List<XYZ> { minDistanceCorner };
                            cornerPoints.AddRange(cornerPointsBackup.Except(cornerPoints));
                        }
                        /*doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                        new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0)));
                        doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                        new XYZ(cornerPoints[2].X, cornerPoints[2].Y, 0)));
                        doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                        new XYZ(cornerPoints[3].X, cornerPoints[3].Y, 0)));*/
                        PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                     new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                        PCl2 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                         new XYZ(cornerPoints[2].X, cornerPoints[2].Y, 0));
                        PCl3 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                         new XYZ(cornerPoints[3].X, cornerPoints[3].Y, 0));
                        linesWithLengths = new Dictionary<double, List<XYZ>>
                                                       {
                                                           {PCl1.Length,new List< XYZ>() {cornerPoints[0], cornerPoints[1] } },
                                                           {PCl2.Length,new List< XYZ>() { cornerPoints[0], cornerPoints[2] }  },
                                                           {PCl3.Length,new List< XYZ>() { cornerPoints[0], cornerPoints[3] }  }
                                                       };
                        linesWithLengths = linesWithLengths.OrderBy(x => x.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        linesWithLengths.Remove(linesWithLengths.Keys.FirstOrDefault());
                        linesWithLengths.Remove(linesWithLengths.Keys.LastOrDefault());
                    }
                    else
                    {
                        PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                        linesWithLengths = new Dictionary<double, List<XYZ>> { { PCl1.Length, new List<XYZ>() { cornerPoints[0], cornerPoints[1] } } };
                    }
                }
                else
                {
                    PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                 new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                    linesWithLengths = new Dictionary<double, List<XYZ>> { { PCl1.Length, new List<XYZ>() { cornerPoints[0], cornerPoints[1] } } };
                }

                List<XYZ> XYZPoints = linesWithLengths.Select(x => x.Value).ToList().FirstOrDefault();
                List<Element> matchingElements = multilayerdPS
                                                 .Where(kvp => XYZPoints.Contains(kvp.Key))
                                                 .Select(kvp => kvp.Value)
                                                 .ToList();

                #region CENTER CONDUIT CREATE TO FIND INTERSECT ANY OTHER CONDUITS 
                List<Element> conduitsBetween = new List<Element>();
                XYZ midPoint1 = (((matchingElements[0].Location as LocationCurve).Curve).GetEndPoint(0) +
                  ((matchingElements[0].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
                XYZ midPoint2 = (((matchingElements[1].Location as LocationCurve).Curve).GetEndPoint(0) +
                   ((matchingElements[1].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
                List<XYZ> midXYZs = new List<XYZ>() { midPoint1, midPoint2 };
                double outsideDiameter1 = matchingElements[0].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
                double outsideDiameter2 = matchingElements[1].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
                Line connectedLine = Line.CreateBound(midXYZs[0], midXYZs[1]);
                XYZ direction = connectedLine.Direction;
                XYZ newXYZ1 = midXYZs[0] - direction * (outsideDiameter1 / 2);
                XYZ newXYZ2 = midXYZs[1] + direction * (outsideDiameter2 / 2);
                Line centerLine = Line.CreateBound(newXYZ1, newXYZ2);
                otherConduit = Utility.CreateConduit(doc, matchingElements[0], centerLine);
                List<Element> collector = multilayerdPS.Select(x => x.Value).ToList();
                double largestDiameter = collector.Max(conduit =>
                {
                    Autodesk.Revit.DB.Parameter diameterParam = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                    return diameterParam?.AsDouble() ?? 0;
                });
                Autodesk.Revit.DB.Parameter newDiameterParam = otherConduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (newDiameterParam != null && newDiameterParam.IsReadOnly == false)
                {
                    newDiameterParam.Set(largestDiameter);
                }
                #endregion
                #region SOLID INTERSECTION METHOD
                Options opt = new Options();
                GeometryElement GE = otherConduit.get_Geometry(opt);
                foreach (GeometryObject GO in GE)
                {
                    if (GO is Solid)
                    {
                        Solid solid = (Solid)GO;
                        ElementIntersectsSolidFilter filter = new ElementIntersectsSolidFilter(solid);
                        List<Conduit> ConduitsIntersecting = new FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(typeof(Conduit))
                            .WherePasses(filter).Cast<Conduit>().ToList();
                        foreach (Conduit con in ConduitsIntersecting)
                        {
                            if (con.Id != matchingElements[0].Id)
                            {
                                foreach (KeyValuePair<XYZ, Element> PS in multilayerdPS)
                                {
                                    if ((PS.Value as Conduit).Id == con.Id)
                                    {
                                        GroupedElement.Add(PS.Value);
                                    }
                                }
                            }
                        }
                    }
                }
                GroupedElement = ArrangeConduits(doc, matchingElements[0] as Conduit, GroupedElement);
                #endregion
                /*#region CURVE INTERSECTION METHOD
                conduitsBetween.Add(matchingElements[0]);
                foreach (Element conduit in collector)
                {
                    LocationCurve conduitCurve = conduit.Location as LocationCurve;
                    if (conduitCurve == null) continue;
                    if (conduit.Id == otherConduit.Id) continue;
                    LocationCurve otherConduitCurve = otherConduit.Location as LocationCurve;
                    if (conduit.Id != matchingElements[0].Id && conduit.Id != matchingElements[1].Id)
                    {
                        XYZ IP = Utility.GetIntersection(conduitCurve.Curve as Line, otherConduitCurve.Curve as Line);
                        if (IP != null)
                        {
                            if (!conduitsBetween.Contains(otherConduit))
                            {
                                conduitsBetween.Add(conduit);
                            }
                        }
                    }
                }
                conduitsBetween.Add(matchingElements[1]);
                GroupedElement = conduitsBetween;
                #endregion*/
                if (otherConduit != null)
                {
                    doc.Delete(otherConduit.Id);
                }
                trans.Commit();
            }
            return GroupedElement;
        }
        public static List<Element> FindCornerConduitsKick(Dictionary<XYZ, Element> multilayerdPS, List<XYZ> xyzPS, Document doc, bool isangledVerticalConduits, List<Element> primaryelementCount)
        {
            List<Element> GroupedElement = new List<Element>();
            using (SubTransaction trans = new SubTransaction(doc))
            {
                trans.Start();
                double maxDistance = 0;
                XYZ firstCorner = null;
                XYZ secondCorner = null;
                for (int a = 0; a < xyzPS.Count; a++)
                {
                    for (int j = a + 1; j < xyzPS.Count; j++)
                    {
                        double distance = xyzPS[a].DistanceTo(xyzPS[j]);
                        if (distance > maxDistance)
                        {
                            maxDistance = distance;
                            firstCorner = xyzPS[a];
                            secondCorner = xyzPS[j];
                        }
                    }
                }
                List<XYZ> remainingPoints = xyzPS.Where(p => p != firstCorner && p != secondCorner).ToList();
                List<XYZ> otherCorners = remainingPoints.OrderByDescending(p => DistanceToLine(firstCorner, secondCorner, p)).Take(2).ToList();
                List<XYZ> cornerPoints = new List<XYZ> { firstCorner, secondCorner };
                Line PCl1 = null;
                Line PCl2 = null;
                Line PCl3 = null;
                Dictionary<double, List<XYZ>> linesWithLengths = new Dictionary<double, List<XYZ>>();

                if ((Math.Round(cornerPoints[0].X, 4) != Math.Round(cornerPoints[1].X, 4)))
                {
                    if (primaryelementCount.Count != multilayerdPS.Count)
                    {
                        cornerPoints.AddRange(otherCorners);
                        List<XYZ> cornerPointsBackup = cornerPoints;

                        double commonZ = xyzPS[0].Z;
                        double minX = xyzPS.Min(p => p.X);
                        double minY = xyzPS.Min(p => p.Y);
                        double maxX = xyzPS.Max(p => p.X);
                        double maxY = xyzPS.Max(p => p.Y);
                        XYZ topLeft = new XYZ(minX, maxY, commonZ);
                        XYZ topRight = new XYZ(maxX, maxY, commonZ);
                        XYZ bottomLeft = new XYZ(minX, minY, commonZ);
                        XYZ bottomRight = new XYZ(maxX, minY, commonZ);
                        List<XYZ> _cornerPoints = new List<XYZ> { topLeft, topRight, bottomLeft, bottomRight };

                        if (_previousXYZ != null)
                        {
                            XYZ[] cp = cornerPoints.ToArray();
                            XYZ minDistanceCorner = FindMinimumDistance(_previousXYZ, cp);
                            cornerPoints = new List<XYZ> { minDistanceCorner };
                            cornerPoints.AddRange(cornerPointsBackup.Except(cornerPoints));
                        }
                        PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                     new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                        PCl2 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                         new XYZ(cornerPoints[2].X, cornerPoints[2].Y, 0));
                        PCl3 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                         new XYZ(cornerPoints[3].X, cornerPoints[3].Y, 0));
                        linesWithLengths = new Dictionary<double, List<XYZ>>
                                                       {
                                                           {PCl1.Length,new List< XYZ>() {cornerPoints[0], cornerPoints[1] } },
                                                           {PCl2.Length,new List< XYZ>() { cornerPoints[0], cornerPoints[2] }  },
                                                           {PCl3.Length,new List< XYZ>() { cornerPoints[0], cornerPoints[3] }  }
                                                       };
                        linesWithLengths = linesWithLengths.OrderBy(x => x.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        linesWithLengths.Remove(linesWithLengths.Keys.FirstOrDefault());
                        linesWithLengths.Remove(linesWithLengths.Keys.LastOrDefault());
                    }
                    else
                    {
                        PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                     new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                        linesWithLengths = new Dictionary<double, List<XYZ>> { { PCl1.Length, new List<XYZ>() { cornerPoints[0], cornerPoints[1] } } };
                    }
                }
                else
                {
                    PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                 new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                    linesWithLengths = new Dictionary<double, List<XYZ>> { { PCl1.Length, new List<XYZ>() { cornerPoints[0], cornerPoints[1] } } };
                }

                List<XYZ> XYZPoints = linesWithLengths.Select(x => x.Value).ToList().FirstOrDefault();
                List<Element> matchingElements = multilayerdPS
                                                 .Where(kvp => XYZPoints.Contains(kvp.Key))
                                                 .Select(kvp => kvp.Value)
                                                 .ToList();
                if (isangledVerticalConduits)
                {
                    XYZ midPoint1 = (((matchingElements[0].Location as LocationCurve).Curve).GetEndPoint(0) +
                ((matchingElements[0].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
                    XYZ midPoint2 = (((matchingElements[1].Location as LocationCurve).Curve).GetEndPoint(0) +
                       ((matchingElements[1].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
                    List<XYZ> midXYZs = new List<XYZ>() { midPoint1, midPoint2 };
                    double outsideDiameter1 = matchingElements[0].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
                    double outsideDiameter2 = matchingElements[1].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
                    Line connectedLine = Line.CreateBound(midXYZs[0], midXYZs[1]);
                    XYZ direction = connectedLine.Direction;
                    XYZ newXYZ1 = midXYZs[0] - direction * (outsideDiameter1 / 2);
                    XYZ newXYZ2 = midXYZs[1] + direction * (outsideDiameter2 / 2);
                    Line centerLine = Line.CreateBound(newXYZ1, newXYZ2);
                    otherConduit = Utility.CreateConduit(doc, matchingElements[0], centerLine);
                    Element midPointConduit = null;
                    List<Element> collector = multilayerdPS.Select(x => x.Value).ToList();
                    List<Element> conduitsBetween = new List<Element>();
                    conduitsBetween.Add(matchingElements[0]);
                    foreach (Element conduit in collector)
                    {
                        LocationCurve conduitCurve = conduit.Location as LocationCurve;
                        if (conduitCurve == null) continue;
                        if (conduit.Id == otherConduit.Id) continue;
                        LocationCurve otherConduitCurve = otherConduit.Location as LocationCurve;
                        if (conduit.Id != matchingElements[0].Id && conduit.Id != matchingElements[1].Id)
                        {
                            SetComparisonResult result = conduitCurve.Curve.Intersect(otherConduitCurve.Curve, out IntersectionResultArray intersectionResultArray);
                            if (result == SetComparisonResult.Overlap)
                            {
                                if (!conduitsBetween.Contains(otherConduit))
                                {
                                    conduitsBetween.Add(conduit);
                                }
                                if (midPointConduit == null || midPointConduit.Id != conduit.Id)
                                {
                                    midPointConduit = conduit;
                                }
                            }
                        }
                    }
                    conduitsBetween.Add(matchingElements[1]);
                    GroupedElement = conduitsBetween;
                    if (otherConduit != null)
                    {
                        doc.Delete(otherConduit.Id);
                    }
                }
                else
                {
                    List<XYZ> orderedPoints = CreateBoundingBoxLineKick(linesWithLengths, matchingElements, multilayerdPS, doc);
                    GroupedElement = multilayerdPS
                                                  .Where(kvp => orderedPoints.Contains(kvp.Key))
                                                  .Select(kvp => kvp.Value)
                                                  .ToList();
                    _previousXYZ = cornerPoints[0];
                }
                trans.Commit();
            }
            return GroupedElement;
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
                        System.Windows.MessageBox.Show("Please select the conduits and ensure they have fittings on both sides.", "Warning-AutoUpdate", MessageBoxButton.OK, MessageBoxImage.Warning);
                        uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                    }
                }
                if (SelectedElements.Count() != 0)
                {
                    List<Element> conduitCollection = new List<Element>();
                    Dictionary<int, List<ConduitGrid>> CongridDictionary1 = GroupStubElements(SelectedElements);
                    Dictionary<double, List<Element>> group = new Dictionary<double, List<Element>>();
                    if (CongridDictionary1.Count == 2)
                    {
                        List<Element> dictFirstElement = CongridDictionary1.First().Value.Select(x => x.Conduit).ToList();
                        List<Element> dictSecondElement = CongridDictionary1.Last().Value.Select(x => x.Conduit).ToList();
                        //STUB AND KICK CONNECT
                        if (Math.Round(Utility.GetLineFromConduit(dictFirstElement[0]).GetEndPoint(0).Z, 4) == Math.Round(Utility.GetLineFromConduit(dictFirstElement[0]).GetEndPoint(1).Z, 4) &&
                           Math.Round(Utility.GetLineFromConduit(dictSecondElement[0]).GetEndPoint(0).Z, 4) != Math.Round(Utility.GetLineFromConduit(dictSecondElement[0]).GetEndPoint(1).Z, 4))
                        {
                            //STUB PROCESS
                            List<Element> dictSecondElementDUP = new List<Element>();
                            List<Element> dictFirstElementDUP = new List<Element>();
                            List<KeyValuePair<double, List<Element>>> elementPairsFirst = new List<KeyValuePair<double, List<Element>>>();
                            List<KeyValuePair<double, List<Element>>> elementPairsSecond = new List<KeyValuePair<double, List<Element>>>();
                            Dictionary<double, List<Element>> stubGroupPrimary = GroupByElementsWithElevation(dictFirstElement, offsetVariable);
                            //Order the Vertical Conduits for Stub Down
                            //using (Transaction transOrder = new Transaction(doc))
                            //{
                            //transOrder.Start("Order the Vertical Conduits");
                            List<Element> primaryelementCountStub = stubGroupPrimary.FirstOrDefault().Value;
                            Dictionary<XYZ, Element> multiorderthePrimaryElementsStub = new Dictionary<XYZ, Element>();
                            Dictionary<XYZ, Element> multiordertheSecondaryElementsStub = new Dictionary<XYZ, Element>();
                            List<Element> GroupedPrimaryElementStub = new List<Element>();
                            List<Element> GroupedSecondaryElementStub = new List<Element>();
                            List<Element> _firstKickGroupStub = new List<Element>();
                            List<Element> _secondKickGroupStub = new List<Element>();
                            List<Element> primaryEGroupedviaZStub = new List<Element>();
                            List<XYZ> primaryXYZGroupedviaZStub = new List<XYZ>();
                            bool isangledVerticalConduitsStub = false;
                            foreach (Element element in dictFirstElement)
                            {
                                XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                multiorderthePrimaryElementsStub.Add(xyzPelement, element);
                            }
                            foreach (Element element in dictSecondElement)
                            {
                                XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                multiordertheSecondaryElementsStub.Add(xyzPelement, element);
                            }
                            //STUB DOWN MULTI LAYER
                            if (stubGroupPrimary.Count > 1)
                            {
                                Dictionary<double, List<Element>> sortedGroupPrimaryStub = stubGroupPrimary.OrderByDescending(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                foreach (KeyValuePair<double, List<Element>> pair in stubGroupPrimary)
                                {
                                    GroupedPrimaryElementStub.AddRange(pair.Value);
                                }
                                List<XYZ> xyzPS = multiordertheSecondaryElementsStub.Select(x => x.Key).ToList();
                                List<XYZ> roundOFF = new List<XYZ>();
                                foreach (var xyz in xyzPS)
                                {
                                    XYZ roundedXYZ = new XYZ(Math.Round(xyz.X, 5), Math.Round(xyz.Y, 5), Math.Round(xyz.Z, 5));
                                    roundOFF.Add(roundedXYZ);
                                }
                                bool hasDuplicateY = HasDuplicateYCoordinates(roundOFF);
                                Dictionary<double, List<Element>> dictSecondaryElementStub = new Dictionary<double, List<Element>>();
                                _previousXYZ = null;
                                /*#region NEW LOGICS 
                                int m = 0;
                                int verticalLayerCount = 0;
                                do
                                {
                                    List<XYZ> xyzListPrimary = new List<XYZ>();
                                    List<XYZ> xyzListSecondary = new List<XYZ>();
                                    xyzListSecondary.AddRange(multiordertheSecondaryElementsStub.Select(x => x.Key));
                                    List<Element> Sele = FindCornerConduitsInclinedVerticalConduits(multiordertheSecondaryElementsStub, xyzListSecondary, doc, verticalLayerCount, primaryelementCountStub);
                                    dictSecondaryElementStub.Add(m, Sele);
                                    GroupedSecondaryElementStub.AddRange(Sele);
                                    m++;
                                    multiordertheSecondaryElementsStub = multiordertheSecondaryElementsStub.Where(kvp => !GroupedSecondaryElementStub.Any(e => e.Id == kvp.Value.Id))
                                                                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                }
                                while (multiordertheSecondaryElementsStub.Count > 0);
                                #endregion*/
                                #region OLD LOGICS
                                if (hasDuplicateY)
                                {
                                    _previousXYZ = null;
                                    int i = 0;
                                    do
                                    {
                                        List<XYZ> xyzListPrimary = new List<XYZ>();
                                        List<XYZ> xyzListSecondary = new List<XYZ>();
                                        xyzListSecondary.AddRange(multiordertheSecondaryElementsStub.Select(x => x.Key));
                                        List<Element> Sele = FindCornerConduitsStub(multiordertheSecondaryElementsStub, xyzListSecondary, doc, isangledVerticalConduitsStub, primaryelementCountStub);
                                        dictSecondaryElementStub.Add(i, Sele);
                                        GroupedSecondaryElementStub.AddRange(Sele);
                                        i++;
                                        multiordertheSecondaryElementsStub = multiordertheSecondaryElementsStub.Where(kvp => !GroupedSecondaryElementStub.Any(e => e.Id == kvp.Value.Id))
                                                                       .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                    }
                                    while (multiordertheSecondaryElementsStub.Count > 0);
                                }
                                else
                                {
                                    isangledVerticalConduitsStub = true;
                                    _previousXYZ = null;
                                    int i = 0;
                                    do
                                    {
                                        List<XYZ> xyzListPrimary = new List<XYZ>();
                                        List<XYZ> xyzListSecondary = new List<XYZ>();
                                        xyzListSecondary.AddRange(multiordertheSecondaryElementsStub.Select(x => x.Key));
                                        List<Element> Sele = FindCornerConduitsStub(multiordertheSecondaryElementsStub, xyzListSecondary, doc, isangledVerticalConduitsStub, primaryelementCountStub);
                                        dictSecondaryElementStub.Add(i, Sele);
                                        GroupedSecondaryElementStub.AddRange(Sele);
                                        i++;
                                        multiordertheSecondaryElementsStub = multiordertheSecondaryElementsStub.Where(kvp => !GroupedSecondaryElementStub.Any(e => e.Id == kvp.Value.Id))
                                                                       .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                    }
                                    while (multiordertheSecondaryElementsStub.Count > 0);
                                }
                                #endregion
                                int q = 0;
                                Dictionary<double, List<Element>> _dictGetMatchElementfromFirst = new Dictionary<double, List<Element>>();
                                Dictionary<double, List<Element>> _dictGetMatchElementfromSecond = new Dictionary<double, List<Element>>();
                                do
                                {
                                    Dictionary<Line, Element> _dictlineelementStub = new Dictionary<Line, Element>();
                                    List<Element> highestElevation = sortedGroupPrimaryStub.Values.FirstOrDefault();
                                    List<Element> storedSecondaryElement = new List<Element>();
                                    for (int i = 0; i < 1; i++)
                                    {
                                        ConnectorSet PrimaryConnectors = Utility.GetConnectorSet(highestElevation[i]);
                                        foreach (KeyValuePair<double, List<Element>> dec in dictSecondaryElementStub)
                                        {
                                            ConnectorSet SecondaryConnectors = Utility.GetConnectorSet(dec.Value.FirstOrDefault());
                                            Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                            Line checkline = Line.CreateBound(Utility.GetXYvalue(ConnectorOne.Origin), Utility.GetXYvalue(ConnectorTwo.Origin));
                                            _dictlineelementStub.Add(checkline, dec.Value.FirstOrDefault());
                                        }
                                        double secElevation = (((dictSecondaryElementStub.Values.FirstOrDefault().FirstOrDefault().Location as LocationCurve).Curve) as Line).Origin.Z;
                                        double priElevation = (((highestElevation[i].Location as LocationCurve).Curve) as Line).Origin.Z;
                                        Line distanceLine = null;
                                        if (priElevation > secElevation)
                                        {
                                            distanceLine = _dictlineelementStub.Keys.OrderByDescending(line => line.Length).FirstOrDefault();
                                        }
                                        else if (priElevation < secElevation)
                                        {
                                            distanceLine = _dictlineelementStub.Keys.OrderBy(line => line.Length).FirstOrDefault();
                                        }
                                        Element distanceLineElement = _dictlineelementStub.Where(kvp => kvp.Key == distanceLine).Select(kvp => kvp.Value).FirstOrDefault();
                                        List<Element> sec = dictSecondaryElementStub.Where(kvp => kvp.Value.Any(x => x == distanceLineElement))
                                                                                    .Select(kvp => kvp.Value).FirstOrDefault();

                                        Dictionary<XYZ, Element> orderXYZStub = new Dictionary<XYZ, Element>();
                                        foreach (Element secele in sec)
                                        {
                                            XYZ xyz = (((secele.Location as LocationCurve).Curve) as Line).Origin;
                                            orderXYZStub.Add(xyz, secele);
                                        }
                                        orderXYZStub = orderXYZStub.OrderByDescending(kvp => kvp.Key.Y).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                        List<Element> secOrder = orderXYZStub.Select(x => x.Value).ToList();
                                        _secondKickGroupStub.AddRange(secOrder);
                                        if (_dictGetMatchElementfromSecond.ContainsKey(((secOrder[0].Location as LocationCurve).Curve as Line).Origin.Z))
                                        {
                                            _dictGetMatchElementfromSecond[((secOrder[0].Location as LocationCurve).Curve as Line).Origin.Z].AddRange(secOrder);
                                        }
                                        else
                                        {
                                            _dictGetMatchElementfromSecond.Add(((secOrder[0].Location as LocationCurve).Curve as Line).Origin.Z, secOrder);
                                        }
                                        storedSecondaryElement.AddRange(secOrder);
                                        _dictReorderStub.Add(q, storedSecondaryElement);
                                        q++;
                                        dictSecondaryElementStub.Remove(dictSecondaryElementStub.FirstOrDefault(kvp => kvp.Value == sec).Key);
                                        if (dictSecondaryElementStub.Count == 1)
                                        {
                                            storedSecondaryElement = new List<Element>();
                                            orderXYZStub = new Dictionary<XYZ, Element>();
                                            foreach (Element secele in dictSecondaryElementStub.Values.FirstOrDefault())
                                            {
                                                XYZ xyz = (((secele.Location as LocationCurve).Curve) as Line).Origin;
                                                orderXYZStub.Add(xyz, secele);
                                            }
                                            orderXYZStub = orderXYZStub.OrderByDescending(kvp => kvp.Key.Y).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                            secOrder = orderXYZStub.Select(x => x.Value).ToList();
                                            _secondKickGroupStub.AddRange(secOrder);
                                            if (_dictGetMatchElementfromSecond.ContainsKey(((secOrder[0].Location as LocationCurve).Curve as Line).Origin.Z))
                                            {
                                                _dictGetMatchElementfromSecond[((secOrder[0].Location as LocationCurve).Curve as Line).Origin.Z].AddRange(secOrder);
                                            }
                                            else
                                            {
                                                _dictGetMatchElementfromSecond.Add(((secOrder[0].Location as LocationCurve).Curve as Line).Origin.Z, secOrder);
                                            }
                                            storedSecondaryElement.AddRange(secOrder);
                                            storedSecondaryElement.AddRange(secOrder);
                                            _dictReorderStub.Add(q, storedSecondaryElement);
                                            dictSecondaryElementStub.Clear();
                                        }
                                    }

                                    List<Element> pri = sortedGroupPrimaryStub.Where(kvp => kvp.Value.Any(x => x == highestElevation.FirstOrDefault()))
                                                                          .Select(kvp => kvp.Value).FirstOrDefault();
                                    _firstKickGroupStub.AddRange(pri);
                                    if (_dictGetMatchElementfromFirst.ContainsKey(((pri[0].Location as LocationCurve).Curve as Line).Origin.Z))
                                    {
                                        _dictGetMatchElementfromFirst[((pri[0].Location as LocationCurve).Curve as Line).Origin.Z].AddRange(pri);
                                    }
                                    else
                                    {
                                        _dictGetMatchElementfromFirst.Add(((pri[0].Location as LocationCurve).Curve as Line).Origin.Z, pri);
                                    }
                                    sortedGroupPrimaryStub.Remove(sortedGroupPrimaryStub.FirstOrDefault(kvp => kvp.Value == pri).Key);
                                    if (sortedGroupPrimaryStub.Count == 1)
                                    {
                                        _firstKickGroupStub.AddRange(sortedGroupPrimaryStub.Values.FirstOrDefault());
                                        if (_dictGetMatchElementfromFirst.ContainsKey(((sortedGroupPrimaryStub.Values.FirstOrDefault()[0].Location as LocationCurve).Curve as Line).Origin.Z))
                                        {
                                            _dictGetMatchElementfromFirst[((sortedGroupPrimaryStub.Values.FirstOrDefault()[0].Location as LocationCurve).Curve as Line).Origin.Z].
                                                AddRange(sortedGroupPrimaryStub.Values.FirstOrDefault());
                                        }
                                        else
                                        {
                                            _dictGetMatchElementfromFirst.Add(((sortedGroupPrimaryStub.Values.FirstOrDefault()[0].Location as LocationCurve).Curve as Line).Origin.Z,
                                                sortedGroupPrimaryStub.Values.FirstOrDefault());
                                        }
                                        sortedGroupPrimaryStub.Clear();
                                    }
                                }
                                while (sortedGroupPrimaryStub.Count > 1 && sortedGroupPrimaryStub.Count == dictSecondaryElementStub.Count);
                                List<KeyValuePair<Element, double>> orderedList = new List<KeyValuePair<Element, double>>();
                                elementPairsFirst = _dictGetMatchElementfromFirst.OrderByDescending(kvp => kvp.Key).ToList();
                                elementPairsSecond = _dictGetMatchElementfromSecond.OrderByDescending(kvp => kvp.Key).ToList();
                                if (elementPairsFirst.Count == elementPairsSecond.Count)
                                {
                                    List<double> firstDictKeys = new List<double>();
                                    List<double> secondDictKeys = new List<double>();
                                    foreach (KeyValuePair<double, List<Element>> pairs in elementPairsFirst)
                                    {
                                        firstDictKeys.Add(pairs.Key);
                                    }
                                    foreach (KeyValuePair<double, List<Element>> pairs in elementPairsSecond)
                                    {
                                        secondDictKeys.Add(pairs.Key);
                                    }
                                    for (int c = 0; c < firstDictKeys.Count; c++)
                                    {
                                        Dictionary<Element, double> ordertheLineLengthStub = new Dictionary<Element, double>();
                                        List<Element> firstList = new List<Element>();
                                        List<Element> secondList = new List<Element>();
                                        double firstKey = firstDictKeys[c];
                                        List<Element> firstElements = _dictGetMatchElementfromFirst[firstKey];
                                        firstList.AddRange(firstElements);
                                        ConnectorSet FirstConnectors = Utility.GetConnectorSet(firstList.FirstOrDefault());
                                        double secondKey = secondDictKeys[c];
                                        List<Element> secondElements = _dictGetMatchElementfromSecond[secondKey];
                                        secondList.AddRange(secondElements);
                                        for (int k = 0; k < secondList.Count; k++)
                                        {
                                            Element fl = secondList[k];
                                            ConnectorSet SecondConnectors = Utility.GetConnectorSet(fl);
                                            Utility.GetClosestConnectors(FirstConnectors, SecondConnectors, out Connector ConnectorFirst, out Connector ConnectorSecond);
                                            Line line = Line.CreateBound(new XYZ(ConnectorFirst.Origin.X, ConnectorFirst.Origin.Y, 0),
                                                                         new XYZ(ConnectorSecond.Origin.X, ConnectorSecond.Origin.Y, 0));
                                            ordertheLineLengthStub.Add(fl, Math.Round(line.Length, 4));
                                        }
                                        orderedList = ordertheLineLengthStub.OrderBy(kvp => kvp.Value).ToList();
                                        foreach (KeyValuePair<Element, double> kvpEle in orderedList)
                                        {
                                            dictSecondElementDUP.Add(kvpEle.Key);
                                        }
                                        dictFirstElementDUP.AddRange(firstElements);
                                    }
                                }
                                else
                                {
                                    List<Element> firstElements = new List<Element>();
                                    List<Element> secondElements = new List<Element>();
                                    foreach (KeyValuePair<double, List<Element>> pairs in elementPairsFirst)
                                    {
                                        firstElements.AddRange(pairs.Value);
                                    }
                                    secondElements = elementPairsSecond.Select(x => x.Value).FirstOrDefault();
                                    ConnectorSet FirstConnectors = Utility.GetConnectorSet(firstElements[0]);
                                    Dictionary<Element, double> ordertheLineLength = new Dictionary<Element, double>();
                                    foreach (Element secEle in secondElements)
                                    {
                                        ConnectorSet SecondConnectors = Utility.GetConnectorSet(secEle);
                                        Utility.GetClosestConnectors(FirstConnectors, SecondConnectors, out Connector ConnectorFirst, out Connector ConnectorSecond);
                                        Line line = Line.CreateBound(new XYZ(ConnectorFirst.Origin.X, ConnectorFirst.Origin.Y, 0),
                                                                  new XYZ(ConnectorSecond.Origin.X, ConnectorSecond.Origin.Y, 0));
                                        ordertheLineLength.Add(secEle, Math.Round(line.Length, 4));
                                    }
                                    orderedList = ordertheLineLength.OrderBy(kvp => kvp.Value).ToList();
                                    foreach (KeyValuePair<Element, double> kvpEle in orderedList)
                                    {
                                        dictSecondElementDUP.Add(kvpEle.Key);
                                    }
                                    dictFirstElementDUP.AddRange(firstElements);
                                    isStubCreate = false;//because of elevation same in vertical conduits ,it need varying elevation in layers
                                }
                            }
                            //STUB DOWN SINGLE LAYER ORDER
                            else
                            {
                                ConnectorSet FirstConnectors = Utility.GetConnectorSet(dictFirstElement[0]);
                                Dictionary<Element, double> ordertheLineLength = new Dictionary<Element, double>();
                                foreach (Element secEle in dictSecondElement)
                                {
                                    ConnectorSet SecondConnectors = Utility.GetConnectorSet(secEle);
                                    Utility.GetClosestConnectors(FirstConnectors, SecondConnectors, out Connector ConnectorFirst, out Connector ConnectorSecond);
                                    Line line = Line.CreateBound(new XYZ(ConnectorFirst.Origin.X, ConnectorFirst.Origin.Y, 0),
                                                              new XYZ(ConnectorSecond.Origin.X, ConnectorSecond.Origin.Y, 0));
                                    ordertheLineLength.Add(secEle, Math.Round(line.Length, 4));
                                }
                                List<KeyValuePair<Element, double>> orderedList = ordertheLineLength.OrderBy(kvp => kvp.Value).ToList();
                                foreach (KeyValuePair<Element, double> kvpEle in orderedList)
                                {
                                    dictSecondElementDUP.Add(kvpEle.Key);
                                }
                                dictFirstElementDUP = dictFirstElement;
                                //Order the Primary Conduits 
                                List<Line> previousLine = new List<Line>();
                                bool isReverseDone = false;
                                for (int z = 0; z < dictSecondElementDUP.Count; z++)
                                {
                                    ConnectorSet PrimaryConnectors = Utility.GetConnectorSet(dictFirstElementDUP[z]);
                                    ConnectorSet SecondaryConnectors = Utility.GetConnectorSet(dictSecondElementDUP[z]);
                                    Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                    Line checkline = Line.CreateBound(Utility.GetXYvalue(ConnectorOne.Origin), Utility.GetXYvalue(ConnectorTwo.Origin));
                                    foreach (Line pl in previousLine)
                                    {
                                        if (Utility.GetIntersection(pl, checkline) != null)
                                        {
                                            dictFirstElementDUP.Reverse();
                                            isReverseDone = true;
                                            break;
                                        }
                                    }
                                    if (isReverseDone)
                                        break;
                                    previousLine.Add(checkline);
                                }
                            }
                            //  transOrder.Commit();
                            // }
                            LocationCurve locCurve2 = null;
                            LocationCurve locCurve1 = null;
                            Line connectLine = null;
                            using Transaction transLine = new Transaction(doc);
                            transLine.Start("Line");
                            if (dictFirstElementDUP.Count > 0 && dictSecondElementDUP.Count > 0)
                            {
                                locCurve1 = dictFirstElementDUP[0].Location as LocationCurve;
                                locCurve2 = dictSecondElementDUP[0].Location as LocationCurve;
                                XYZ startPoint = Utility.GetXYvalue(locCurve1.Curve.GetEndPoint(0));
                                XYZ endPoint = Utility.GetXYvalue(locCurve2.Curve.GetEndPoint(1));
                                //connectLine = Line.CreateBound(startPoint, endPoint);
                                GetClosestConnectorJoin(dictFirstElementDUP[0] as Conduit, dictSecondElementDUP[0] as Conduit,
                                    out Connector closestConnector2, out Connector closestConnector1);
                                connectLine = Line.CreateBound(new XYZ(closestConnector1.Origin.X, closestConnector1.Origin.Y, 0),
                                    new XYZ(closestConnector2.Origin.X, closestConnector2.Origin.Y, 0));
                            }
                            else
                            {
                                locCurve1 = dictFirstElement[0].Location as LocationCurve;
                                locCurve2 = dictSecondElement[0].Location as LocationCurve;
                                XYZ startPoint = Utility.GetXYvalue(locCurve1.Curve.GetEndPoint(0));
                                XYZ endPoint = Utility.GetXYvalue(locCurve2.Curve.GetEndPoint(1));
                                //connectLine = Line.CreateBound(startPoint, endPoint);
                                GetClosestConnectorJoin(dictFirstElement[0] as Conduit, dictSecondElement[0] as Conduit,
                                    out Connector closestConnector2, out Connector closestConnector1);
                                connectLine = Line.CreateBound(new XYZ(closestConnector1.Origin.X, closestConnector1.Origin.Y, 0),
                                    new XYZ(closestConnector2.Origin.X, closestConnector2.Origin.Y, 0));
                            }
                            transLine.Commit();
                            //STUB AND KICK CREATION
                            using (Transaction transaction = new Transaction(doc))
                            {
                                transaction.Start("StubandKick");
                                //90 Stub
                                if (Utility.IsSameDirection(Utility.GetXYvalue(((dictFirstElement[0].Location as LocationCurve).Curve as Line).Direction),
                                Utility.GetXYvalue(connectLine.Direction)))
                                {
                                    if (isStubCreate)
                                    {
                                        try
                                        {
                                            if (MainWindow.Instance.tgleAngleAcute.Visibility == System.Windows.Visibility.Visible)
                                            {
                                                MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Collapsed;
                                                iswhenReloadTool = true;
                                                isOffsetTool = true;
                                                isoffsetwindowClose = true;
                                            }
                                            if (dictFirstElementDUP.Count == dictSecondElementDUP.Count)
                                            {
                                                for (int i = 0; i < dictFirstElement.Count; i++)
                                                {
                                                    Utility.CreateElbowFittings(dictFirstElementDUP[i], dictSecondElementDUP[i], doc, _uiapp);
                                                }
                                            }
                                            else
                                            {
                                                for (int i = 0; i < dictFirstElement.Count; i++)
                                                {
                                                    Utility.CreateElbowFittings(dictFirstElement[i], dictSecondElement[i], doc, _uiapp);
                                                }
                                            }
                                            if (!MainWindow.Instance.isStaticTool)
                                            {
                                                MainWindow.Instance.Close();
                                                ExternalApplication.window = null;
                                                SelectedElements.Clear();
                                                uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                            }
                                            else
                                            {
                                                SelectedElements.Clear();
                                                uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                            }
                                        }
                                        catch
                                        {
                                            System.Windows.MessageBox.Show("Make sure the elevation between the vertical conduits", "Warning",
                                                MessageBoxButton.OK, MessageBoxImage.Warning);
                                            isoffsetwindowClose = true;
                                        }
                                    }
                                    else
                                    {
                                        System.Windows.MessageBox.Show("Make sure the elevation between the vertical conduits", "Warning",
                                                MessageBoxButton.OK, MessageBoxImage.Warning);
                                        isoffsetwindowClose = true;
                                    }
                                }
                                //90 Kick
                                else
                                {
                                    if (MainWindow.Instance.isoffset)
                                    {
                                        Element elementsList = null;
                                        using (SubTransaction trans = new SubTransaction(doc))
                                        {
                                            trans.Start();
                                            SelectedElements.Clear();
                                            uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                            ElementsFilter conduitFilter = new ElementsFilter("Conduits");
                                            Reference pickedRef = uidoc.Selection.PickObject(ObjectType.Element, conduitFilter, "Please select a conduit element.");
                                            if (pickedRef != null)
                                            {
                                                elementsList = doc.GetElement(pickedRef);
                                            }
                                            trans.Commit();
                                        }
                                        Dictionary<XYZ, Element> multiorderthePrimaryElements = new Dictionary<XYZ, Element>();
                                        Dictionary<XYZ, Element> multiordertheSecondaryElements = new Dictionary<XYZ, Element>();
                                        List<Element> GroupedPrimaryElement = new List<Element>();
                                        List<Element> GroupedSecondaryElement = new List<Element>();
                                        List<Element> _firstKickGroup = new List<Element>();
                                        List<Element> _secondKickGroup = new List<Element>();
                                        List<Element> primaryEGroupedviaZ = new List<Element>();
                                        List<XYZ> primaryXYZGroupedviaZ = new List<XYZ>();
                                        foreach (Element element in dictFirstElement)
                                        {
                                            XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                            multiorderthePrimaryElements.Add(xyzPelement, element);
                                        }
                                        foreach (Element element in dictSecondElement)
                                        {
                                            XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                            multiordertheSecondaryElements.Add(xyzPelement, element);
                                        }
                                        Dictionary<double, List<Element>> groupPrimary = GroupByElementsWithElevation(dictFirstElement, offsetVariable);
                                        GroupPrimaryCount = groupPrimary.Count;
                                        List<Element> primaryelementCount = groupPrimary.FirstOrDefault().Value;
                                        bool isangledVerticalConduits = false;
                                        if (groupPrimary.Count > 1)
                                        {
                                            Dictionary<double, List<Element>> sortedGroupPrimary = groupPrimary.OrderByDescending(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                            foreach (KeyValuePair<double, List<Element>> pair in groupPrimary)
                                            {
                                                GroupedPrimaryElement.AddRange(pair.Value);
                                            }
                                            //Grouping Logic for Secondary Conduits 
                                            List<XYZ> xyzPS = multiordertheSecondaryElements.Select(x => x.Key).ToList();
                                            List<XYZ> roundOFF = new List<XYZ>();
                                            foreach (var xyz in xyzPS)
                                            {
                                                XYZ roundedXYZ = new XYZ(Math.Round(xyz.X, 5), Math.Round(xyz.Y, 5), Math.Round(xyz.Z, 5));
                                                roundOFF.Add(roundedXYZ);
                                            }
                                            bool hasDuplicateY = HasDuplicateYCoordinates(roundOFF);
                                            bool hasDuplicateX = HasDuplicateXCoordinates(roundOFF);
                                            Dictionary<double, List<Element>> dictSecondaryElementKick = new Dictionary<double, List<Element>>();
                                            if (hasDuplicateY)
                                            {
                                                _previousXYZ = null;
                                                int i = 0;
                                                do
                                                {
                                                    List<XYZ> xyzListPrimary = new List<XYZ>();
                                                    List<XYZ> xyzListSecondary = new List<XYZ>();
                                                    xyzListSecondary.AddRange(multiordertheSecondaryElements.Select(x => x.Key));
                                                    List<Element> Sele = FindCornerConduitsKick(multiordertheSecondaryElements, xyzListSecondary, doc, isangledVerticalConduits, primaryelementCount);
                                                    dictSecondaryElementKick.Add(i, Sele);
                                                    GroupedSecondaryElement.AddRange(Sele);
                                                    i++;
                                                    multiordertheSecondaryElements = multiordertheSecondaryElements.Where(kvp => !GroupedSecondaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                }
                                                while (multiordertheSecondaryElements.Count > 0);
                                            }
                                            else
                                            {
                                                isangledVerticalConduits = true;
                                                _previousXYZ = null;
                                                int i = 0;
                                                do
                                                {
                                                    List<XYZ> xyzListPrimary = new List<XYZ>();
                                                    List<XYZ> xyzListSecondary = new List<XYZ>();
                                                    xyzListSecondary.AddRange(multiordertheSecondaryElements.Select(x => x.Key));
                                                    List<Element> Sele = FindCornerConduitsKick(multiordertheSecondaryElements, xyzListSecondary, doc, isangledVerticalConduits, primaryelementCount);
                                                    dictSecondaryElementKick.Add(i, Sele);
                                                    GroupedSecondaryElement.AddRange(Sele);
                                                    i++;
                                                    multiordertheSecondaryElements = multiordertheSecondaryElements.Where(kvp => !GroupedSecondaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                }
                                                while (multiordertheSecondaryElements.Count > 0);
                                            }
                                            //Find Maximum Distance Line for Layer Matching
                                            int o = 0;
                                            do
                                            {
                                                Dictionary<Line, Element> _dictlineelement = new Dictionary<Line, Element>();
                                                List<Element> highestElevation = sortedGroupPrimary.Values.FirstOrDefault();
                                                List<Element> storedSecondaryElement = new List<Element>();
                                                for (int i = 0; i < 1; i++)
                                                {
                                                    ConnectorSet PrimaryConnectors = Utility.GetConnectorSet(highestElevation[i]);
                                                    foreach (KeyValuePair<double, List<Element>> dec in dictSecondaryElementKick)
                                                    {
                                                        ConnectorSet SecondaryConnectors = Utility.GetConnectorSet(dec.Value.FirstOrDefault());
                                                        Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                                        Line checkline = Line.CreateBound(Utility.GetXYvalue(ConnectorOne.Origin), Utility.GetXYvalue(ConnectorTwo.Origin));
                                                        _dictlineelement.Add(checkline, dec.Value.FirstOrDefault());
                                                    }
                                                    double secElevation = (((dictSecondaryElementKick.Values.FirstOrDefault().FirstOrDefault().Location as LocationCurve).Curve) as Line).Origin.Z;
                                                    double priElevation = (((highestElevation[i].Location as LocationCurve).Curve) as Line).Origin.Z;
                                                    Line distanceLine = null;
                                                    if (priElevation > secElevation)
                                                    {
                                                        distanceLine = _dictlineelement.Keys.OrderByDescending(line => line.Length).FirstOrDefault();
                                                    }
                                                    else if (priElevation < secElevation)
                                                    {
                                                        distanceLine = _dictlineelement.Keys.OrderBy(line => line.Length).FirstOrDefault();
                                                    }
                                                    Element distanceLineElement = _dictlineelement.Where(kvp => kvp.Key == distanceLine).Select(kvp => kvp.Value).FirstOrDefault();
                                                    List<Element> sec = dictSecondaryElementKick.Where(kvp => kvp.Value.Any(x => x == distanceLineElement))
                                                                                                .Select(kvp => kvp.Value).FirstOrDefault();
                                                    Dictionary<XYZ, Element> orderXYZ = new Dictionary<XYZ, Element>();
                                                    foreach (Element secele in sec)
                                                    {
                                                        XYZ xyz = (((secele.Location as LocationCurve).Curve) as Line).Origin;
                                                        orderXYZ.Add(xyz, secele);
                                                    }
                                                    orderXYZ = orderXYZ.OrderByDescending(kvp => kvp.Key.Y).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                    List<Element> secOrder = orderXYZ.Select(x => x.Value).ToList();
                                                    _secondKickGroup.AddRange(secOrder);
                                                    storedSecondaryElement.AddRange(secOrder);
                                                    _dictReorder.Add(o, storedSecondaryElement);
                                                    o++;
                                                    dictSecondaryElementKick.Remove(dictSecondaryElementKick.FirstOrDefault(kvp => kvp.Value == sec).Key);
                                                    if (dictSecondaryElementKick.Count == 1)
                                                    {
                                                        storedSecondaryElement = new List<Element>();
                                                        orderXYZ = new Dictionary<XYZ, Element>();
                                                        foreach (Element secele in dictSecondaryElementKick.Values.FirstOrDefault())
                                                        {
                                                            XYZ xyz = (((secele.Location as LocationCurve).Curve) as Line).Origin;
                                                            orderXYZ.Add(xyz, secele);
                                                        }
                                                        orderXYZ = orderXYZ.OrderByDescending(kvp => kvp.Key.Y).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                        secOrder = orderXYZ.Select(x => x.Value).ToList();
                                                        _secondKickGroup.AddRange(secOrder);
                                                        storedSecondaryElement.AddRange(secOrder);
                                                        _dictReorder.Add(o, storedSecondaryElement);
                                                        dictSecondaryElementKick.Clear();
                                                    }
                                                }
                                                List<Element> pri = sortedGroupPrimary.Where(kvp => kvp.Value.Any(x => x == highestElevation.FirstOrDefault())).Select(kvp => kvp.Value).FirstOrDefault();

                                                /*List<Line> previousLine = new List<Line>();
                                                bool isReverseDone = false;
                                                for (int z = 0; z < sec.Count; z++)
                                                {
                                                    ConnectorSet PrimaryConnectorsMulti = Utility.GetConnectorSet(pri[z]);
                                                    ConnectorSet SecondaryConnectors = Utility.GetConnectorSet(sec[z]);
                                                    Utility.GetClosestConnectors(PrimaryConnectorsMulti, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                                    Line checkline = Line.CreateBound(Utility.GetXYvalue(ConnectorOne.Origin), Utility.GetXYvalue(ConnectorTwo.Origin));
                                                    doc.Create.NewDetailCurve(doc.ActiveView, checkline);
                                                    foreach (Line pl in previousLine)
                                                    {
                                                        if (Utility.GetIntersection(pl, checkline) != null)
                                                        {
                                                            pri.Reverse();
                                                            isReverseDone = true;
                                                            break;
                                                        }
                                                    }
                                                    if (isReverseDone)
                                                        break;
                                                    previousLine.Add(checkline);
                                                }*/

                                                _firstKickGroup.AddRange(pri);
                                                sortedGroupPrimary.Remove(sortedGroupPrimary.FirstOrDefault(kvp => kvp.Value == pri).Key);
                                                if (sortedGroupPrimary.Count == 1)
                                                {
                                                    _firstKickGroup.AddRange(sortedGroupPrimary.Values.FirstOrDefault());
                                                    sortedGroupPrimary.Clear();
                                                }
                                            }
                                            while (sortedGroupPrimary.Count > 1 && sortedGroupPrimary.Count == dictSecondaryElementKick.Count);
                                            if (elementsList is Conduit && elementsList != null)
                                            {
                                                XYZ xyz = ((elementsList.Location as LocationCurve).Curve as Line).Direction;
                                                if (xyz.Z == 1)
                                                {
                                                    using (SubTransaction subReorder = new SubTransaction(doc))
                                                    {
                                                        subReorder.Start();
                                                        //90 Kick Far
                                                        isfar = true;
                                                        ApplyKick(doc, _uiapp, _firstKickGroup, _secondKickGroup, offsetVariable);
                                                        subReorder.Commit();
                                                    }
                                                }
                                                else
                                                {
                                                    using (SubTransaction subReorder = new SubTransaction(doc))
                                                    {
                                                        subReorder.Start();
                                                        //90 Kick Near
                                                        isfar = false;
                                                        ApplyKick(doc, _uiapp, _secondKickGroup, _firstKickGroup, offsetVariable);
                                                        subReorder.Commit();
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            #region kick Order Method
                                            foreach (KeyValuePair<double, List<Element>> pair in groupPrimary)
                                            {
                                                GroupedPrimaryElement.AddRange(pair.Value);
                                            }
                                            //Order the Secondary Conduits
                                            List<XYZ> xyzPS = multiordertheSecondaryElements.Select(x => x.Key).ToList();
                                            List<XYZ> roundOFF = new List<XYZ>();
                                            foreach (var xyz in xyzPS)
                                            {
                                                XYZ roundedXYZ = new XYZ(Math.Round(xyz.X, 5), Math.Round(xyz.Y, 5), Math.Round(xyz.Z, 5));
                                                roundOFF.Add(roundedXYZ);
                                            }
                                            bool hasDuplicateY = HasDuplicateYCoordinates(roundOFF);
                                            bool hasDuplicateX = HasDuplicateXCoordinates(roundOFF);
                                            Dictionary<double, List<Element>> dictSecondaryElementKick = new Dictionary<double, List<Element>>();
                                            if (hasDuplicateY || hasDuplicateX)
                                            {
                                                _previousXYZ = null;
                                                int i = 0;
                                                do
                                                {
                                                    List<XYZ> xyzListPrimary = new List<XYZ>();
                                                    List<XYZ> xyzListSecondary = new List<XYZ>();
                                                    xyzListSecondary.AddRange(multiordertheSecondaryElements.Select(x => x.Key));
                                                    List<Element> Sele = FindCornerConduitsKick(multiordertheSecondaryElements, xyzListSecondary, doc, isangledVerticalConduits, primaryelementCount);
                                                    dictSecondaryElementKick.Add(i, Sele);
                                                    GroupedSecondaryElement.AddRange(Sele);
                                                    i++;
                                                    multiordertheSecondaryElements = multiordertheSecondaryElements.Where(kvp => !GroupedSecondaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                }
                                                while (multiordertheSecondaryElements.Count > 0);
                                            }
                                            else
                                            {
                                                isangledVerticalConduits = true;
                                                _previousXYZ = null;
                                                int i = 0;
                                                do
                                                {
                                                    List<XYZ> xyzListPrimary = new List<XYZ>();
                                                    List<XYZ> xyzListSecondary = new List<XYZ>();
                                                    xyzListSecondary.AddRange(multiordertheSecondaryElements.Select(x => x.Key));
                                                    List<Element> Sele = FindCornerConduitsKick(multiordertheSecondaryElements, xyzListSecondary, doc, isangledVerticalConduits, primaryelementCount);
                                                    dictSecondaryElementKick.Add(i, Sele);
                                                    GroupedSecondaryElement.AddRange(Sele);
                                                    i++;
                                                    multiordertheSecondaryElements = multiordertheSecondaryElements.Where(kvp => !GroupedSecondaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                }
                                                while (multiordertheSecondaryElements.Count > 0);
                                            }
                                            //Order the Primary Conduits 
                                            List<Line> previousLine = new List<Line>();
                                            bool isReverseDone = false;
                                            for (int z = 0; z < GroupedSecondaryElement.Count; z++)
                                            {
                                                ConnectorSet PrimaryConnectors = Utility.GetConnectorSet(GroupedPrimaryElement[z]);
                                                ConnectorSet SecondaryConnectors = Utility.GetConnectorSet(GroupedSecondaryElement[z]);
                                                Utility.GetClosestConnectors(PrimaryConnectors, SecondaryConnectors, out Connector ConnectorOne, out Connector ConnectorTwo);
                                                Line checkline = Line.CreateBound(Utility.GetXYvalue(ConnectorOne.Origin), Utility.GetXYvalue(ConnectorTwo.Origin));
                                                foreach (Line pl in previousLine)
                                                {
                                                    if (Utility.GetIntersection(pl, checkline) != null)
                                                    {
                                                        GroupedPrimaryElement.Reverse();
                                                        isReverseDone = true;
                                                        break;
                                                    }
                                                }
                                                if (isReverseDone)
                                                    break;
                                                previousLine.Add(checkline);
                                            }
                                            if (elementsList is Conduit && elementsList != null)
                                            {
                                                XYZ xyz = ((elementsList.Location as LocationCurve).Curve as Line).Direction;
                                                if (xyz.Z == 1)
                                                {
                                                    //90 Kick Far
                                                    isfar = true;
                                                    ApplyKick(doc, _uiapp, GroupedPrimaryElement, GroupedSecondaryElement, offsetVariable);
                                                }
                                                else
                                                {
                                                    //90 Kick Near
                                                    isfar = false;
                                                    ApplyKick(doc, _uiapp, GroupedSecondaryElement, GroupedPrimaryElement, offsetVariable);
                                                }
                                            }
                                            #endregion
                                        }
                                        if (!MainWindow.Instance.isStaticTool)
                                        {
                                            MainWindow.Instance.Close();
                                            ExternalApplication.window = null;
                                            SelectedElements.Clear();
                                            uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                            isoffsetwindowClose = true;
                                        }
                                        else
                                        {
                                            isOffsetTool = true;
                                            SelectedElements.Clear();
                                            uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                        }
                                    }
                                    else
                                    {
                                        MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Visible;
                                        isOffsetTool = false;
                                        iswindowClose = false;
                                    }
                                }
                                transaction.Commit();
                            }
                        }
                        //HORIZONTAL VERTICAL ROLLING CONNECT
                        else
                        {
                            Dictionary<double, List<Element>> groupPrimary = GroupByElementsWithElevation(CongridDictionary1.First().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                            Dictionary<double, List<Element>> groupSecondary = GroupByElementsWithElevation(CongridDictionary1.Last().Value.Select(x => x.Conduit).ToList(), offsetVariable);
                            foreach (var elem in groupPrimary)
                            {
                                foreach (var elem2 in elem.Value)
                                {
                                    DistanceElements.Add(elem2);
                                }
                            }
                            if (groupPrimary.Select(x => x.Value).ToList().FirstOrDefault().Count == groupSecondary.Select(x => x.Value).ToList().FirstOrDefault().Count)
                            {
                                for (int i = 0; i < groupPrimary.Count; i++)
                                {
                                    //using Transaction trsn = new Transaction(doc);
                                    //trsn.Start("MultiTrimConnect");
                                    List<Element> primarySortedElementspre = SortbyPlane(doc, groupPrimary.ElementAt(i).Value);
                                    List<Element> secondarySortedElementspre = SortbyPlane(doc, groupSecondary.ElementAt(i).Value);
                                    bool isNotStaright = ReverseingConduits(doc, ref primarySortedElementspre, ref secondarySortedElementspre);
                                    //defind the primary and secondary sets 
                                    double conduitlengthone = primarySortedElementspre[0].LookupParameter("Length").AsDouble();
                                    double conduitlengthsecond = secondarySortedElementspre[0].LookupParameter("Length").AsDouble();

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
                                    // trsn.Commit();
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
                                        Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                        Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                        Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                        Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);
                                        if (!isSamDir)
                                        {
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
                                            /* System.Windows.MessageBox.Show("Warning. \n" + "Please use Multi Connect tool for the selected group of conduits to connect", "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                                             successful = false;
                                             return;*/
                                            using (Transaction transMulti = new Transaction(doc))
                                            {
                                                transMulti.Start("MultiConnect");
                                                if (MainWindow.Instance.tgleAngleAcute.Visibility == System.Windows.Visibility.Visible)
                                                {
                                                    MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Collapsed;
                                                    iswhenReloadTool = true;
                                                    isOffsetTool = true;
                                                    isoffsetwindowClose = true;
                                                }
                                                for (int j = 0; j < primarySortedElements.Count; j++)
                                                {
                                                    Utility.CreateElbowFittings(primarySortedElements[j], secondarySortedElements[j], doc, _uiapp);
                                                }
                                                transMulti.Commit();
                                            }
                                        }
                                        else if ((isSamDir && Math.Round(groupPrimary.ElementAt(i).Key, 4) == Math.Round(groupSecondary.ElementAt(i).Key, 4))
                                            || (isSamDir && Math.Round(priFirst.GetEndPoint(0).Z, 3) != Math.Round(priFirst.GetEndPoint(1).Z, 3)))
                                        {
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
                                                if (MainWindow.Instance.tgleAngleAcute.Visibility == System.Windows.Visibility.Visible)
                                                {
                                                    MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Collapsed;
                                                    iswhenReloadTool = true;
                                                    iswindowClose = true;
                                                }
                                                //Extend
                                                SelectedElements.Clear();
                                                uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                                            }
                                            else
                                            {

                                                LocationCurve l1 = primarySortedElements[0].Location as LocationCurve;
                                                LocationCurve l2 = secondarySortedElements[0].Location as LocationCurve;
                                                XYZ sp = Utility.GetXYvalue(l1.Curve.GetEndPoint(0));
                                                XYZ ep = Utility.GetXYvalue(l2.Curve.GetEndPoint(1));
                                                if (Math.Round(l1.Curve.GetEndPoint(0).Z, 4) != Math.Round(l1.Curve.GetEndPoint(1).Z, 4) &&
                                                   Math.Round(l2.Curve.GetEndPoint(0).Z, 4) != Math.Round(l2.Curve.GetEndPoint(1).Z, 4))
                                                {
                                                    List<Element> primaryOrderElements = new List<Element>();
                                                    List<Element> secondaryOrderElements = new List<Element>();
                                                    Dictionary<XYZ, Element> multiorderthePrimaryElements = new Dictionary<XYZ, Element>();
                                                    Dictionary<XYZ, Element> multiordertheSecondaryElements = new Dictionary<XYZ, Element>();
                                                    foreach (Element element in primarySortedElements)
                                                    {
                                                        XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                                        multiorderthePrimaryElements.Add(xyzPelement, element);
                                                    }
                                                    multiorderthePrimaryElements = multiorderthePrimaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                    foreach (Element element in secondarySortedElements)
                                                    {
                                                        XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                                        multiordertheSecondaryElements.Add(xyzPelement, element);
                                                    }
                                                    multiordertheSecondaryElements = multiordertheSecondaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                    foreach (KeyValuePair<XYZ, Element> pair in multiorderthePrimaryElements)
                                                    {
                                                        primaryOrderElements.Add(pair.Value);
                                                    }
                                                    foreach (KeyValuePair<XYZ, Element> pair in multiordertheSecondaryElements)
                                                    {
                                                        secondaryOrderElements.Add(pair.Value);
                                                    }
                                                    //Grouping Logic
                                                    List<Element> GroupedPrimaryElement = new List<Element>();
                                                    List<Element> GroupedSecondaryElement = new List<Element>();

                                                    if (multiorderthePrimaryElements.Count > 1 && multiordertheSecondaryElements.Count > 1)
                                                    {
                                                        //using (Transaction trans = new Transaction(doc))
                                                        //{
                                                        //    trans.Start("CornerGroup");

                                                        List<XYZ> xyzPS = multiorderthePrimaryElements.Select(x => x.Key).ToList();
                                                        List<XYZ> roundOFF = new List<XYZ>();
                                                        foreach (var xyz in xyzPS)
                                                        {
                                                            XYZ roundedXYZ = new XYZ(Math.Round(xyz.X, 5), Math.Round(xyz.Y, 5), Math.Round(xyz.Z, 5));
                                                            roundOFF.Add(roundedXYZ);
                                                        }
                                                        bool hasDuplicateY = HasDuplicateYCoordinates(roundOFF);
                                                        int s = 0;
                                                        int verticalLayerCount = 0;
                                                        _previousXYZ = null;
                                                        int d = 0;
                                                        Dictionary<int, List<Element>> reversePriElements = new Dictionary<int, List<Element>>();
                                                        List<Element> previousListofElement = new List<Element>();
                                                        do
                                                        {
                                                            List<XYZ> xyzListPrimary = new List<XYZ>();
                                                            List<XYZ> xyzListSecondary = new List<XYZ>();
                                                            xyzListPrimary.AddRange(multiorderthePrimaryElements.Select(x => x.Key));
                                                            List<Element> Pele = FindCornerConduitsInclinedVerticalConduits(multiorderthePrimaryElements, xyzListPrimary, doc, verticalLayerCount, previousListofElement);
                                                            previousListofElement = (Pele);
                                                            reversePriElements.Add(d, Pele);
                                                            d++;
                                                            GroupedPrimaryElement.AddRange(Pele);
                                                            if (s == 0)
                                                            {
                                                                verticalLayerCount = GroupedPrimaryElement.Count;
                                                            }
                                                            s++;
                                                            multiorderthePrimaryElements = multiorderthePrimaryElements.Where(kvp => !GroupedPrimaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                               .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                            multiorderthePrimaryElements = multiorderthePrimaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                            if (multiorderthePrimaryElements.Count == 1)
                                                            {
                                                                GroupedPrimaryElement.Add(multiorderthePrimaryElements.FirstOrDefault().Value);
                                                                reversePriElements.Add(d, multiorderthePrimaryElements.Values.ToList());
                                                                multiorderthePrimaryElements.Clear();
                                                            }
                                                        }
                                                        while (multiorderthePrimaryElements.Count > 0);
                                                        _previousXYZ = null;
                                                        previousListofElement = new List<Element>();
                                                        int c = 0;
                                                        Dictionary<int, List<Element>> reverseSecElements = new Dictionary<int, List<Element>>();
                                                        do
                                                        {
                                                            List<XYZ> xyzListPrimary = new List<XYZ>();
                                                            List<XYZ> xyzListSecondary = new List<XYZ>();
                                                            xyzListSecondary.AddRange(multiordertheSecondaryElements.Select(x => x.Key));
                                                            List<Element> Sele = FindCornerConduitsInclinedVerticalConduits(multiordertheSecondaryElements, xyzListSecondary, doc, verticalLayerCount, previousListofElement);
                                                            reverseSecElements.Add(c, Sele);
                                                            c++;
                                                            previousListofElement = (Sele);
                                                            GroupedSecondaryElement.AddRange(Sele);
                                                            multiordertheSecondaryElements = multiordertheSecondaryElements.Where(kvp => !GroupedSecondaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                           .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                            multiordertheSecondaryElements = multiordertheSecondaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                            if (multiordertheSecondaryElements.Count == 1)
                                                            {
                                                                GroupedSecondaryElement.Add(multiordertheSecondaryElements.FirstOrDefault().Value);
                                                                reverseSecElements.Add(c, multiordertheSecondaryElements.Values.ToList());
                                                                multiordertheSecondaryElements.Clear();
                                                            }
                                                        }
                                                        while (multiordertheSecondaryElements.Count > 0);
                                                        if (reverseSecElements.Count == 2 && reversePriElements.Count == 2)
                                                        {
                                                            bool isReverse = false;
                                                            List<Line> previousLines = new List<Line>();
                                                            for (int b = 0; b < reverseSecElements.Values.Count; b++)
                                                            {
                                                                Element priEle = reversePriElements[b].Cast<Element>().ToList().FirstOrDefault();
                                                                Element secEle = reverseSecElements[b].Cast<Element>().ToList().FirstOrDefault();
                                                                XYZ priOriginXYZ = Utility.GetXYvalue(Utility.GetLineFromConduit(priEle).Origin);
                                                                XYZ secOriginXYZ = Utility.GetXYvalue(Utility.GetLineFromConduit(secEle).Origin);
                                                                Line prisecLine = Line.CreateBound(priOriginXYZ, secOriginXYZ);
                                                                foreach (Line pl in previousLines)
                                                                {
                                                                    if (Utility.GetIntersection(pl, prisecLine) != null)
                                                                    {
                                                                        GroupedSecondaryElement = new List<Element>();
                                                                        reverseSecElements = reverseSecElements.OrderByDescending(kvp => kvp.Key).ToDictionary(x => x.Key, x => x.Value);
                                                                        foreach (KeyValuePair<int, List<Element>> kvp in reverseSecElements)
                                                                        {
                                                                            GroupedSecondaryElement.AddRange(kvp.Value);
                                                                        }
                                                                        isReverse = true;
                                                                        break;
                                                                    }
                                                                }
                                                                if (isReverse)
                                                                    break;
                                                                previousLines.Add(prisecLine);
                                                            }
                                                        }
                                                        //    trans.Commit();
                                                        //}
                                                        HoffsetExecute(_uiapp, ref GroupedPrimaryElement, ref GroupedSecondaryElement);
                                                        isOffsetTool = true;
                                                        isoffsetwindowClose = true;
                                                    }
                                                    else
                                                    {
                                                        HoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                                        isOffsetTool = true;
                                                        isoffsetwindowClose = true;
                                                    }
                                                }
                                                else
                                                {
                                                    HoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                                    isOffsetTool = true;
                                                    isoffsetwindowClose = true;
                                                }

                                            }
                                        }
                                        else
                                        {
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
                                                if (MainWindow.Instance.isoffset)
                                                {
                                                    //kick
                                                    KickExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, i);
                                                    isOffsetTool = true;
                                                    isoffsetwindowClose = true;
                                                }
                                                else
                                                {
                                                    MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Visible;
                                                    isOffsetTool = false;
                                                    iswindowClose = false;
                                                    break;
                                                }
                                            }
                                            else if (isSamDirecheckline)
                                            {
                                                if (MainWindow.Instance.isoffset)
                                                {
                                                    //Voffset
                                                    VoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                                    isOffsetTool = true;
                                                    isoffsetwindowClose = true;
                                                }
                                                else
                                                {
                                                    MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Visible;
                                                    isOffsetTool = false;
                                                    iswindowClose = false;
                                                    break;
                                                }
                                            }
                                            else
                                            {
                                                if (MainWindow.Instance.isoffset)
                                                {
                                                    //Roffset
                                                    try
                                                    {

                                                        List<ElementId> unwantedids = RoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements, "Left-Down");
                                                        isOffsetTool = true;
                                                        isoffsetwindowClose = true;

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
                                                        unusedfittings = unusedfittings.Where(x => (x as FamilyInstance).MEPModel.ConnectorManager.UnusedConnectors.Size == 2).ToList();
                                                        doc.Delete(unusedfittings.Select(r => r.Id).ToList());

                                                        successful = true;


                                                        Utility.ApplySync(primarySortedElements, _uiapp);

                                                    }
                                                    catch
                                                    {

                                                    }
                                                    isOffsetTool = true;
                                                    isoffsetwindowClose = true;
                                                }
                                                else
                                                {
                                                    MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Visible;
                                                    isOffsetTool = false;
                                                    iswindowClose = false;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else if (!groupPrimary.All(X => X.Value.TrueForAll(Y => Y.LookupParameter("Reference Level").AsElementId() == groupPrimary.FirstOrDefault().Value.FirstOrDefault().LookupParameter("Reference Level").AsElementId()))
                                                                                        || !groupSecondary.All(X => X.Value.TrueForAll(Y => Y.LookupParameter("Reference Level").AsElementId() == groupSecondary.FirstOrDefault().Value.FirstOrDefault().LookupParameter("Reference Level").AsElementId())))
                            {
                                System.Windows.MessageBox.Show("Conduits have different reference level", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                                SelectedElements.Clear();
                                uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                            }
                            else
                            {
                                if (CongridDictionary1.First().Value.Count == CongridDictionary1.Last().Value.Count)
                                {
                                    if (MainWindow.Instance.isoffset)
                                    {
                                        LocationCurve l1 = CongridDictionary1.First().Value.Select(x => x.Conduit).ToList()[0].Location as LocationCurve;
                                        LocationCurve l2 = CongridDictionary1.Last().Value.Select(x => x.Conduit).ToList()[0].Location as LocationCurve;
                                        XYZ sp = Utility.GetXYvalue(l1.Curve.GetEndPoint(0));
                                        XYZ ep = Utility.GetXYvalue(l2.Curve.GetEndPoint(1));
                                        if (Math.Round(l1.Curve.GetEndPoint(0).Z, 4) != Math.Round(l1.Curve.GetEndPoint(1).Z, 4) &&
                                           Math.Round(l2.Curve.GetEndPoint(0).Z, 4) != Math.Round(l2.Curve.GetEndPoint(1).Z, 4))
                                        {
                                            List<Element> primaryOrderElements = new List<Element>();
                                            List<Element> secondaryOrderElements = new List<Element>();
                                            Dictionary<XYZ, Element> multiorderthePrimaryElements = new Dictionary<XYZ, Element>();
                                            Dictionary<XYZ, Element> multiordertheSecondaryElements = new Dictionary<XYZ, Element>();
                                            foreach (Element element in CongridDictionary1.First().Value.Select(x => x.Conduit).ToList())
                                            {
                                                XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                                multiorderthePrimaryElements.Add(xyzPelement, element);
                                            }
                                            multiorderthePrimaryElements = multiorderthePrimaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                            foreach (Element element in CongridDictionary1.Last().Value.Select(x => x.Conduit).ToList())
                                            {
                                                XYZ xyzPelement = ((element.Location as LocationCurve).Curve as Line).Origin;
                                                multiordertheSecondaryElements.Add(xyzPelement, element);
                                            }
                                            multiordertheSecondaryElements = multiordertheSecondaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                            foreach (KeyValuePair<XYZ, Element> pair in multiorderthePrimaryElements)
                                            {
                                                primaryOrderElements.Add(pair.Value);
                                            }
                                            foreach (KeyValuePair<XYZ, Element> pair in multiordertheSecondaryElements)
                                            {
                                                secondaryOrderElements.Add(pair.Value);
                                            }
                                            //Grouping Logic
                                            List<Element> GroupedPrimaryElement = new List<Element>();
                                            List<Element> GroupedSecondaryElement = new List<Element>();

                                            if (multiorderthePrimaryElements.Count > 1 && multiordertheSecondaryElements.Count > 1)
                                            {
                                                //using (Transaction trans = new Transaction(doc))
                                                //{
                                                //    trans.Start("CornerGroup");

                                                List<XYZ> xyzPS = multiorderthePrimaryElements.Select(x => x.Key).ToList();
                                                List<XYZ> roundOFF = new List<XYZ>();
                                                foreach (var xyz in xyzPS)
                                                {
                                                    XYZ roundedXYZ = new XYZ(Math.Round(xyz.X, 5), Math.Round(xyz.Y, 5), Math.Round(xyz.Z, 5));
                                                    roundOFF.Add(roundedXYZ);
                                                }
                                                bool hasDuplicateY = HasDuplicateYCoordinates(roundOFF);
                                                int s = 0;
                                                int verticalLayerCount = 0;
                                                _previousXYZ = null;
                                                int d = 0;
                                                Dictionary<int, List<Element>> reversePriElements = new Dictionary<int, List<Element>>();
                                                List<Element> previousListofElement = new List<Element>();
                                                do
                                                {
                                                    List<XYZ> xyzListPrimary = new List<XYZ>();
                                                    List<XYZ> xyzListSecondary = new List<XYZ>();
                                                    xyzListPrimary.AddRange(multiorderthePrimaryElements.Select(x => x.Key));
                                                    List<Element> Pele = FindCornerConduitsInclinedVerticalConduits(multiorderthePrimaryElements, xyzListPrimary, doc, verticalLayerCount, previousListofElement);
                                                    previousListofElement = (Pele);
                                                    reversePriElements.Add(d, Pele);
                                                    d++;
                                                    GroupedPrimaryElement.AddRange(Pele);
                                                    if (s == 0)
                                                    {
                                                        verticalLayerCount = GroupedPrimaryElement.Count;
                                                    }
                                                    s++;
                                                    multiorderthePrimaryElements = multiorderthePrimaryElements.Where(kvp => !GroupedPrimaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                       .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                    multiorderthePrimaryElements = multiorderthePrimaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                    if (multiorderthePrimaryElements.Count == 1)
                                                    {
                                                        GroupedPrimaryElement.Add(multiorderthePrimaryElements.FirstOrDefault().Value);
                                                        reversePriElements.Add(d, multiorderthePrimaryElements.Values.ToList());
                                                        multiorderthePrimaryElements.Clear();
                                                    }
                                                }
                                                while (multiorderthePrimaryElements.Count > 0);
                                                _previousXYZ = null;
                                                previousListofElement = new List<Element>();
                                                int c = 0;
                                                Dictionary<int, List<Element>> reverseSecElements = new Dictionary<int, List<Element>>();
                                                do
                                                {
                                                    List<XYZ> xyzListPrimary = new List<XYZ>();
                                                    List<XYZ> xyzListSecondary = new List<XYZ>();
                                                    xyzListSecondary.AddRange(multiordertheSecondaryElements.Select(x => x.Key));
                                                    List<Element> Sele = FindCornerConduitsInclinedVerticalConduits(multiordertheSecondaryElements, xyzListSecondary, doc, verticalLayerCount, previousListofElement);
                                                    reverseSecElements.Add(c, Sele);
                                                    c++;
                                                    previousListofElement = (Sele);
                                                    GroupedSecondaryElement.AddRange(Sele);
                                                    multiordertheSecondaryElements = multiordertheSecondaryElements.Where(kvp => !GroupedSecondaryElement.Any(e => e.Id == kvp.Value.Id))
                                                                                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                    multiordertheSecondaryElements = multiordertheSecondaryElements.OrderBy(kvp => kvp.Key, new XYZComparer()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                                                    if (multiordertheSecondaryElements.Count == 1)
                                                    {
                                                        GroupedSecondaryElement.Add(multiordertheSecondaryElements.FirstOrDefault().Value);
                                                        reverseSecElements.Add(c, multiordertheSecondaryElements.Values.ToList());
                                                        multiordertheSecondaryElements.Clear();
                                                    }
                                                }
                                                while (multiordertheSecondaryElements.Count > 0);
                                                if (reverseSecElements.Count == 2 && reversePriElements.Count == 2)
                                                {
                                                    bool isReverse = false;
                                                    List<Line> previousLines = new List<Line>();
                                                    for (int b = 0; b < reverseSecElements.Values.Count; b++)
                                                    {
                                                        Element priEle = reversePriElements[b].Cast<Element>().ToList().FirstOrDefault();
                                                        Element secEle = reverseSecElements[b].Cast<Element>().ToList().FirstOrDefault();
                                                        XYZ priOriginXYZ = Utility.GetXYvalue(Utility.GetLineFromConduit(priEle).Origin);
                                                        XYZ secOriginXYZ = Utility.GetXYvalue(Utility.GetLineFromConduit(secEle).Origin);
                                                        Line prisecLine = Line.CreateBound(priOriginXYZ, secOriginXYZ);
                                                        foreach (Line pl in previousLines)
                                                        {
                                                            if (Utility.GetIntersection(pl, prisecLine) != null)
                                                            {
                                                                GroupedSecondaryElement = new List<Element>();
                                                                reverseSecElements = reverseSecElements.OrderByDescending(kvp => kvp.Key).ToDictionary(x => x.Key, x => x.Value);
                                                                foreach (KeyValuePair<int, List<Element>> kvp in reverseSecElements)
                                                                {
                                                                    GroupedSecondaryElement.AddRange(kvp.Value);
                                                                }
                                                                isReverse = true;
                                                                break;
                                                            }
                                                        }
                                                        if (isReverse)
                                                            break;
                                                        previousLines.Add(prisecLine);
                                                    }
                                                }
                                                //    trans.Commit();
                                                //}
                                                HoffsetExecute(_uiapp, ref GroupedPrimaryElement, ref GroupedSecondaryElement);
                                                isOffsetTool = true;
                                                isoffsetwindowClose = true;
                                            }
                                            else
                                            {
                                                HoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                                isOffsetTool = true;
                                                isoffsetwindowClose = true;
                                            }
                                        }                                        
                                    }
                                    else
                                    {
                                        MainWindow.Instance.tgleAngleAcute.Visibility = System.Windows.Visibility.Visible;
                                        isOffsetTool = false;
                                        iswindowClose = false;
                                    }
                                }
                                else
                                {
                                    System.Windows.MessageBox.Show("Please select equal number of conduits", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    isoffsetwindowClose = true;
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
                                        bool isVerticalConduits = false;
                                        Line priFirst = ((primaryFirst.Location as LocationCurve).Curve as Line);
                                        Line priLast = ((primaryLast.Location as LocationCurve).Curve as Line);
                                        Line secFirst = ((secondaryFirst.Location as LocationCurve).Curve as Line);
                                        Line secLast = ((secondaryLast.Location as LocationCurve).Curve as Line);
                                        XYZ directionOne = priFirst.Direction;
                                        XYZ directionTwo = secFirst.Direction;
                                        isVerticalConduits = new XYZ(0, 0, Math.Abs(directionOne.Z)).IsAlmostEqualTo(XYZ.BasisZ)
                                            && new XYZ(0, 0, Math.Abs(directionTwo.Z)).IsAlmostEqualTo(XYZ.BasisZ);

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

                                        if ((priSlope == -1 && SecSlope == 0) ||
                                            Math.Round((Math.Round(priSlope, 5)) * (Math.Round(SecSlope, 5)), 4) == -1 ||
                                            Math.Round((Math.Round(priSlope, 5)) * (Math.Round(SecSlope, 5)), 4).ToString() == double.NaN.ToString() && !isVerticalConduits)
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

                                            //Hoffset //else if
                                            HoffsetExecute(_uiapp, ref primarySortedElements, ref secondarySortedElements);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    SelectedElements.Clear();
                    uidoc.Selection.SetElementIds(new List<ElementId> { ElementId.InvalidElementId });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

            }
            finally
            {
                MainWindow.Instance.isoffset = false;
                otherConduit = null;
                isCatch = false;
                _dictReorder = new Dictionary<int, List<Element>>();
                _dictReorderStub = new Dictionary<int, List<Element>>();
                isStubCreate = true;

                _AscendingElementwithPositiveAngle = false;
                _DescendingElementwithPositiveAngle = false;
                _AscendingElementwithNegativeAngle = false;
                _DescendingElementwithNegativeAngle = false;
            }
        }

        public static Line GetParallelLine(Element firstElement, Element secondElement, ref bool isVerticalConduits, Document doc)
        {
            Line line = (firstElement.Location as LocationCurve).Curve as Line;
            Line line2 = (secondElement.Location as LocationCurve).Curve as Line;
            ConnectorSet connectors = Utility.GetConnectors(firstElement);
            ConnectorSet connectors2 = Utility.GetConnectors(secondElement);
            Utility.GetClosestConnectors(connectors, connectors2, out var ConnectorOne, out var ConnectorTwo);
            List<XYZ> list = new List<XYZ>();
            foreach (Connector item in connectors)
            {
                list.Add(item.Origin);
            }

            if (Utility.IsXYTrue(list.FirstOrDefault(), list.LastOrDefault()))
            {
                isVerticalConduits = true;
            }

            if (!isVerticalConduits)
            {
                XYZ direction = line2.Direction;
                XYZ xYZ = direction.CrossProduct(XYZ.BasisZ);
                Line line3 = Line.CreateBound(ConnectorTwo.Origin, ConnectorTwo.Origin + xYZ.Multiply(ConnectorOne.Origin.DistanceTo(ConnectorTwo.Origin)));
                XYZ xYZ2 = Utility.FindIntersectionPoint(line.GetEndPoint(0), line.GetEndPoint(1), line3.GetEndPoint(0), line3.GetEndPoint(1));
                XYZ endpoint = new XYZ(xYZ2.X, xYZ2.Y, ConnectorOne.Origin.Z);
                return Line.CreateBound(ConnectorOne.Origin, endpoint);
            }
            Line line4 = Line.CreateBound(ConnectorTwo.Origin, new XYZ(ConnectorOne.Origin.X, ConnectorOne.Origin.Y, ConnectorTwo.Origin.Z));
            XYZ xYZ3 = FindIntersectionPoint(line.GetEndPoint(0), line.GetEndPoint(1), line4.GetEndPoint(0), line4.GetEndPoint(1));
            if (xYZ3 == null)
            {
                xYZ3 = new XYZ(ConnectorOne.Origin.X, ConnectorOne.Origin.Y, 0);
            }
            XYZ endpoint2 = xYZ3 != null ? new XYZ(xYZ3.X, xYZ3.Y, ConnectorTwo.Origin.Z) : new XYZ(0, 0, ConnectorTwo.Origin.Z);
            return Line.CreateBound(ConnectorOne.Origin, endpoint2);
        }
        public static XYZ FindIntersectionPoint(XYZ s1, XYZ e1, XYZ s2, XYZ e2, int roundOff = 0)
        {
            if (roundOff > 0)
            {
                s1 = XYZroundOf(s1, roundOff);
                e1 = XYZroundOf(e1, roundOff);
                s2 = XYZroundOf(s2, roundOff);
                e2 = XYZroundOf(e2, roundOff);
            }

            double num = e1.Y - s1.Y;
            double num2 = s1.X - e1.X;
            double num3 = num * s1.X + num2 * s1.Y;
            double num4 = e2.Y - s2.Y;
            double num5 = s2.X - e2.X;
            double num6 = num4 * s2.X + num5 * s2.Y;
            double num7 = num * num5 - num4 * num2;
            return (num7 == 0.0) ? null : new XYZ((num5 * num3 - num2 * num6) / num7, (num * num6 - num4 * num3) / num7, 0.0);
        }
        public static XYZ XYZroundOf(XYZ xyz, int digit)
        {
            return new XYZ(Math.Round(xyz.X, digit), Math.Round(xyz.Y, digit), Math.Round(xyz.Z, digit));
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
                        Line newLine = GetParallelLine(firstElement, secondElement, ref isVerticalConduits, doc);
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
                            Line newLine = GetParallelLine(firstElement, secondElement, ref isVerticalConduits, doc);
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
                                Line newLine = GetParallelLine(firstElement, secondElement, ref isVerticalConduits, doc);
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
        private static bool HasDuplicateYCoordinates(List<XYZ> points)
        {
            HashSet<double> uniqueYCoordinates = new HashSet<double>();

            foreach (var point in points)
            {
                if (!uniqueYCoordinates.Add(point.Y))
                {
                    return true;
                }
            }
            return false;
        }

        public static Dictionary<int, List<ConduitGrid>> GroupStubElements(List<Autodesk.Revit.DB.Element> ElementCollection, double maximumSpacing = 0.5)
        {
            List<ConduitGrid> list = new List<ConduitGrid>();
            List<Autodesk.Revit.DB.Element> list2 = new List<Autodesk.Revit.DB.Element>();
            foreach (Autodesk.Revit.DB.Element item2 in ElementCollection)
            {
                Line line = (item2.Location as LocationCurve).Curve as Line;
                XYZ DirectionOne = line.Direction;
                if (!list2.Any((Autodesk.Revit.DB.Element r) => Utility.IsSameDirection(((r.Location as LocationCurve).Curve as Line).Direction, DirectionOne)))
                {
                    list2.Add(item2);
                }
            }

            foreach (Autodesk.Revit.DB.Element item3 in list2)
            {
                Line line2 = (item3.Location as LocationCurve).Curve as Line;
                XYZ direction = line2.Direction;
                foreach (Autodesk.Revit.DB.Element item4 in ElementCollection)
                {
                    Line line3 = (item4.Location as LocationCurve).Curve as Line;
                    XYZ direction2 = line3.Direction;
                    if (!Utility.IsSameDirection(direction, direction2))
                    {
                        continue;
                    }

                    XYZ endPoint = line2.GetEndPoint(0);
                    XYZ endPoint2 = line2.GetEndPoint(1);

                    Line line4 = null;
                    XYZ xYZ = (endPoint + endPoint2) / 2.0;
                    XYZ xYZ2 = line2.Direction.CrossProduct(XYZ.BasisZ);
                    XYZ endpoint = xYZ + xYZ2.Multiply(100.0);
                    XYZ endpoint2 = xYZ - xYZ2.Multiply(100.0);
                    try
                    {
                        line4 = Line.CreateBound(endpoint, endpoint2);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    try
                    {
                        XYZ xYZ3 = null;
                        try
                        {
                            xYZ3 = Utility.FindIntersection(item4, line4);
                        }
                        catch
                        {

                        }
                        if (xYZ3 != null)
                        {
                            ConduitGrid item = new ConduitGrid(item4, item3, xYZ3.DistanceTo(new XYZ(xYZ.X, xYZ.Y, 0.0)));
                            list.Add(item);
                        }
                        else
                        {
                            endPoint = line2.GetEndPoint(0);
                            endPoint2 = line3.GetEndPoint(0);
                            ConduitGrid item = new ConduitGrid(item4, item3, new XYZ(endPoint.X, endPoint.Y, 0).DistanceTo(new XYZ(endPoint2.X, endPoint2.Y, 0)));
                            list.Add(item);
                        }
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine(ex2.Message);
                    }
                }
            }

            Dictionary<int, List<ConduitGrid>> CongridDictionary = new Dictionary<int, List<ConduitGrid>>();
            List<Autodesk.Revit.DB.Element> list3 = new List<Autodesk.Revit.DB.Element>();
            int index = 0;
            foreach (IGrouping<Autodesk.Revit.DB.ElementId, ConduitGrid> item5 in from r in list
                                                                                  group r by r.RefConduit.Id)
            {
                foreach (ConduitGrid congrid in item5.OrderBy((ConduitGrid x) => x.Distance))
                {
                    if (list3.Any((Autodesk.Revit.DB.Element r) => r.Id == congrid.Conduit.Id))
                    {
                        continue;
                    }

                    List<ConduitGrid> source = list.Where((ConduitGrid r) => r.RefConduit.Id == congrid.RefConduit.Id && !CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> x) => x.Value.Any((ConduitGrid y) => y.Conduit.Id == r.Conduit.Id))).ToList();
                    List<ConduitGrid> IntersectingConduits = source.Where((ConduitGrid r) => GetIntersection(r.Conduit, congrid, congrid.StartPoint, maximumSpacing) != null).ToList();
                    IntersectingConduits.AddRange(source.Where((ConduitGrid r) => !IntersectingConduits.Any((ConduitGrid x) => r.Conduit.Id == x.Conduit.Id) && GetIntersection(r.Conduit, congrid, congrid.EndPoint, maximumSpacing) != null).ToList());
                    IntersectingConduits.AddRange(source.Where((ConduitGrid r) => !IntersectingConduits.Any((ConduitGrid x) => r.Conduit.Id == x.Conduit.Id) && GetIntersection(r.Conduit, congrid, congrid.MidPoint, maximumSpacing) != null).ToList());
                    if (IntersectingConduits == null || !IntersectingConduits.Any())
                    {
                        continue;
                    }

                    if (!CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => IntersectingConduits.Any((ConduitGrid y) => y.Conduit.Id == x.Conduit.Id))))
                    {
                        foreach (ConduitGrid cg in IntersectingConduits)
                        {
                            source = list.Where((ConduitGrid r) => r.RefConduit.Id == cg.RefConduit.Id).ToList();
                            List<ConduitGrid> ISC = source.Where((ConduitGrid r) => GetIntersection(r.Conduit, cg, cg.StartPoint, maximumSpacing) != null).ToList();
                            ISC.AddRange(source.Where((ConduitGrid r) => !ISC.Any((ConduitGrid x) => r.Conduit.Id == x.Conduit.Id) && GetIntersection(r.Conduit, cg, cg.EndPoint, maximumSpacing) != null).ToList());
                            ISC.AddRange(source.Where((ConduitGrid r) => !ISC.Any((ConduitGrid x) => r.Conduit.Id == x.Conduit.Id) && GetIntersection(r.Conduit, cg, cg.MidPoint, maximumSpacing) != null).ToList());
                            if (ISC == null)
                            {
                                continue;
                            }

                            if (!CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => ISC.Any((ConduitGrid y) => y.Conduit.Id == x.Conduit.Id))))
                            {
                                ISC = ISC.Where((ConduitGrid x) => !CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid y) => x.Conduit.Id == y.Conduit.Id))).ToList();
                                if (!CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Key == index))
                                {
                                    CongridDictionary.Add(index, ISC);
                                }

                                continue;
                            }

                            KeyValuePair<int, List<ConduitGrid>> keyValuePair = CongridDictionary.FirstOrDefault((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => ISC.Any((ConduitGrid y) => y.Conduit.Id == x.Conduit.Id)));
                            List<ConduitGrid> value = keyValuePair.Value;
                            if (value == null)
                            {
                                continue;
                            }

                            foreach (ConduitGrid conGrid3 in ISC)
                            {
                                if (!value.Any((ConduitGrid r) => r.Conduit.Id == conGrid3.Conduit.Id && r.RefConduit.Id == conGrid3.RefConduit.Id) && !CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => x.Conduit.Id == conGrid3.Conduit.Id)))
                                {
                                    value.Add(conGrid3);
                                }
                            }

                            CongridDictionary[keyValuePair.Key] = value;
                        }

                        if (!CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => IntersectingConduits.Any((ConduitGrid x) => r.Value.Any((ConduitGrid y) => x.Conduit.Id == y.Conduit.Id))))
                        {
                            IntersectingConduits = IntersectingConduits.Where((ConduitGrid x) => !CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid y) => x.Conduit.Id == y.Conduit.Id))).ToList();
                            if (!CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Key == index))
                            {
                                if (!CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => IntersectingConduits.Any((ConduitGrid x) => r.Value.Any((ConduitGrid y) => x.Conduit.Id == y.RefConduit.Id || x.RefConduit.Id == y.Conduit.Id))))
                                {
                                    CongridDictionary.Add(index, IntersectingConduits);
                                }
                                else
                                {
                                    KeyValuePair<int, List<ConduitGrid>> keyValuePair2 = CongridDictionary.FirstOrDefault((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => IntersectingConduits.Any((ConduitGrid y) => y.RefConduit.Id == x.Conduit.Id)));
                                    List<ConduitGrid> value2 = keyValuePair2.Value;
                                    if (value2 != null)
                                    {
                                        foreach (ConduitGrid conGrid2 in IntersectingConduits)
                                        {
                                            if (!value2.Any((ConduitGrid r) => r.Conduit.Id == conGrid2.Conduit.Id && r.RefConduit.Id == conGrid2.RefConduit.Id) && !CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => x.Conduit.Id == conGrid2.Conduit.Id)))
                                            {
                                                value2.Add(conGrid2);
                                            }
                                        }

                                        CongridDictionary[keyValuePair2.Key] = value2;
                                    }
                                }
                            }
                        }

                        int num = index;
                        index = num + 1;
                    }
                    else
                    {
                        KeyValuePair<int, List<ConduitGrid>> keyValuePair3 = CongridDictionary.FirstOrDefault((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => IntersectingConduits.Any((ConduitGrid y) => y.Conduit.Id == x.Conduit.Id)));
                        List<ConduitGrid> value3 = keyValuePair3.Value;
                        if (value3 != null)
                        {
                            foreach (ConduitGrid conGrid in IntersectingConduits)
                            {
                                if (!value3.Any((ConduitGrid r) => r.Conduit.Id == conGrid.Conduit.Id && r.RefConduit.Id == conGrid.RefConduit.Id) && !CongridDictionary.Any((KeyValuePair<int, List<ConduitGrid>> r) => r.Value.Any((ConduitGrid x) => x.Conduit.Id == conGrid.Conduit.Id)))
                                {
                                    value3.Add(conGrid);
                                }
                            }

                            CongridDictionary[keyValuePair3.Key] = value3;
                        }
                    }

                    list3.Add(congrid.Conduit);
                }
            }
            return CongridDictionary;
        }
        public static XYZ GetIntersection(Autodesk.Revit.DB.Element element, ConduitGrid conGrid, XYZ Point, double maximumSpacing = 1.0)
        {
            try
            {
                double num = ((element.GetType() == typeof(Autodesk.Revit.DB.Electrical.Conduit)) ? element.LookupParameter("Outside Diameter").AsDouble() : element.LookupParameter("Width").AsDouble());
                double num2 = ((conGrid.Conduit.GetType() == typeof(Autodesk.Revit.DB.Electrical.Conduit)) ? conGrid.Conduit.LookupParameter("Outside Diameter").AsDouble() : conGrid.Conduit.LookupParameter("Width").AsDouble());
                double num3 = num / 2.0;
                double num4 = num2 / 2.0;
                double value = num3 + num4 + maximumSpacing;
                XYZ direction = conGrid.ConduitLine.Direction;
                XYZ xYZ = direction.CrossProduct(XYZ.BasisZ);
                XYZ endpoint = Point + xYZ.Multiply(value);
                XYZ endpoint2 = Point - xYZ.Multiply(value);
                Line line = Line.CreateBound(endpoint, endpoint2);
                Line line2 = (element.Location as LocationCurve).Curve as Line;
                XYZ endPoint = line2.GetEndPoint(0);
                XYZ endPoint2 = line2.GetEndPoint(1);
                endPoint = new XYZ(endPoint.X, endPoint.Y, 0.0);
                endPoint2 = new XYZ(endPoint2.X, endPoint2.Y, 0.0);
                Line line3 = Line.CreateBound(endPoint, endPoint2);
                return GetIntersection(line3, line);
            }
            catch
            {
                Line lineOne = (element.Location as LocationCurve).Curve as Line;
                Line lineTwo = (conGrid.Conduit.Location as LocationCurve).Curve as Line;
                XYZ endPointOne = lineOne.GetEndPoint(0);
                XYZ endPointTwo = lineTwo.GetEndPoint(0);
                double num = ((element.GetType() == typeof(Autodesk.Revit.DB.Electrical.Conduit)) ? element.LookupParameter("Outside Diameter").AsDouble() : element.LookupParameter("Width").AsDouble());
                double num2 = ((conGrid.Conduit.GetType() == typeof(Autodesk.Revit.DB.Electrical.Conduit)) ? conGrid.Conduit.LookupParameter("Outside Diameter").AsDouble() : conGrid.Conduit.LookupParameter("Width").AsDouble());
                double num3 = num / 2.0;
                double num4 = num2 / 2.0;
                double value = num3 + num4 + maximumSpacing;
                double distance = new XYZ(endPointOne.X, endPointOne.Y, 0).DistanceTo(new XYZ(endPointTwo.X, endPointTwo.Y, 0));
                if (distance <= value)
                {
                    return endPointTwo;
                }
                return null;
            }

            return null;
        }
        public static List<Element> FindCornerConduitsStub(Dictionary<XYZ, Element> multilayerdPS, List<XYZ> xyzPS, Document doc, bool isangledVerticalConduits, List<Element> primaryelementCount)
        {
            List<Element> GroupedElement = new List<Element>();
            using (SubTransaction trans = new SubTransaction(doc))
            {
                trans.Start();
                double maxDistance = 0;
                XYZ firstCorner = null;
                XYZ secondCorner = null;
                for (int a = 0; a < xyzPS.Count; a++)
                {
                    for (int j = a + 1; j < xyzPS.Count; j++)
                    {
                        double distance = xyzPS[a].DistanceTo(xyzPS[j]);
                        if (distance > maxDistance)
                        {
                            maxDistance = distance;
                            firstCorner = xyzPS[a];
                            secondCorner = xyzPS[j];
                        }
                    }
                }
                List<XYZ> remainingPoints = xyzPS.Where(p => p != firstCorner && p != secondCorner).ToList();
                List<XYZ> otherCorners = remainingPoints.OrderByDescending(p => DistanceToLine(firstCorner, secondCorner, p)).Take(2).ToList();
                List<XYZ> cornerPoints = new List<XYZ> { firstCorner, secondCorner };
                Line PCl1 = null;
                Line PCl2 = null;
                Line PCl3 = null;
                Dictionary<double, List<XYZ>> linesWithLengths = new Dictionary<double, List<XYZ>>();

                if ((Math.Round(cornerPoints[0].X, 4) != Math.Round(cornerPoints[1].X, 4)))
                {
                    if (primaryelementCount.Count != multilayerdPS.Count)
                    {
                        cornerPoints.AddRange(otherCorners);
                        List<XYZ> cornerPointsBackup = cornerPoints;

                        double commonZ = xyzPS[0].Z;
                        double minX = xyzPS.Min(p => p.X);
                        double minY = xyzPS.Min(p => p.Y);
                        double maxX = xyzPS.Max(p => p.X);
                        double maxY = xyzPS.Max(p => p.Y);
                        XYZ topLeft = new XYZ(minX, maxY, commonZ);      // (minX, maxY)
                        XYZ topRight = new XYZ(maxX, maxY, commonZ);     // (maxX, maxY)
                        XYZ bottomLeft = new XYZ(minX, minY, commonZ);   // (minX, minY)
                        XYZ bottomRight = new XYZ(maxX, minY, commonZ);  // (maxX, minY)
                        List<XYZ> _cornerPoints = new List<XYZ> { topLeft, topRight, bottomLeft, bottomRight };
                        if (_previousXYZ != null)
                        {
                            XYZ[] cp = cornerPoints.ToArray();
                            XYZ minDistanceCorner = FindMinimumDistance(_previousXYZ, cp);
                            cornerPoints = new List<XYZ> { minDistanceCorner };
                            cornerPoints.AddRange(cornerPointsBackup.Except(cornerPoints));
                        }
                        PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                     new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                        PCl2 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                         new XYZ(cornerPoints[2].X, cornerPoints[2].Y, 0));
                        PCl3 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                         new XYZ(cornerPoints[3].X, cornerPoints[3].Y, 0));
                        linesWithLengths = new Dictionary<double, List<XYZ>>
                                                       {
                                                           {PCl1.Length,new List< XYZ>() {cornerPoints[0], cornerPoints[1] } },
                                                           {PCl2.Length,new List< XYZ>() { cornerPoints[0], cornerPoints[2] }  },
                                                           {PCl3.Length,new List< XYZ>() { cornerPoints[0], cornerPoints[3] }  }
                                                       };
                        linesWithLengths = linesWithLengths.OrderBy(x => x.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        //2x2 matrix element take short line only
                        if (xyzPS.Count > 4)
                        {
                            linesWithLengths.Remove(linesWithLengths.Keys.FirstOrDefault());
                            linesWithLengths.Remove(linesWithLengths.Keys.LastOrDefault());
                        }
                        else
                        {
                            var firstEntry = linesWithLengths.GetEnumerator();
                            firstEntry.MoveNext();
                            var firstKey = firstEntry.Current.Key;
                            var firstValue = firstEntry.Current.Value;
                            linesWithLengths.Clear();
                            linesWithLengths.Add(firstKey, firstValue);

                        }
                    }
                    else
                    {
                        PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                     new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                        linesWithLengths = new Dictionary<double, List<XYZ>> { { PCl1.Length, new List<XYZ>() { cornerPoints[0], cornerPoints[1] } } };
                    }
                }
                else
                {
                    PCl1 = Line.CreateBound(new XYZ(cornerPoints[0].X, cornerPoints[0].Y, 0),
                 new XYZ(cornerPoints[1].X, cornerPoints[1].Y, 0));
                    linesWithLengths = new Dictionary<double, List<XYZ>> { { PCl1.Length, new List<XYZ>() { cornerPoints[0], cornerPoints[1] } } };
                }

                List<XYZ> XYZPoints = linesWithLengths.Select(x => x.Value).ToList().FirstOrDefault();
                List<Element> matchingElements = multilayerdPS
                                                 .Where(kvp => XYZPoints.Contains(kvp.Key))
                                                 .Select(kvp => kvp.Value)
                                                 .ToList();
                if (isangledVerticalConduits)
                {
                    XYZ midPoint1 = (((matchingElements[0].Location as LocationCurve).Curve).GetEndPoint(0) +
                ((matchingElements[0].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
                    XYZ midPoint2 = (((matchingElements[1].Location as LocationCurve).Curve).GetEndPoint(0) +
                       ((matchingElements[1].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
                    List<XYZ> midXYZs = new List<XYZ>() { midPoint1, midPoint2 };
                    double outsideDiameter1 = matchingElements[0].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
                    double outsideDiameter2 = matchingElements[1].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
                    Line connectedLine = Line.CreateBound(midXYZs[0], midXYZs[1]);
                    XYZ direction = connectedLine.Direction;
                    XYZ newXYZ1 = midXYZs[0] - direction * (outsideDiameter1 / 2);
                    XYZ newXYZ2 = midXYZs[1] + direction * (outsideDiameter2 / 2);
                    Line centerLine = Line.CreateBound(newXYZ1, newXYZ2);
                    otherConduit = Utility.CreateConduit(doc, matchingElements[0], centerLine);
                    Element midPointConduit = null;
                    List<Element> collector = multilayerdPS.Select(x => x.Value).ToList();
                    List<Element> conduitsBetween = new List<Element>();
                    conduitsBetween.Add(matchingElements[0]);
                    foreach (Element conduit in collector)
                    {
                        LocationCurve conduitCurve = conduit.Location as LocationCurve;
                        if (conduitCurve == null) continue;
                        if (conduit.Id == otherConduit.Id) continue;
                        LocationCurve otherConduitCurve = otherConduit.Location as LocationCurve;
                        if (conduit.Id != matchingElements[0].Id && conduit.Id != matchingElements[1].Id)
                        {
                            SetComparisonResult result = conduitCurve.Curve.Intersect(otherConduitCurve.Curve, out IntersectionResultArray intersectionResultArray);
                            if (result == SetComparisonResult.Overlap)
                            {
                                if (!conduitsBetween.Contains(otherConduit))
                                {
                                    conduitsBetween.Add(conduit);
                                }
                                if (midPointConduit == null || midPointConduit.Id != conduit.Id)
                                {
                                    midPointConduit = conduit;
                                }
                            }
                        }
                    }
                    conduitsBetween.Add(matchingElements[1]);
                    GroupedElement = conduitsBetween;
                    if (otherConduit != null)
                    {
                        doc.Delete(otherConduit.Id);
                    }
                }
                else
                {
                    List<XYZ> orderedPoints = CreateBoundingBoxLineKick(linesWithLengths, matchingElements, multilayerdPS, doc);
                    GroupedElement = multilayerdPS
                                                  .Where(kvp => orderedPoints.Contains(kvp.Key))
                                                  .Select(kvp => kvp.Value)
                                                  .ToList();
                    _previousXYZ = cornerPoints[0];
                }
                trans.Commit();
            }
            return GroupedElement;
        }
        public static List<XYZ> CreateBoundingBoxLineKick(Dictionary<double, List<XYZ>> ConduitconnectedLine, List<Element> twoConduits,
          Dictionary<XYZ, Element> multilayerdPS, Document doc)
        {
            List<XYZ> orderedPoints = new List<XYZ>();

            XYZ midPoint1 = (((twoConduits[0].Location as LocationCurve).Curve).GetEndPoint(0) +
                ((twoConduits[0].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
            XYZ midPoint2 = (((twoConduits[1].Location as LocationCurve).Curve).GetEndPoint(0) +
               ((twoConduits[1].Location as LocationCurve).Curve).GetEndPoint(1)) / 2;
            midPoint2 = new XYZ(midPoint2.X, midPoint2.Y, midPoint1.Z);
            List<XYZ> midXYZs = new List<XYZ>() { midPoint1, midPoint2 };


            double outsideDiameter1 = twoConduits[0].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
            double outsideDiameter2 = twoConduits[1].get_Parameter(BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM).AsDouble();
            List<XYZ> oldXYZ = ConduitconnectedLine.Select(x => x.Value).ToList().FirstOrDefault();
            //Line connectedLine = Line.CreateBound(oldXYZ[0], oldXYZ[1]);
            Line connectedLine = Line.CreateBound(midXYZs[0], midXYZs[1]);
            XYZ direction = connectedLine.Direction;
            //XYZ newXYZ1 = oldXYZ[0] - direction * (outsideDiameter1 / 2);
            XYZ newXYZ1 = midXYZs[0] - direction * (outsideDiameter1 / 2);
            //XYZ newXYZ2 = oldXYZ[1] + direction * (outsideDiameter2 / 2);
            XYZ newXYZ2 = midXYZs[1] + direction * (outsideDiameter2 / 2);
            Line centerLine = Line.CreateBound(newXYZ1, newXYZ2);
            //doc.Create.NewDetailCurve(doc.ActiveView, Line.CreateBound(newXYZ1, newXYZ2));

            XYZ normal = centerLine.Direction.CrossProduct(XYZ.BasisZ);
            XYZ origin = centerLine.GetEndPoint(0);
            Plane plane = Plane.CreateByNormalAndOrigin(normal, origin);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
            //ModelLine modelLine = doc.Create.NewModelCurve(Line.CreateBound(newXYZ1, newXYZ2), sketchPlane) as ModelLine;

            Line leftLine = Utility.CrossProductLine(connectedLine, newXYZ1, (outsideDiameter1 / 2));
            Line rightLine = Utility.CrossProductLine(connectedLine, newXYZ2, (outsideDiameter1 / 2));

            normal = leftLine.Direction.CrossProduct(XYZ.BasisZ); // Assuming Z-axis is perpendicular
            origin = leftLine.GetEndPoint(0);
            plane = Plane.CreateByNormalAndOrigin(normal, origin);
            sketchPlane = SketchPlane.Create(doc, plane);
            //modelLine = doc.Create.NewModelCurve(leftLine, sketchPlane) as ModelLine;
            normal = rightLine.Direction.CrossProduct(XYZ.BasisZ); // Assuming Z-axis is perpendicular
            origin = rightLine.GetEndPoint(0);
            plane = Plane.CreateByNormalAndOrigin(normal, origin);
            sketchPlane = SketchPlane.Create(doc, plane);
            //modelLine = doc.Create.NewModelCurve(rightLine, sketchPlane) as ModelLine;

            //doc.Create.NewDetailCurve(doc.ActiveView, leftLine);
            //doc.Create.NewDetailCurve(doc.ActiveView, rightLine);
            List<XYZ> newXYZs = new List<XYZ>() { leftLine.GetEndPoint(0),leftLine.GetEndPoint(1),
            rightLine.GetEndPoint(0),rightLine.GetEndPoint(1)};
            Line upperLine = Line.CreateBound(leftLine.GetEndPoint(0), rightLine.GetEndPoint(0));
            Line lowerLine = Line.CreateBound(leftLine.GetEndPoint(1), rightLine.GetEndPoint(1));
            //doc.Create.NewDetailCurve(doc.ActiveView, upperLine);
            //doc.Create.NewDetailCurve(doc.ActiveView, lowerLine);

            normal = upperLine.Direction.CrossProduct(XYZ.BasisZ); // Assuming Z-axis is perpendicular
            origin = upperLine.GetEndPoint(0);
            plane = Plane.CreateByNormalAndOrigin(normal, origin);
            sketchPlane = SketchPlane.Create(doc, plane);
            //modelLine = doc.Create.NewModelCurve(upperLine, sketchPlane) as ModelLine;
            normal = lowerLine.Direction.CrossProduct(XYZ.BasisZ); // Assuming Z-axis is perpendicular
            origin = lowerLine.GetEndPoint(0);
            plane = Plane.CreateByNormalAndOrigin(normal, origin);
            sketchPlane = SketchPlane.Create(doc, plane);
            //modelLine = doc.Create.NewModelCurve(lowerLine, sketchPlane) as ModelLine;

            BoundingBoxXYZ bbox = new BoundingBoxXYZ
            {
                Min = new XYZ(newXYZs.Min(p => p.X), newXYZs.Min(p => p.Y), newXYZs.Min(p => p.Z)),
                Max = new XYZ(newXYZs.Max(p => p.X), newXYZs.Max(p => p.Y), newXYZs.Max(p => p.Z))
            };

            /*double minX = newXYZs.Min(p => p.X);
            double minY = newXYZs.Min(p => p.Y);
            double minZ = newXYZs.Min(p => p.Z);
            double maxX = newXYZs.Max(p => p.X);
            double maxY = newXYZs.Max(p => p.Y);
            double maxZ = newXYZs.Max(p => p.Z);
            XYZ minXYZ = new XYZ(minX, minY, minZ);
            XYZ maxXYZ = new XYZ(maxX, maxY, maxZ);
            Line diagnolLine = Line.CreateBound(bbox.Min, bbox.Max);*/
            /*normal = diagnolLine.Direction.CrossProduct(XYZ.BasisZ); // Assuming Z-axis is perpendicular
             origin = diagnolLine.GetEndPoint(0);
             plane = Plane.CreateByNormalAndOrigin(normal, origin);
             sketchPlane = SketchPlane.Create(doc, plane);
             modelLine = doc.Create.NewModelCurve(diagnolLine, sketchPlane) as ModelLine;
             doc.Create.NewDetailCurve(doc.ActiveView, diagnolLine);*/

            orderedPoints = CollectBetweenElementByNewBoundingBoxKick(bbox.Min, bbox.Max, twoConduits, multilayerdPS, doc);
            return orderedPoints;
        }
        private static List<XYZ> CollectBetweenElementByNewBoundingBoxKick(XYZ minXYZ, XYZ maxXYZ, List<Element> matchingElements, Dictionary<XYZ,
           Element> multilayerdPS, Document doc)
        {
            BoundingBoxXYZ combinedBoundingBox = new BoundingBoxXYZ
            {
                Min = minXYZ,
                Max = maxXYZ
            };
            BoundingBoxIntersectsFilter boundingBoxFilter = new BoundingBoxIntersectsFilter(new Autodesk.Revit.DB.Outline(combinedBoundingBox.Min, combinedBoundingBox.Max));
            List<Element> collector = multilayerdPS.Select(x => x.Value).ToList();
            List<Element> conduitsBetween = new List<Element>();
            conduitsBetween.Add(matchingElements[0]);
            List<Element> IntersectingElements = new FilteredElementCollector(doc, doc.ActiveView.Id).WhereElementIsNotElementType().WherePasses(boundingBoxFilter).ToList();
            foreach (Element conduit in collector)
            {
                if (conduit.Id != matchingElements[0].Id && conduit.Id != matchingElements[1].Id) // Exclude the original two conduits
                {
                    if (boundingBoxFilter.PassesFilter(conduit))
                    {
                        conduitsBetween.Add(conduit);
                    }
                    else if (IntersectingElements.Any(x => x.Id.IntegerValue == conduit.Id.IntegerValue))
                    {
                        conduitsBetween.Add(conduit);
                    }
                }
            }
            conduitsBetween.Add(matchingElements[1]);
            List<XYZ> collectingOrigin = multilayerdPS
                                            .Where(kvp => conduitsBetween.Contains(kvp.Value))
                                            .Select(kvp => kvp.Key)
                                            .ToList();
            //XYZ referencePoint = collectingOrigin[0];
            //List<XYZ> orderedPoints = collectingOrigin.OrderBy(p => p.DistanceTo(referencePoint)).ToList();
            return collectingOrigin;
        }
        private static double DistanceToLine(XYZ p1, XYZ p2, XYZ p)
        {
            // Calculate the distance from point p to the line defined by points p1 and p2
            XYZ lineDirection = p2 - p1;
            XYZ lineToPoint = p - p1;
            XYZ projection = lineDirection.CrossProduct(lineToPoint.CrossProduct(lineDirection)).Normalize();
            return projection.DotProduct(lineToPoint);
        }
        public static XYZ FindMinimumDistance(XYZ referencePoint, XYZ[] points)
        {
            if (points == null || points.Length == 0)
                throw new ArgumentException("The points array must contain at least one point.");
            XYZ minPoint = points[0];
            double minDistance = referencePoint.DistanceTo(minPoint);
            foreach (var point in points)
            {
                double distance = referencePoint.DistanceTo(point);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    minPoint = point;
                }
            }
            return minPoint;
        }
        public static XYZ GetIntersection(Line line1, Line line2)
        {
            IntersectionResultArray resultArray;
            SetComparisonResult setComparisonResult = line1.Intersect(line2, out resultArray);
            if (setComparisonResult != SetComparisonResult.Overlap)
            {
                return null;
            }

            if (resultArray == null || resultArray.Size != 1)
            {
                return XYZ.Zero;
            }

            IntersectionResult intersectionResult = resultArray.get_Item(0);
            return intersectionResult.XYZPoint;
        }
        public static Dictionary<double, List<Element>> GroupByElementsWithElevation(List<Element> ElementCollection, string offsetVariable)
        {
            Dictionary<int, List<ConduitGrid>> CongridDictionary = GroupStubElements(ElementCollection);
            Dictionary<double, List<Element>> groupedPrimaryElements = new Dictionary<double, List<Element>>();
            foreach (KeyValuePair<int, List<ConduitGrid>> kvp in CongridDictionary)
            {
                if (kvp.Value.Any())
                {
                    List<Element> groupedConduits = kvp.Value.Select(r => r.Conduit).ToList();
                    Utility.GroupByElevation(groupedConduits, offsetVariable, ref groupedPrimaryElements);
                }
            }
            return groupedPrimaryElements;
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
                            Autodesk.Revit.DB.Parameter parameter = newConCopy.LookupParameter(offsetVariable);
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


                            Autodesk.Revit.DB.Parameter param = newCon.LookupParameter("Middle Elevation");
                            Autodesk.Revit.DB.Parameter param2 = newCon2.LookupParameter("Middle Elevation");

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
                        _ = Utility.UserActivityLog(System.Reflection.Assembly.GetExecutingAssembly(), uiapp, "Auto Updater", startDate, "Completed", "Vertical Offset", "Public", "Connect");

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
                    _ = Utility.UserActivityLog(System.Reflection.Assembly.GetExecutingAssembly(), uiapp, "Auto Updater", startDate, "Failed", "Vertical Offset", "Public", "Connect");
                    successful = false;
                }
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show("Warning. \n" + exception.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                successful = false;
                _ = Utility.UserActivityLog(System.Reflection.Assembly.GetExecutingAssembly(), uiapp, "Auto Updater", startDate, "Failed", "Vertical Offset", "Public", "Connect");
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
            try
            {
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
            }
            catch
            {
                conduitCollection = arrelements;
            }

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
