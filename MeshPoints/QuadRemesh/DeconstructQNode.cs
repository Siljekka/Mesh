﻿using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using MeshPoints.Classes;

namespace MeshPoints.QuadRemesh
{
    public class DeconstructQNode : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DeconstructQNode class.
        /// </summary>
        public DeconstructQNode()
          : base("Deconstruct qNode", "dqn",
              "Deconstruct qNode class",
              "MyPlugIn", "QuadRemesh")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("qNode", "qel", "Input qNode class", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Point","pt","Coordinate of node", GH_ParamAccess.item);
            pManager.AddGenericParameter("Topology vertex index", "tv", "Vertex index in topology", GH_ParamAccess.item);
            pManager.AddGenericParameter("Mesh vertex index", "mv", "Vertex index in mesh", GH_ParamAccess.item);
            pManager.AddGenericParameter("Adjacent edges", "ae", "Index of adjacent edges to the node", GH_ParamAccess.list);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            qNode node = new qNode();
            DA.GetData(0, ref node);
            DA.SetData(0, node.Coordinate);
            DA.SetData(1, node.TopologyVertexIndex);
            DA.SetData(2, node.MeshVertexIndex);
        }

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
            get { return new Guid("ca709769-2507-4b33-880c-faf88fc0d3ce"); }
        }
    }
}