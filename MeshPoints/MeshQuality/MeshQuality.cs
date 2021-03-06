﻿using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino;
using System;
using System.Linq;
using System.Collections.Generic;
using MeshPoints.Classes;
using System.Drawing;
using MathNet.Numerics.LinearAlgebra;

namespace MeshPoints.MeshQuality
{
    public class MeshQuality : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Mesh_Quality class.
        /// </summary>
        public MeshQuality()
          : base("Mesh Quality", "mq",
              "Mesh Quality",
              "MyPlugIn", "Quality")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Mesh2D", "m", "Insert Mesh2D class", GH_ParamAccess.item);
            pManager.AddGenericParameter("Mesh3D", "m", "Insert Mesh3D class", GH_ParamAccess.item);

            pManager.AddIntegerParameter("Quality metric", "q", "Aspect Ratio = 1, Skewness = 2, Jacobian = 3", GH_ParamAccess.item);

            pManager[0].Optional = true; // mesh2D is optional
            pManager[1].Optional = true; // mesh3D is optional
            pManager[2].Optional = true; // coloring the mesh is optional
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Quality", "mq", "Mesh Quality for elements", GH_ParamAccess.list);
            pManager.AddGenericParameter("Avg. Aspect Ratio", "ar", "Average aspect ratio of all elements.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Avg. Skewness", "sk", "Average skewness of all elements.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Avg. Jacobian", "jb", "Average Jacobian ratio of all elements", GH_ParamAccess.item);
            pManager.AddGenericParameter("Color Mesh", "cm", "Color map of quality check", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            #region Variables
            Mesh2D mesh2D = new Mesh2D();
            Mesh3D mesh3D = new Mesh3D();
            Mesh colorMesh = new Mesh();

            // Determines which quality check type to color mesh with
            // 1 = aspect ratio, 2 = skewness, 3 = jacobian
            int qualityCheckType = 0;

            List<Quality> qualityList = new List<Quality>(); // list of Quality for each element in the mesh
            Quality elementQuality = new Quality(); 
            List<Element> elements = new List<Element>();

            double sumAspectRatio = 0;
            double sumSkewness = 0;
            double sumJacobianRatio = 0;
            double avgAspectRatio = 0;
            double avgSkewness = 0;
            double avgJacobianRatio = 0;

            #endregion

            #region Inputs
            DA.GetData(0, ref mesh2D);
            DA.GetData(1, ref mesh3D);
            DA.GetData(2, ref qualityCheckType);
            #endregion

            #region Calculate mesh quality
            elements = GetElements(mesh2D, mesh3D);

            if (elements != null) {
                foreach (Element e in elements)
                {
                    elementQuality.AspectRatio = CalculateAspectRatio(e);
                    elementQuality.Skewness = CalculateSkewness(e);
                    elementQuality.JacobianRatio = CalculateJacobianRatio(e);

                    elementQuality.element = e;
                    e.MeshQuality = elementQuality;

                    sumAspectRatio += elementQuality.AspectRatio;
                    sumSkewness += elementQuality.Skewness;
                    sumJacobianRatio += elementQuality.JacobianRatio;

                    qualityList.Add(elementQuality); // todo: check if this is needed
                    elementQuality = new Quality();
                }

                avgAspectRatio = Math.Round(sumAspectRatio / elements.Count, 3);
                avgSkewness = Math.Round(sumSkewness / elements.Count, 3);
                avgJacobianRatio = Math.Round(sumJacobianRatio / elements.Count, 3);
                #endregion

                // Color the mesh based on quality type
                ColorMesh(colorMesh, qualityList, qualityCheckType);
            }

            #region Outputs
            DA.SetDataList(0, qualityList);
            DA.SetData(1, avgAspectRatio);
            DA.SetData(2, avgSkewness);
            DA.SetData(3, avgJacobianRatio);
            DA.SetData(4, colorMesh);
            #endregion
        }

