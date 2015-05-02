﻿/*
Ferram Aerospace Research v0.14.6
Copyright 2014, Michael Ferrara, aka Ferram4

    This file is part of Ferram Aerospace Research.

    Ferram Aerospace Research is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Ferram Aerospace Research is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Ferram Aerospace Research.  If not, see <http://www.gnu.org/licenses/>.

    Serious thanks:		a.g., for tons of bugfixes and code-refactorings
            			Taverius, for correcting a ton of incorrect values
            			sarbian, for refactoring code for working with MechJeb, and the Module Manager 1.5 updates
            			ialdabaoth (who is awesome), who originally created Module Manager
                        Regex, for adding RPM support
            			Duxwing, for copy editing the readme
 * 
 * Kerbal Engineer Redux created by Cybutek, Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License
 *      Referenced for starting point for fixing the "editor click-through-GUI" bug
 *
 * Part.cfg changes powered by sarbian & ialdabaoth's ModuleManager plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/55219
 *
 * Toolbar integration powered by blizzy78's Toolbar plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/60863
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FerramAerospaceResearch.FARPartGeometry
{
    public class GeometryMesh
    {
        public Vector3[] vertices;
        public int[] triangles;
        public Transform meshTransform;
        public Matrix4x4 thisToVesselMatrix;
        public Bounds bounds;

        public GeometryMesh(Vector3[] untransformedVerts, int[] triangles, Bounds meshBounds, Transform meshTransform, Matrix4x4 worldToVesselMatrix)
        {
            vertices = new Vector3[untransformedVerts.Length];
            this.thisToVesselMatrix = worldToVesselMatrix * meshTransform.localToWorldMatrix;

            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = thisToVesselMatrix.MultiplyPoint3x4(untransformedVerts[i]);
            
            this.triangles = triangles;
            this.meshTransform = meshTransform;

            Vector3 size = thisToVesselMatrix.MultiplyVector(meshBounds.size);
            size.x = Math.Abs(size.x);
            size.y = Math.Abs(size.y);
            size.z = Math.Abs(size.z);

            bounds = TransformBounds(meshBounds, thisToVesselMatrix);
        }

        public bool TryTransformBasis(Matrix4x4 newThisToVesselMatrix)
        {
            if (meshTransform == null)
                return false;

            Matrix4x4 tempMatrix = thisToVesselMatrix.inverse;
            thisToVesselMatrix = newThisToVesselMatrix * meshTransform.localToWorldMatrix;

            tempMatrix = thisToVesselMatrix *  tempMatrix;

            bounds = TransformBounds(bounds, tempMatrix);

            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = tempMatrix.MultiplyPoint3x4(vertices[i]);

            return true;
        }

        private Bounds TransformBounds(Bounds oldBounds, Matrix4x4 matrix)
        {
            Bounds bounds = new Bounds();
            Vector3 center, extents;
            center = oldBounds.center;//matrix.MultiplyPoint3x4(m.bounds.center);
            extents = oldBounds.extents;//matrix.MultiplyVector(m.bounds.size);

            /*size.x = Math.Abs(size.x);
            size.y = Math.Abs(size.y);
            size.z = Math.Abs(size.z);*/

            Vector3 boundPt;
            boundPt = center + extents;
            boundPt = matrix.MultiplyPoint3x4(boundPt);
            bounds.Encapsulate(boundPt);

            boundPt = center - extents;
            boundPt = matrix.MultiplyPoint3x4(boundPt);
            bounds.Encapsulate(boundPt);

            boundPt = center;
            boundPt.x += extents.x;
            boundPt.y += extents.y;
            boundPt.z -= extents.z;
            boundPt = matrix.MultiplyPoint3x4(boundPt);
            bounds.Encapsulate(boundPt);

            boundPt = center;
            boundPt.x += extents.x;
            boundPt.y -= extents.y;
            boundPt.z += extents.z;
            boundPt = matrix.MultiplyPoint3x4(boundPt);
            bounds.Encapsulate(boundPt);

            boundPt = center;
            boundPt.x -= extents.x;
            boundPt.y += extents.y;
            boundPt.z += extents.z;
            boundPt = matrix.MultiplyPoint3x4(boundPt);
            bounds.Encapsulate(boundPt);

            boundPt = center;
            boundPt.x -= extents.x;
            boundPt.y -= extents.y;
            boundPt.z += extents.z;
            boundPt = matrix.MultiplyPoint3x4(boundPt);
            bounds.Encapsulate(boundPt);

            boundPt = center;
            boundPt.x -= extents.x;
            boundPt.y += extents.y;
            boundPt.z -= extents.z;
            boundPt = matrix.MultiplyPoint3x4(boundPt);
            bounds.Encapsulate(boundPt);

            boundPt = center;
            boundPt.x += extents.x;
            boundPt.y -= extents.y;
            boundPt.z -= extents.z;
            boundPt = matrix.MultiplyPoint3x4(boundPt);

            bounds.Encapsulate(boundPt);

            return bounds;
        }
    }
}