﻿using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using MeshPoints.Classes;
using Rhino.Geometry.Collections;

namespace MeshPoints.QuadRemesh
{
    public class AnalyzeTriangleMesh : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AnalyzeTriangleMesh class.
        /// </summary>
        public AnalyzeTriangleMesh()
          : base("Analyze Triangle Mesh", "atm",
              "Analyse elements and edges in a triangle mesh",
              "MyPlugIn", "QuadRemesh")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Triangle mesh", "trimesh", "Input a trinagle mesh", GH_ParamAccess.item);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Front edges", "element", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("List 1-1", "11", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("List 1-0", "10", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("List 0-1", "01", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("List 0-0", "00", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("qElements", "element", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("E_1", "element", "", GH_ParamAccess.item);
            pManager.AddGenericParameter("E_2", "element", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // variables
            Mesh mesh = new Mesh();
            List<qElement> elementList = new List<qElement>();
            List<qEdge> edgeList = new List<qEdge>();
            qElement element = new qElement();
            List<qEdge> frontEdges = new List<qEdge>();
            List<double> angleList = new List<double>();

            // input
            DA.GetData(0, ref mesh);

            // NOTE: Topological vertices and vertices are not sorted the same way. Make use to topological vertices in these codes.
            edgeList = CreateEdges(mesh);
            elementList = CreateElements(mesh, edgeList);
            SetNeighborElements(mesh, elementList, edgeList);
            frontEdges = GetFrontEdges(mesh, edgeList);
            SetNeighorFrontEdges(mesh, frontEdges);
            var edgeStates = CreateEdgeStateList(mesh, frontEdges); // todo: check bug. Angles.
            var list11 = edgeStates.Item1;
            var list10 = edgeStates.Item2;
            var list01 = edgeStates.Item3;
            var list00 = edgeStates.Item4;


            //-----side edge definiton-------
            qEdge E_front = new qEdge();
            int edgeState = 0;
            double thetaTolerance = 0.16667 * Math.PI;

            // get E_front
            if (list11.Count != 0) { E_front = list11[0]; edgeState = 11; }
            else if (list10.Count != 0) { E_front = list10[0]; edgeState = 10; }
            else if (list01.Count != 0) { E_front = list01[0]; edgeState = 01; }
            else { E_front = list00[0]; edgeState = 00; }

            E_front = list01[0]; //-------------- only temporary
            edgeState = 01; //----------- only temporary

            int nodeToEvaluate = 0; // 0=left, 1=right //

            qEdge E_leftFront = E_front.LeftFrontNeighbor;
            qEdge E_rightFront = E_front.RightFrontNeighbor;
            qEdge E_neighborFront = new qEdge();
            qEdge E_1 = new qEdge();
            qEdge E_2 = new qEdge();

            // get node, if not edgestate == 11
            qNode rightNode = GetRightNode(E_front);
            qNode leftNode = GetLeftNode(E_front);

            if (nodeToEvaluate == 0) { E_neighborFront = E_leftFront; }
            else { E_neighborFront = E_rightFront; }

            var vectorsAndSharedNode = CalculateVectorsFromSharedNode(E_front, E_neighborFront);
            Vector3d vec1 = vectorsAndSharedNode.Item1; // get vector for E_front
            Vector3d vec2 = vectorsAndSharedNode.Item2; // get vector for E_neighborFront
            Vector3d V_k = vec1.Length * vec2 + vec2.Length * vec1; // angle bisector

            if (V_k == Vector3d.Zero)
            {
                if (nodeToEvaluate == 0) { V_k = vec1; }
                else { V_k = vec2; }
                Vector3d rotationAxis = Vector3d.CrossProduct(vec1, vec2);
                V_k.Rotate(0.5 * Math.PI, rotationAxis);
            }

            qNode N_k = vectorsAndSharedNode.Item3; // shared node
            int[] connectedEdgesIndex = N_k.ConnectedEdges; // get connected edges indices of shared node

            #region Get E_i
            List<qEdge> E_i_list = new List<qEdge>();
            foreach (int edgIndex in connectedEdgesIndex) // loop connected edges
            {
                if ((edgIndex == E_front.Index) | (edgIndex == E_neighborFront.Index)) { continue; } // continue if connected edge is E_frontEdge or E_neighborFront 
                int[] connectedElements = mesh.TopologyEdges.GetConnectedFaces(edgIndex); // get connected element indices of edge

                foreach (int elemIndex in connectedElements) // loop connected elements
                {
                    qElement connectedElement = elementList[elemIndex];
                    if (!connectedElement.IsQuad)
                    {
                        E_i_list.Add(edgeList[edgIndex]);
                    }
                }
            }
            #endregion

            // calcualte thetas 
            List<double> theta_i_list = new List<double>();
            foreach (qEdge E_i in E_i_list)
            {
                Vector3d E_i_vec = GetVectorOfEdgeFromNode(E_i, N_k);
                double theta_i = Vector3d.VectorAngle(V_k, E_i_vec);
                theta_i_list.Add(theta_i);
            }

            // get E_k
            qEdge E_k = new qEdge();
            qEdge E_0 = new qEdge();

            // find smallest theta
            double min = theta_i_list[0];
            int minIndex = 0;
            for (int i = 1; i < theta_i_list.Count; ++i)
            {
                if (theta_i_list[i] < min)
                {
                    min = theta_i_list[i];
                    minIndex = i;
                }
            }

            if (theta_i_list[minIndex] < thetaTolerance) { E_k = E_i_list[minIndex]; } // todo: when e_1 = e_2, what to do?
            // next: FindEdge(node1, node2)
            // swap this edge...
            /*
            qElement sharedElement = new qElement();
            if (E_1.Element1 == E_2.Element1) { sharedElement = E_1.Element1; }
            else if (E_1.Element1 == E_2.Element2) { sharedElement = E_1.Element1; }
            else if (E_1.Element2 == E_2.Element1) { sharedElement = E_1.Element2; }
            else if (E_1.Element2 == E_2.Element2) { sharedElement = E_1.Element2; }

            foreach (qEdge edge in sharedElement.EdgeList)
            { if (edge != E_1 & edge != E_2) { E_0 = edge; } }
            
            */


            // output
            DA.SetDataList(0, frontEdges);
            DA.SetDataList(1, list11);
            DA.SetDataList(2, list10);
            DA.SetDataList(3, list01);
            DA.SetDataList(4, list00);
            DA.SetDataList(5, theta_i_list);
            DA.SetData(6,minIndex); 
            DA.SetData(7, E_k);
        }
        #region Methods
        private List<qNode> CreateNodes(Mesh mesh)
        {
            List<qNode> nodeList = new List<qNode>();
            MeshTopologyVertexList topologyVertexList = mesh.TopologyVertices;
            for (int i = 0; i < topologyVertexList.Count; i++)
            {
                qNode node = new qNode();
                node.TopologyVertexIndex = i;
                node.MeshVertexIndex = topologyVertexList.MeshVertexIndices(i)[0]; // todo: check if ok
                node.Coordinate = mesh.Vertices.Point3dAt(node.MeshVertexIndex);
                node.ConnectedEdges = mesh.TopologyVertices.ConnectedEdges(node.TopologyVertexIndex);
                nodeList.Add(node);
            }
            return nodeList;
        }
        private List<qEdge> CreateEdges(Mesh mesh)
        {
            List<qEdge> edgeList = new List<qEdge>();
            MeshTopologyEdgeList topologyEdgeList = mesh.TopologyEdges;
            MeshTopologyVertexList topologyVertexList = mesh.TopologyVertices;
            List<qNode> nodeList = CreateNodes(mesh);

            for (int i = 0; i < topologyEdgeList.Count; i++)
            {
                Rhino.IndexPair edgeTopoVerticesIndex = topologyEdgeList.GetTopologyVertices(i);
                qNode startNode = nodeList[edgeTopoVerticesIndex[0]];
                qNode endNode = nodeList[edgeTopoVerticesIndex[1]];

                qEdge edge = new qEdge(i, startNode, endNode); //sjekk om i er nødvendig
                edgeList.Add(edge);
            }
            return edgeList;
        }
        private List<qElement> CreateElements(Mesh mesh, List<qEdge> edgeList)
        {
            qElement element = new qElement();
            List<qElement> elementList = new List<qElement>();
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                List<qEdge> edgeListElement = new List<qEdge>();

                int[] elementEdgesIndex = mesh.TopologyEdges.GetEdgesForFace(i);
                foreach (int n in elementEdgesIndex)
                {
                    edgeListElement.Add(edgeList[n]);
                }
                element = new qElement(i, edgeListElement);
                elementList.Add(element);
            }
            return elementList;
        }
        private void SetNeighborElements(Mesh mesh, List<qElement> elementList, List<qEdge> edgeList)
        {
            for (int i = 0; i < mesh.TopologyEdges.Count; i++)
            {
                int[] connectedElementsIndex = mesh.TopologyEdges.GetConnectedFaces(i);
                edgeList[i].Element1 = elementList[connectedElementsIndex[0]];
                if (connectedElementsIndex.Length == 2)
                {
                    edgeList[i].Element2 = elementList[connectedElementsIndex[1]];
                }
            }
            return;
        }
        private List<qEdge> GetFrontEdges(Mesh mesh, List<qEdge> edgeList)
        {
            List<qEdge> frontEdges = new List<qEdge>();
            for (int i = 0; i < edgeList.Count; i++)
            {
                int[] connectedElementsIndex = mesh.TopologyEdges.GetConnectedFaces(i);
                if ((connectedElementsIndex.Length == 1) & !edgeList[i].Element1.IsQuad)
                {
                    frontEdges.Add(edgeList[i]);
                }
            }
            return frontEdges;
        }
        private void SetNeighorFrontEdges(Mesh mesh, List<qEdge> frontEdges)
        {
            for (int i = 0; i < frontEdges.Count; i++)
            {
                qEdge edg = frontEdges[i];
                qEdge edg1 = new qEdge();
                qEdge edg2 = new qEdge();
                qNode[] edgeNodes = new qNode[] { edg.StartNode, edg.EndNode };
                for (int j = 0; j < frontEdges.Count; j++)
                {
                    if (i != j)
                    {
                        qNode[] testNodes = new qNode[] { frontEdges[j].StartNode, frontEdges[j].EndNode };
                        if ((edgeNodes[0] == testNodes[0]) | (edgeNodes[0] == testNodes[1]))
                        {
                             edg1 = frontEdges[j];
                        }
                        else if ((edgeNodes[1] == testNodes[0]) | (edgeNodes[1] == testNodes[1]))
                        {
                             edg2 = frontEdges[j];
                        }
                        Point3d midPointEdg = (edgeNodes[0].Coordinate + edgeNodes[1].Coordinate) / 2.0;

                        int[] connectedElementsIndex1 = mesh.TopologyEdges.GetConnectedFaces(edg1.Index); // get the face index to edg1
                        Point3d center1 = mesh.Faces.GetFaceCenter(connectedElementsIndex1[0]);           // get the facecenter of edg1

                        int[] connectedElementsIndex2 = mesh.TopologyEdges.GetConnectedFaces(edg2.Index); // get the face index to edg1
                        Point3d center2 = mesh.Faces.GetFaceCenter(connectedElementsIndex2[0]);           // get the facecenter of edg1

                        Vector3d vec1 = center1 - midPointEdg;
                        Vector3d vec2 = center2 - midPointEdg;
                        if (Vector3d.CrossProduct(vec1, vec2).Z > 0) // todo: modify to more general relative to face normals
                        {
                            edg.RightFrontNeighbor = edg1;
                            edg.LeftFrontNeighbor = edg2;
                        }
                        else
                        {
                            edg.RightFrontNeighbor = edg2;
                            edg.LeftFrontNeighbor = edg1;
                        }
                    }
                }
            }
            return;
        }
        Tuple<List<qEdge>, List<qEdge>, List<qEdge>, List<qEdge>> CreateEdgeStateList(Mesh mesh, List<qEdge> frontEdges)
        {
            // Return the EdgeStates in the format of the list: list11, list01, list10, andlist00.
            List<qEdge> list11 = new List<qEdge>();
            List<qEdge> list01 = new List<qEdge>();
            List<qEdge> list10 = new List<qEdge>();
            List<qEdge> list00 = new List<qEdge>();

            double angleTolerance = 0.75 * Math.PI;
            double leftAngle = 0;
            double rightAngle = 0;
            int edgeState = 0;
            
            for (int i = 0; i < frontEdges.Count; i++)
            {
                for (int nodeToCalculate = 0; nodeToCalculate < 2; nodeToCalculate++) // nodeToCalculate = 0 is left neighbor, otherwise right neighbor
                {
                    if (nodeToCalculate == 0)
                    {
                        leftAngle = CalculateAngleOfAdjecentEdges(mesh, nodeToCalculate, frontEdges[i]);
                    }
                    else
                    {
                        rightAngle = CalculateAngleOfAdjecentEdges(mesh, nodeToCalculate, frontEdges[i]);
                    }

                    if (leftAngle < angleTolerance & rightAngle < angleTolerance) { edgeState = 11; }
                    else if (leftAngle >= angleTolerance & rightAngle < angleTolerance) { edgeState = 01; }
                    else if (leftAngle < angleTolerance & rightAngle >= angleTolerance) { edgeState = 10; }
                    else if (leftAngle >= angleTolerance & rightAngle >= angleTolerance) { edgeState = 00; }
                }

                switch (edgeState)
                {
                    case 11:
                        list11.Add(frontEdges[i]);
                        break;
                    case 10:
                        list10.Add(frontEdges[i]);
                        break;
                    case 01:
                        list01.Add(frontEdges[i]);
                        break;
                    case 00:
                        list00.Add(frontEdges[i]);
                        break;
                }
            }
            return Tuple.Create(list11, list10, list01, list00);
        }
        private double CalculateAngleOfAdjecentEdges(Mesh mesh, int nodeToCalculate, qEdge edge)
        {
            // Calculate the angle between front edges with a shared point, i.e. neighbor front edges.

            int[] connectedElementsIndex = mesh.TopologyEdges.GetConnectedFaces(edge.Index);
            if (!((connectedElementsIndex.Length == 1) & !edge.Element1.IsQuad))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The edge is not a front edge.");}

            qEdge edgeNeighbor = new qEdge();
            Vector3d vec1 = Vector3d.Zero;
            Vector3d vec2 = Vector3d.Zero;
            double angle = 0;

            // Get neighbor nodes
            if (nodeToCalculate == 0) { edgeNeighbor = edge.LeftFrontNeighbor; }
            else { edgeNeighbor = edge.RightFrontNeighbor; }

            // Create vectors from shared node
            var vectors = CalculateVectorsFromSharedNode(edge, edgeNeighbor);
            vec1 = vectors.Item1;
            vec2 = vectors.Item2;
            qNode sharedNode= vectors.Item3;
            Vector3d V_k = vec1.Length * vec2 + vec2.Length * vec1; // angle bisector
            if (V_k == Vector3d.Zero) // create V_k if zero vector
            {
                if (nodeToCalculate == 0) { V_k = vec1; }
                else { V_k = vec2; }

                Vector3d rotationAxis = Vector3d.CrossProduct(vec1, vec2);
                V_k.Rotate(0.5 * Math.PI, rotationAxis);
            }
            V_k = V_k / V_k.Length; // normalize

            // check with domain
            Point3d endPointV_k = Point3d.Add(sharedNode.Coordinate, V_k); // endpoint of V_k from sharedNode
            Point3d endPointV_kNegative = Point3d.Add(sharedNode.Coordinate, -V_k); // endpoint of -V_k from sharedNode

            // distance to domain
            int edgeElementFaceId = edge.Element1.FaceIndex;
            int edgeNeighborElementFaceId = edgeNeighbor.Element1.FaceIndex;
            Point3d edgeFaceCenter = mesh.Faces.GetFaceCenter(edge.Element1.FaceIndex);
            Point3d edgeNeighborFaceCenter = mesh.Faces.GetFaceCenter(edgeNeighbor.Element1.FaceIndex);
            Point3d midFaceCenter = (edgeFaceCenter + edgeNeighborFaceCenter) / 2; // mid face center

            double distanceEndPoint = midFaceCenter.DistanceTo(endPointV_k); // todo: ok for all cases?
            double distanceEndPointNegative = midFaceCenter.DistanceTo(endPointV_kNegative); // todo: ok for all cases?
            double alpha = Vector3d.VectorAngle(vec1, vec2);

            if (distanceEndPoint < distanceEndPointNegative)
            {
                // V_k is inside domain
                angle = alpha;
            }
            else 
            {
                // V_k is outside domain
                angle = 2 * Math.PI - alpha;
            }
            return angle;
        }
        private qNode GetRightNode(qEdge edge)
        {
            // get the right node of a front edge
            qNode sharedNode = new qNode();
            if (edge.StartNode == edge.RightFrontNeighbor.StartNode) { sharedNode = edge.StartNode; }
            else if (edge.StartNode == edge.RightFrontNeighbor.EndNode) { sharedNode = edge.StartNode; }
            else if (edge.EndNode == edge.RightFrontNeighbor.StartNode) { sharedNode = edge.EndNode; }
            else if (edge.EndNode == edge.RightFrontNeighbor.EndNode) { sharedNode = edge.EndNode; }
            else { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No shared nodes"); }
            return sharedNode;
        }
        private qNode GetLeftNode(qEdge edge)
        {
            // get the right node of a front edge
            qNode sharedNode = new qNode();
            if (edge.StartNode == edge.LeftFrontNeighbor.StartNode) { sharedNode = edge.StartNode; }
            else if (edge.StartNode == edge.LeftFrontNeighbor.EndNode) { sharedNode = edge.StartNode; }
            else if (edge.EndNode == edge.LeftFrontNeighbor.StartNode) { sharedNode = edge.EndNode; }
            else if (edge.EndNode == edge.LeftFrontNeighbor.EndNode) { sharedNode = edge.EndNode; }
            else { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No shared nodes"); }
            return sharedNode;
        }
        private Tuple<Vector3d, Vector3d, qNode> CalculateVectorsFromSharedNode(qEdge edge1, qEdge edge2)
        {
            Vector3d vec1 = Vector3d.Zero;
            Vector3d vec2 = Vector3d.Zero;
            qNode sharedNode = new qNode();
            if (edge1.StartNode == edge2.StartNode)
            {
                sharedNode = edge1.StartNode;
                vec1 = edge1.EndNode.Coordinate - edge1.StartNode.Coordinate;
                vec2 = edge2.EndNode.Coordinate - edge2.StartNode.Coordinate;
            }
            else if (edge1.StartNode == edge2.EndNode)
            {
                sharedNode = edge1.StartNode;
                vec1 = edge1.EndNode.Coordinate - edge1.StartNode.Coordinate;
                vec2 = edge2.StartNode.Coordinate - edge2.EndNode.Coordinate;
            }
            else if (edge1.EndNode == edge2.StartNode)
            {
                sharedNode = edge1.EndNode;
                vec1 = edge1.StartNode.Coordinate - edge1.EndNode.Coordinate;
                vec2 = edge2.EndNode.Coordinate - edge2.StartNode.Coordinate;
            }
            else if (edge1.EndNode == edge2.EndNode)
            {
                sharedNode = edge1.EndNode;
                vec1 = edge1.StartNode.Coordinate - edge1.EndNode.Coordinate;
                vec2 = edge2.StartNode.Coordinate - edge2.EndNode.Coordinate;
            }
            return Tuple.Create(vec1, vec2, sharedNode);
        }
        private Vector3d GetVectorOfEdgeFromNode(qEdge edge, qNode node)
        {
            Vector3d vec = Vector3d.Zero;
            if (edge.StartNode == node) { vec = edge.EndNode.Coordinate - node.Coordinate; }
            else if (edge.EndNode == node) { vec = edge.StartNode.Coordinate - node.Coordinate; }
            return vec;
        }
        #endregion

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("71512089-a4f6-45fd-8b73-c1c42c18e59e"); }
        }
    }
}