        #region Component methods
        /// <summary>
        /// Gets the elements from a mesh, independent of the mesh type is Mesh2D or Mesh3D.
        /// </summary>
        List<Element> GetElements(Mesh2D mesh2D, Mesh3D mesh3D)
        {
            List<Element> elements = new List<Element>();

            // check mesh type
            if (mesh2D.Elements != null) { elements = mesh2D.Elements; } // mesh2D input
            else { elements = mesh3D.Elements; } // mesh3D input

            // if no elements are found
            if (elements == null)
            {
                //throw new ArgumentNullException(
                //    message: "No elements found.",
                //    paramName: "elements");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Zero mesh inputs found.");
            }
            return elements;
        }

        /// <summary>
        /// Calculates the Aspect Ratio of an element.
        /// </summary>
        double CalculateAspectRatio(Element e)
        {
            double AR = 0;
            double maxDistance = 0;
            double minDistance = 0;
            double idealAR = 0.5 / Math.Sqrt(Math.Pow(0.5, 2) * 3);

            List<double> cornerToCornerDistance = new List<double>();
            List<double> cornerToCentroidDistance = new List<double>();
            List<double> faceToCentroidDistance = new List<double>();

            List<Point3d> faceCenterPts = GetFaceCenter(e);
            Point3d centroidPt = GetCentroidOfElement(e);
            List<Point3d> elementCornerPtsDublicated = GetCoordinatesOfNodesDublicated(e);

            // find distances from corners to centroid
            for (int n = 0; n < elementCornerPtsDublicated.Count / 2; n++)
            {
                cornerToCentroidDistance.Add(elementCornerPtsDublicated[n].DistanceTo(centroidPt));
                cornerToCornerDistance.Add(elementCornerPtsDublicated[n].DistanceTo(elementCornerPtsDublicated[n + 1])); // add the distance between the points, following mesh edges CCW
            }

            // find distances from face center to centroid
            for (int n = 0; n < faceCenterPts.Count; n++)
            {
                faceToCentroidDistance.Add(faceCenterPts[n].DistanceTo(centroidPt));
            }

            cornerToCentroidDistance.Sort();
            cornerToCornerDistance.Sort();
            faceToCentroidDistance.Sort();

            if (!e.IsCube) // mesh2d
            {
                minDistance = cornerToCornerDistance[0];
                maxDistance = cornerToCornerDistance[cornerToCornerDistance.Count - 1];
                AR = minDistance / maxDistance;
            }
            else // mesh3d
            {
                minDistance = Math.Min(cornerToCentroidDistance[0], faceToCentroidDistance[0]);
                maxDistance = Math.Max(cornerToCentroidDistance[cornerToCentroidDistance.Count - 1], faceToCentroidDistance[faceToCentroidDistance.Count - 1]);
                AR = (minDistance / maxDistance) / idealAR; // normalized AR
            }
            return AR;
        }

        /// <summary>
        /// Calculates the Skewness of an element.
        /// </summary>
        double CalculateSkewness(Element e)
        {
            double SK = 0;
            double maxAngle = 0;
            double minAngle = 0;
            double idealAngle = 90; //ideal angle in degrees
            int neighborPoint = 3; //variable used in skewness calculation
            Vector3d vec1 = Vector3d.Zero;
            Vector3d vec2 = Vector3d.Zero;
            List<double> elementAngles = new List<double>();

            int numFaces = 1; // mesh2D
            if (e.IsCube) { numFaces = 6; } // mesh3D

            for (int i = 0; i < numFaces; i++)
            {
                e.mesh.Faces.GetFaceVertices(i, out Point3f n1, out Point3f n2, out Point3f n3, out Point3f n4); // todo: sjekk rekkefølge // todo: rett opp i rekkefølge og point3f til point3d
                List<Point3d> faceCornersDublicated = new List<Point3d>()
                {
                        n1, n2, n3, n4,
                        n1, n2, n3, n4
                };

                for (int n = 0; n < faceCornersDublicated.Count / 2; n++)
                {
                    //create a vector from a vertex to a neighbouring vertex
                    vec1 = faceCornersDublicated[n] - faceCornersDublicated[n + 1];
                    vec2 = faceCornersDublicated[n] - faceCornersDublicated[n + neighborPoint];

                    //calculate angles between vectors
                    double angleRad = Math.Abs(Math.Acos(Vector3d.Multiply(vec1, vec2) / (vec1.Length * vec2.Length)));
                    double angleDegree = angleRad * 180 / Math.PI;//convert from rad to deg
                    elementAngles.Add(angleDegree);
                }

            }
            elementAngles.Sort();
            minAngle = Math.Abs(elementAngles[0]); // todo: controll if abs ok
            maxAngle = Math.Abs(elementAngles[elementAngles.Count - 1]);
            SK = 1 - Math.Max((maxAngle - idealAngle) / (180 - idealAngle), (idealAngle - minAngle) / (idealAngle));
            return SK;
        }

        /// <summary>
        /// Determines the Jacobian ratio based on if the element is from a <see cref="Mesh2D"/> or a <see cref="Mesh3D"/>.
        /// </summary>
        /// <param name="e">A single <see cref="Element"/> object from a mesh.</param>
        /// <returns>A <see cref="double"/> between 0.0 and 1.0 describing the ratio between the min and max values of the determinants of the Jacobian matrix of the element, evaluated in the corner nodes.</returns>
        private double CalculateJacobianRatio(Element e)
        {
            double jacobian;
            if (e.IsCube) // returns true if the Element e stems from a Mesh3D.
            {
                jacobian = CalculateJacobianOf8NodeElement(e);
            }
            else
            {
                jacobian = CalculateJacobianOfQuadElement(e);
            }
            return jacobian;
        }

        /// <summary>
        /// Returns a list of node coordinated that is dublicate.
        /// </summary>
        List<Point3d> GetCoordinatesOfNodesDublicated(Element e)
        {
            List<Point3d> pts = new List<Point3d>();
            if (!e.IsCube) // mesh2D
            {
                pts = new List<Point3d>()
                {
                    e.Node1.Coordinate, e.Node2.Coordinate, e.Node3.Coordinate, e.Node4.Coordinate,
                    e.Node1.Coordinate, e.Node2.Coordinate, e.Node3.Coordinate, e.Node4.Coordinate,
                };
            }
            else // mesh3D
            {
                pts = new List<Point3d>()
                {
                    e.Node1.Coordinate, e.Node2.Coordinate, e.Node3.Coordinate, e.Node4.Coordinate,
                    e.Node5.Coordinate, e.Node6.Coordinate, e.Node7.Coordinate, e.Node8.Coordinate,
                    e.Node1.Coordinate, e.Node2.Coordinate, e.Node3.Coordinate, e.Node4.Coordinate,
                    e.Node5.Coordinate, e.Node6.Coordinate, e.Node7.Coordinate, e.Node8.Coordinate,
                };

            }
            return pts;
        }

        /// <summary>
        /// Returns a list of the face centroid to an element
        /// </summary>
        List<Point3d> GetFaceCenter(Element e)
        {
            List<Point3d> faceCenterPts = new List<Point3d>();
            int numFaces = 1; // if mesh2D
            if (e.IsCube) { numFaces = 6; } // if mesh3D

            for (int i = 0; i < numFaces; i++)
            {
                faceCenterPts.Add(e.mesh.Faces.GetFaceCenter(i));
            }
            return faceCenterPts;
        }

        /// <summary>
        /// Return the centroid of an element
        /// </summary>
        Point3d GetCentroidOfElement(Element e)
        {
            double sx = 0;
            double sy = 0;
            double sz = 0;
            List<Point3d> pts = GetCoordinatesOfNodesDublicated(e);
            foreach (Point3d pt in pts)
            {
                sx = sx + pt.X;
                sy = sy + pt.Y;
                sz = sz + pt.Z;
            }
            int n = pts.Count;
            Point3d centroidPt = new Point3d(sx / n, sy / n, sz / n);

            return centroidPt;
        }

        /// <summary>
        /// Calculates the Jacobian Ratio of a hexahedral 8-node 3D element.
        /// </summary>
        /// <param name="e">An 8 node <see cref="Element"/> that is part of a <see cref="Mesh3D"/></param>
        /// <returns>A <see cref="double"/> between 0.0 and 1.0.</returns>
        private double CalculateJacobianOf8NodeElement(Element e)
        {
            var naturalNodes = new List<List<Double>>
            {
                new List<double> { -1, -1, -1 }, new List<double> { 1, -1, -1}, new List<double> { 1, 1, -1 }, new List<double> { -1, 1, -1 },
                new List<double> { -1, -1, 1 }, new List<double> { 1, -1, 1 }, new List<double> { 1, 1, 1 }, new List<double> { -1, 1, 1 }
            };

            // Global X, Y, and Z-coordinates of the corner nodes of the actual element
            List<double> gX = new List<double>()
                {
                    e.Node1.Coordinate.X, e.Node2.Coordinate.X, e.Node3.Coordinate.X, e.Node4.Coordinate.X,
                    e.Node5.Coordinate.X, e.Node6.Coordinate.X, e.Node7.Coordinate.X, e.Node8.Coordinate.X
                };
            List<double> gY = new List<double>()
                {
                    e.Node1.Coordinate.Y, e.Node2.Coordinate.Y, e.Node3.Coordinate.Y, e.Node4.Coordinate.Y,
                    e.Node5.Coordinate.Y, e.Node6.Coordinate.Y, e.Node7.Coordinate.Y, e.Node8.Coordinate.Y
                };
            List<double> gZ = new List<double>()
                {
                    e.Node1.Coordinate.Z, e.Node2.Coordinate.Z, e.Node3.Coordinate.Z, e.Node4.Coordinate.Z,
                    e.Node5.Coordinate.Z, e.Node6.Coordinate.Z, e.Node7.Coordinate.Z, e.Node8.Coordinate.Z
                };

            // Evaluate in each of the corner nodes.
            List<double> jacobiansOfElement = new List<double>();
            foreach (List<Double> node in naturalNodes)
            {
                // Substitute the natural coordinates into the symbolic expression
                var r = node[0];
                var s = node[1];
                var t = node[2];

                // Partial derivatives of the shape functions
                var N1Dr = -0.125 * (s - 1) * (t - 1);
                var N1Ds = -0.125 * (r - 1) * (t - 1);
                var N1Dt = -0.125 * (r - 1) * (s - 1);
                var N2Dr = 0.125 * (s - 1) * (t - 1);
                var N2Ds = 0.125 * (r + 1) * (t - 1);
                var N2Dt = 0.125 * (r + 1) * (s - 1);
                var N3Dr = -0.125 * (s + 1) * (t - 1);
                var N3Ds = -0.125 * (r + 1) * (t - 1);
                var N3Dt = -0.125 * (r + 1) * (s + 1);
                var N4Dr = 0.125 * (s + 1) * (t - 1);
                var N4Ds = 0.125 * (r - 1) * (t - 1);
                var N4Dt = 0.125 * (r - 1) * (s + 1);
                var N5Dr = 0.125 * (s - 1) * (t + 1);
                var N5Ds = 0.125 * (r - 1) * (t + 1);
                var N5Dt = 0.125 * (r - 1) * (s - 1);
                var N6Dr = -0.125 * (s - 1) * (t + 1);
                var N6Ds = -0.125 * (r + 1) * (t + 1);
                var N6Dt = -0.125 * (r + 1) * (s - 1);
                var N7Dr = 0.125 * (s + 1) * (t + 1);
                var N7Ds = 0.125 * (r + 1) * (t + 1);
                var N7Dt = 0.125 * (r + 1) * (s + 1);
                var N8Dr = -0.125 * (s + 1) * (t + 1);
                var N8Ds = -0.125 * (r - 1) * (t + 1);
                var N8Dt = -0.125 * (r - 1) * (s + 1);

                var sfDr = new List<double> // shape functions differentiated on r
                    {
                        N1Dr, N2Dr, N3Dr, N4Dr, N5Dr, N6Dr, N7Dr, N8Dr
                    };
                var sfDs = new List<double>
                    {
                        N1Ds, N2Ds, N3Ds, N4Ds, N5Ds, N6Ds, N7Ds, N8Ds
                    };
                var sfDt = new List<double>
                    {
                        N1Dt, N2Dt, N3Dt, N4Dt, N5Dt, N6Dt, N7Dt, N8Dt
                    };

                // Evaluates each partial derivative in the isoparametric node
                var calcDerivs = new List<Double>
                    {
                        MultiplyLists(gX, sfDr),
                        MultiplyLists(gX, sfDs),
                        MultiplyLists(gX, sfDt),

                        MultiplyLists(gY, sfDr),
                        MultiplyLists(gY, sfDs),
                        MultiplyLists(gY, sfDt),

                        MultiplyLists(gZ, sfDr),
                        MultiplyLists(gZ, sfDs),
                        MultiplyLists(gZ, sfDt)
                    };

                // Helper function to piecewise multiply elements of two lists of length 8 and add them together
                double MultiplyLists(List<double> a, List<double> b)
                {
                    double sum = 0.0;
                    for (int i = 0; i < 8; i++)
                    {
                        sum += (a[i] * b[i]);
                    }
                    return sum;
                }

                // Structure data in the form of a Jacobian matrix
                Matrix<double> jacobianMatrix = DenseMatrixModule.ofArray2(new double[,]
                {
                        {calcDerivs[0], calcDerivs[3], calcDerivs[6] },
                        {calcDerivs[1], calcDerivs[4], calcDerivs[7] },
                        {calcDerivs[2], calcDerivs[5], calcDerivs[8] },
                });

                var jacobianDeterminant = jacobianMatrix.Determinant();
                jacobiansOfElement.Add(jacobianDeterminant);
                
            }
            
            double jacobianRatio = 0;
            // If any of the determinants are negative, we have to divide the maximum with the minimum
            if (jacobiansOfElement.Any(x => x < 0))
            {
                jacobianRatio = jacobiansOfElement.Max() / jacobiansOfElement.Min();

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"One or more Jacobian determinants of element {e.Id} is negative.");
                if (jacobianRatio < 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The Jacobian Ratio of element {e.Id} is negative.");
                }
            }
            else
            {
                jacobianRatio = jacobiansOfElement.Min() / jacobiansOfElement.Max();
            }

            return jacobianRatio;
        }

        /// <summary>
        /// Calculates the Jacobian ratio of a simple quadrilateral mesh element.
        /// </summary>
        /// <param name="e">An <see cref="Element"/> object describing a mesh face; see <see cref="Element"/> class for attributes.</param>
        /// <returns>A <see cref="double"/> between 0.0 and 1.0.</returns>
        double CalculateJacobianOfQuadElement(Element e)
        {
            /*
             * This method uses the idea of shape functions and natural coordinate system to 
             * calculate the Jacobian at given points on a simple quadrilateral element.
             * 
             * 1. Transform the input 3D quad element (and specifically the corner points) to a 2D space (X', Y', Z'=0).
             * 2. Define natural coordinates we want to calculate the Jacobian in. This could be the corner points (or 
             *    alternatively the Gauss points) of the quad element. 
             * 3. Calculate the Jacobian determinant of each point. 
             * 4. The ratio is the ratio of the minimum and maximum Jacobian calculated, given as a double from 0.0 to 1.0.
             *    A negative Jacobian indicates a self-intersecting or convex element and should not happen.
                */
            List<Point3d> cornerPoints = new List<Point3d>(){
                e.Node1.Coordinate, e.Node2.Coordinate, e.Node3.Coordinate, e.Node4.Coordinate
            };
            List<Point3d> localPoints = TransformQuadSurfaceTo2DPoints(cornerPoints);

            var gX = new List<Double>()
            {
                localPoints[0].X, localPoints[1].X, localPoints[2].X, localPoints[3].X,
            };
            var gY = new List<Double>()
            {
                localPoints[0].Y, localPoints[1].Y, localPoints[2].Y, localPoints[3].Y,
            };

            var naturalPoints = new List<List<Double>> // natural coordinates of corner points
            {
                new List<double>{ -1, -1}, new List<double> { 1, -1 }, new List<double> { 1, 1 }, new List<double> { -1, 1 }
            };

            #region Todo: Implement which points we want to evaluate the jacobians for (corner vs 4 gauss integration points)
            //double s = 0.57735; // this represents the Gauss point of an isoparametric quadrilateral element: sqrt(1/3)
            //var naturalGaussPoints = new List<List<Double>> // natural coordinates of Gauss points
            //{
            //    new List<double>{ -s, -s}, new List<double> { s, -s }, new List<double> { s, s }, new List<double> { -s, s }
            //};
            #endregion

            var jacobiansOfElement = new List<Double>();

            // Calculate the Jacobian determinant of each corner point
            for (int n=0; n<naturalPoints.Count; n++)
            {
                double nX = naturalPoints[n][0];
                double nY = naturalPoints[n][1];

                // See documentation for derivation of formula
                double jacobianDeterminantOfPoint = 0.0625 *
                    (
                    ((1 - nY) * (gX[1] - gX[0]) + (1 + nY) * (gX[2] - gX[3]))*
                    ((1 - nX) * (gY[3] - gY[0]) + (1 + nX) * (gY[2] - gY[1]))
                    -
                    ((1 - nY) * (gY[1] - gY[0]) + (1 + nY) * (gY[2] - gY[3])) *
                    ((1 - nX) * (gX[3] - gX[0]) + (1 + nX) * (gX[2] - gX[1]))
                    );

                jacobiansOfElement.Add(jacobianDeterminantOfPoint);
            };

            // Minimum element divided by maximum element. A value of 1 denotes a rectangular element.
            double jacobianRatio = jacobiansOfElement.Min() / jacobiansOfElement.Max();

            // Generate a warning if Jacobian ratio is outside of the valid range: [0.0, 1.0].
            if (jacobianRatio < 0 || 1.0 < jacobianRatio)
            {
                //throw new ArgumentOutOfRangeException(
                //    paramName: "jacobianRatio", jacobianRatio,
                //    message: "The Jacobian ratio is outside of the valid range [0.0, 1.0]. A negative value might indicate a concave or self-intersecting element.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The Jacobian ratio is outside of the valid range [0.0, 1.0]. A negative value might indicate a concave or self-intersecting element.");
            }

            return jacobianRatio;
        }

        /// <summary>
        /// Transforms the corner points of an arbitrary 3D quad surface to a 2D plane.
        /// </summary>
        /// <param name="cornerPoints">A list of <see cref="Point3d"/> containing the corner points of a mesh element.</param>
        /// <returns>A list of <see cref="Point3d"/> where the Z (third) coordinate is 0.</returns>
        List<Point3d> TransformQuadSurfaceTo2DPoints(List<Point3d> cornerPoints)
        {
            List<Point3d> planeCornerPoints = new List<Point3d>();
            if (Point3d.ArePointsCoplanar(cornerPoints, RhinoMath.ZeroTolerance))
            {
                planeCornerPoints = cornerPoints;
            } 
            else
            {
                // Calculate average surface normal based on corner points
                List<Vector3d> pointNormals = new List<Vector3d>();
                for (int i = 0; i<cornerPoints.Count(); i++)
                {
                    var n = (i + 1) % 4; // next point in quad
                    var p = (i + 3) % 4; // previous point in quad

                    var pointVectors = new List<Vector3d>()
                    {
                        new Vector3d(cornerPoints[n].X - cornerPoints[0].X, cornerPoints[n].Y - cornerPoints[0].Y, cornerPoints[n].Z - cornerPoints[0].Z),
                        new Vector3d(cornerPoints[p].X - cornerPoints[0].X, cornerPoints[p].Y - cornerPoints[0].Y, cornerPoints[p].Z - cornerPoints[0].Z)
                    };
                    pointNormals.Add(Vector3d.CrossProduct(pointVectors[0], pointVectors[1]));
                }
                Vector3d averageNormal = pointNormals[0] + pointNormals[1] + pointNormals[2] + pointNormals[3];

                // Calculate average point of all corner locations.
                double avgX, avgY, avgZ;
                avgX = avgY = avgZ = 0;

                foreach (Point3d point in cornerPoints)
                {
                    avgX += 0.25 * point.X;
                    avgY += 0.25 * point.Y;
                    avgZ += 0.25 * point.Z;
                }
                Point3d averagePoint = new Point3d(avgX, avgY, avgZ);

                // Create a plane: through the "average point" & along the average normal.
                Plane projectPlane = new Plane(averagePoint, averageNormal);

                // Project all points onto the plane along the average normal
                Transform planeProjectionTransformation = Transform.ProjectAlong(projectPlane, averageNormal);
                foreach (Point3d point in cornerPoints)
                {
                    planeCornerPoints.Add(planeProjectionTransformation * point);
                }
            }
            
            // Finally, transform the input points to the xy-plane. 
            Transform elementTransformation = GetTransformationPlanePointsToXYPlane(planeCornerPoints);
            List<Point3d> transformedPoints = new List<Point3d>();
            foreach (Point3d point in planeCornerPoints)
            {
                transformedPoints.Add(elementTransformation * point);
            }
            
            return transformedPoints;


            // Inner method for getting a Transform object/matrix for mapping plane points in 3D space to the xy-plane (z=0).
            Transform GetTransformationPlanePointsToXYPlane(List<Point3d> points)
            {
                var elementVectors = new List<Vector3d>
                {
                    new Vector3d(points[1].X - points[0].X, points[1].Y - points[0].Y, points[1].Z - points[0].Z),
                    new Vector3d(points.Last().X - points[0].X, points.Last().Y - points[0].Y, points.Last().Z - points[0].Z)
                };

                // Cross product of two linearily independent vectors is the normal to the plane containing them
                var surfaceNormal = Vector3d.CrossProduct(elementVectors[0], elementVectors[1]);

                var surfacePlane = new Plane(points[0], surfaceNormal);
                var xyPlane = new Plane(new Point3d(0, 0, 0), new Vector3d(0, 0, 1));

                return Transform.PlaneToPlane(surfacePlane, xyPlane);
            }
        }
        
        /// <summary>
        /// Color each mesh face based on a given quality type.
        /// </summary>
        /// <param name="colorMesh">The output colored <see cref="Mesh"/> object.</param>
        /// <param name="qualityList">List of <see cref="Quality"/> objects for each element in the mesh.</param>
        /// <param name="qualityCheckType">Which quality type to color the mesh with; 1 = AR, 2 = SK, 3 = J.</param>
        void ColorMesh(Mesh colorMesh, List<Quality> qualityList, int qualityCheckType)
        {
            switch (qualityCheckType)
            {
                // 1 = Aspect ratio
                case 1:
                    foreach (Quality q in qualityList)
                    {
                        if (q.AspectRatio > 0.9)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Green);
                        }
                        else if (q.AspectRatio > 0.7)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Yellow);
                        }
                        else if (q.AspectRatio > 0.6)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Orange);
                        }
                        else if (q.AspectRatio > 0)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Red);
                        }
                        colorMesh.Append(q.element.mesh);
                    }
                    break;
                // 2 = Skewness
                case 2:
                    foreach (Quality q in qualityList)
                    {
                        if (q.Skewness > 0.9)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Green);
                        }
                        else if (q.Skewness > 0.75)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Yellow);
                        }
                        else if (q.Skewness > 0.6)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Orange);
                        }
                        else if (q.Skewness > 0)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Red);
                        }
                        colorMesh.Append(q.element.mesh);
                    }
                    break;
                // 3 = Jacobian
                case 3:
                    foreach (Quality q in qualityList)
                    {
                        if (q.JacobianRatio > 0.8)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Green);
                        }
                        else if (q.JacobianRatio > 0.5)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Yellow);
                        }
                        else if (q.JacobianRatio > 0.03)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Orange);
                        }
                        else if (q.JacobianRatio >= 0)
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.Red);
                        }
                        else
                        {
                            q.element.mesh.VertexColors.CreateMonotoneMesh(Color.HotPink); // invalid mesh
                        }
                        colorMesh.Append(q.element.mesh);
                    }
                    break;
            }
        }
        #endregion

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.MeshQuality;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("31a76520-576a-4b37-a64f-8d51178e4be7"); }
        }
    }
}