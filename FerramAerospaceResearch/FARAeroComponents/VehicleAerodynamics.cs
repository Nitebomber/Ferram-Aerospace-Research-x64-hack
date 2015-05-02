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
using System.Threading;
using UnityEngine;
using FerramAerospaceResearch.FARPartGeometry;

namespace FerramAerospaceResearch.FARAeroComponents
{
    class VehicleAerodynamics
    {
        VehicleVoxel _voxel = null;
        VoxelCrossSection[] _vehicleCrossSection = new VoxelCrossSection[1];
        int _voxelCount;

        double _length = 0;
        public double Length
        {
            get { return _length; }
        }

        double _maxCrossSectionArea = 0;
        public double MaxCrossSectionArea
        {
            get { return _maxCrossSectionArea; }
        }

        bool _calculationCompleted = false;
        public bool CalculationCompleted
        {
            get { return _calculationCompleted; }
        }

        double _sonicDragArea;
        public double SonicDragArea
        {
            get { return _sonicDragArea; }
        }

        double _criticalMach;
        public double CriticalMach
        {
            get { return _criticalMach; }
        }

        Matrix4x4 _worldToLocalMatrix, _localToWorldMatrix;

        Vector3d _voxelLowerRightCorner;
        double _sectionThickness;
        public double SectionThickness
        {
            get { return _sectionThickness; }
        }

        Vector3 _vehicleMainAxis;
        List<Part> _vehiclePartList;

        List<GeometryPartModule> _currentGeoModules;
        Dictionary<Part, PartTransformInfo> _partWorldToLocalMatrix = new Dictionary<Part, PartTransformInfo>();
        Dictionary<FARAeroPartModule, FARAeroPartModule.ProjectedArea> _moduleAndAreas = new Dictionary<FARAeroPartModule, FARAeroPartModule.ProjectedArea>();

        List<FARAeroPartModule> _currentAeroModules;
        List<FARAeroPartModule> _newAeroModules;

        List<FARAeroPartModule> _currentUnusedAeroModules;
        List<FARAeroPartModule> _newUnusedAeroModules;

        List<FARAeroSection> _currentAeroSections;
        List<FARAeroSection> _newAeroSections;

        int validSectionCount;
        int firstSection;

        bool visualizing = false;
        bool voxelizing = false;

        public void GetNewAeroData(out List<FARAeroPartModule> aeroModules, out List<FARAeroPartModule> unusedAeroModules, out List<FARAeroSection> aeroSections)
        {
            _calculationCompleted = false;
            aeroModules = _currentAeroModules = _newAeroModules;

            aeroSections = _currentAeroSections = _newAeroSections;

            unusedAeroModules = _currentUnusedAeroModules = _newUnusedAeroModules;

            LEGACY_UpdateWingAerodynamicModels();
        }

        public void GetNewAeroData(out List<FARAeroPartModule> aeroModules, out List<FARAeroSection> aeroSections)
        {
            _calculationCompleted = false;
            aeroModules = _currentAeroModules = _newAeroModules;

            aeroSections = _currentAeroSections = _newAeroSections;

            LEGACY_UpdateWingAerodynamicModels();
        }

        public Matrix4x4 VoxelAxisToLocalCoordMatrix()
        {
            return Matrix4x4.TRS(Vector3.zero, Quaternion.FromToRotation(_vehicleMainAxis, Vector3.up), Vector3.one);
        }

        public double FirstSectionXOffset()
        {
            double offset = Vector3d.Dot(_vehicleMainAxis, _voxelLowerRightCorner);
            offset += firstSection * _sectionThickness;

            return offset;
        }

        public double[] GetCrossSectionAreas()
        {
            double[] areas = new double[validSectionCount];
            return GetCrossSectionAreas(areas);
        }

        public double[] GetCrossSectionAreas(double[] areas)
        {
            for (int i = firstSection; i < validSectionCount + firstSection; i++)
                areas[i - firstSection] = _vehicleCrossSection[i].area;

            return areas;
        }

        public double[] GetCrossSection2ndAreaDerivs()
        {
            double[] areaDerivs = new double[validSectionCount];
            return GetCrossSection2ndAreaDerivs(areaDerivs);
        }

        public double[] GetCrossSection2ndAreaDerivs(double[] areaDerivs)
        {
            for (int i = firstSection; i < validSectionCount + firstSection; i++)
                areaDerivs[i - firstSection] = _vehicleCrossSection[i].secondAreaDeriv;

            return areaDerivs;
        }

        private void ClearDebugVoxel()
        {
            _voxel.ClearVisualVoxels();
            visualizing = false;
        }

        private void DisplayDebugVoxels(Matrix4x4 localToWorldMatrix)
        {
            _voxel.VisualizeVoxel(localToWorldMatrix);
            visualizing = true;
        }

        public void DebugVisualizeVoxels(Matrix4x4 localToWorldMatrix)
        {
            if (visualizing)
                ClearDebugVoxel();
            else
                DisplayDebugVoxels(localToWorldMatrix);
        }

        public bool TryVoxelUpdate(Matrix4x4 worldToLocalMatrix, Matrix4x4 localToWorldMatrix, int voxelCount, List<Part> vehiclePartList, List<GeometryPartModule> currentGeoModules, bool updateGeometryPartModules = true)
        {
            if (Monitor.TryEnter(this))
            {
                if (voxelizing)
                {
                    Monitor.Exit(this);
                    return false;
                }
                _voxelCount = voxelCount;

                this._worldToLocalMatrix = worldToLocalMatrix;
                this._localToWorldMatrix = localToWorldMatrix;
                this._vehiclePartList = vehiclePartList;
                this._currentGeoModules = currentGeoModules;

                _partWorldToLocalMatrix.Clear();

                for (int i = 0; i < _currentGeoModules.Count; i++)
                {
                    GeometryPartModule g = _currentGeoModules[i];
                    _partWorldToLocalMatrix.Add(g.part, new PartTransformInfo(g.part.transform));
                    if(updateGeometryPartModules)
                        g.UpdateTransformMatrixList(_worldToLocalMatrix);
                }

                this._vehicleMainAxis = CalculateVehicleMainAxis();

                if (_voxel != null)
                {
                    visualizing = false;
                    _voxel.CleanupVoxel();
                }

                visualizing = false;

                ThreadPool.QueueUserWorkItem(CreateVoxel);
                Monitor.Exit(this);
                return true;
            }
            else
                return false;
        }

        private void CreateVoxel(object nullObj)
        {
            try
            {
                lock (this)
                {
                    voxelizing = true;

                    _voxel = new VehicleVoxel(_vehiclePartList, _currentGeoModules, _voxelCount, true, true);
                    if (_vehicleCrossSection.Length < _voxel.MaxArrayLength)
                        _vehicleCrossSection = _voxel.EmptyCrossSectionArray;

                    _voxelLowerRightCorner = _voxel.LocalLowerRightCorner;

                    CalculateVesselAeroProperties();
                    _calculationCompleted = true;

                    voxelizing = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void LEGACY_UpdateWingAerodynamicModels()
        {
            for (int i = 0; i < _currentAeroModules.Count; i++)
            {
                Part p = _currentAeroModules[i].part;
                if (!p)
                    continue;
                ferram4.FARWingAerodynamicModel w = p.GetComponent<ferram4.FARWingAerodynamicModel>();
                if (w)
                    w.NUFAR_ClearAreaExposedFactor();
            }

            for (int i = 0; i < _currentAeroSections.Count; i++)
            {
                FARAeroSection sect = _currentAeroSections[i];
                sect.LEGACY_SetLiftForFARWingAerodynamicModel();
            }

            for (int i = 0; i < _currentAeroModules.Count; i++)
            {
                Part p = _currentAeroModules[i].part;
                if (!p)
                    continue;
                ferram4.FARWingAerodynamicModel w = p.GetComponent<ferram4.FARWingAerodynamicModel>();
                if (w)
                    w.NUFAR_SetExposedAreaFactor();
            }
        }

        private Vector3 CalculateVehicleMainAxis()
        {
            Vector3 axis = Vector3.zero;
            for (int i = 0; i < _currentGeoModules.Count; i++)      //get axis by averaging all parts up vectors
            {
                GeometryPartModule m = _currentGeoModules[i];
                if (m != null)
                {
                    Bounds b = m.overallMeshBounds;
                    Vector3 vec = m.part.transform.up;
                    ModuleResourceIntake intake = m.part.GetComponent<ModuleResourceIntake>();
                    if (intake)
                    {
                        Transform intakeTrans = m.part.FindModelTransform(intake.intakeTransformName);
                        if ((object)intakeTrans != null)
                            vec = intakeTrans.forward;
                    }
                    vec = _worldToLocalMatrix.MultiplyVector(vec);
                    vec.x = Math.Abs(vec.x);
                    vec.y = Math.Abs(vec.y);
                    vec.z = Math.Abs(vec.z);

                    axis += vec * b.size.x * b.size.y * b.size.z;    //scale part influence by approximate size
                }
            }
            axis.Normalize();   //normalize axis for later calcs
            float dotProdX, dotProdY, dotProdZ;

            //axis = _worldToLocalMatrix.MultiplyVector(axis);

            dotProdX = Math.Abs(Vector3.Dot(axis, Vector3.right));
            dotProdY = Math.Abs(Vector3.Dot(axis, Vector3.up));
            dotProdZ = Math.Abs(Vector3.Dot(axis, Vector3.forward));

            if (dotProdY > 2 * dotProdX && dotProdY > 2 * dotProdZ)
                return Vector3.up;

            if (dotProdX > 2 * dotProdY && dotProdX > 2 * dotProdZ)
                return Vector3.right;

            if (dotProdZ > 2 * dotProdX && dotProdZ > 2 * dotProdY)
                return Vector3.forward;

            //Otherwise, now we need to use axis, since it's obviously not close to anything else


            return axis;
        }

        void GaussianSmoothCrossSections(VoxelCrossSection[] vehicleCrossSection, double stdDevCutoff, double lengthPercentFactor, double sectionThickness, double length, int frontIndex, int backIndex, int areaSmoothingIterations, int derivSmoothingIterations)
        {
            double stdDev = length * lengthPercentFactor;
            int numVals = (int)Math.Ceiling(stdDevCutoff * stdDev / sectionThickness);

            double[] gaussianFactors = new double[numVals];
            double[] prevUncorrectedVals = new double[numVals];
            double[] futureUncorrectedVals = new double[numVals - 1];

            double invVariance = 1 / (stdDev * stdDev);

            //calculate Gaussian factors for each of the points that will be hit
            for(int i = 0; i < gaussianFactors.Length; i++)
            {
                double factor = (i * sectionThickness);
                factor *= factor;
                gaussianFactors[i] = Math.Exp(-0.5 * factor * invVariance);
            }

            //then sum them up...
            double sum = 0;
            for (int i = 0; i < gaussianFactors.Length; i++)
                if (i == 0)
                    sum += gaussianFactors[i];
                else
                    sum += 2 * gaussianFactors[i];

            double invSum = 1 / sum;    //and then use that to normalize the factors

            for (int i = 0; i < gaussianFactors.Length; i++)
            {
                gaussianFactors[i] *= invSum;
            }



            //first smooth the area itself.  This has a greater effect on the 2nd deriv due to the effect of noise on derivatives
            for (int j = 0; j < areaSmoothingIterations; j++)
            {
                for (int i = 0; i < prevUncorrectedVals.Length; i++)
                    prevUncorrectedVals[i] = 0;     //set all the vals to 0 to prevent screwups between iterations

                for (int i = frontIndex; i <= backIndex; i++)       //area smoothing pass
                {
                    for (int k = prevUncorrectedVals.Length - 1; k > 0; k--)
                    {
                        prevUncorrectedVals[k] = prevUncorrectedVals[k - 1];        //shift prev vals down
                    }
                    double curValue = vehicleCrossSection[i].area;
                    prevUncorrectedVals[0] = curValue;       //and set the central value


                    for (int k = 0; k < futureUncorrectedVals.Length; k++)          //update future vals
                    {
                        if (i + k < backIndex)
                            futureUncorrectedVals[k] = vehicleCrossSection[i + k + 1].area;
                        else
                            futureUncorrectedVals[k] = 0;
                    }
                    curValue = 0;       //zero for coming calculations...

                    double borderScaling = 1;      //factor to correct for the 0s lurking at the borders of the curve...
                    
                    for (int k = 0; k < prevUncorrectedVals.Length; k++)
                    {
                        double val = prevUncorrectedVals[k];
                        double gaussianFactor = gaussianFactors[k];

                        curValue += gaussianFactor * val;        //central and previous values;
                        if (val == 0)
                            borderScaling -= gaussianFactor;
                    }
                    for (int k = 0; k < futureUncorrectedVals.Length; k++)
                    {
                        double val = futureUncorrectedVals[k];
                        double gaussianFactor = gaussianFactors[k + 1];

                        curValue += gaussianFactor * val;      //future values
                        if (val == 0)
                            borderScaling -= gaussianFactor;
                    }
                    if (borderScaling > 0)
                        curValue /= borderScaling;      //and now all of the 0s beyond the edge have been removed

                    vehicleCrossSection[i].area = curValue;
                }
            }

            //2nd derivs must be recalculated now using the adjusted areas
            double denom = sectionThickness;
            denom *= denom;
            denom = 1 / denom;

            for (int i = frontIndex; i <= backIndex; i++)       //calculate 2nd derivs, raw
            {
                double areaM1, area0, areaP1;

                if(i == frontIndex)     //forward difference for frontIndex
                {
                    areaM1 = vehicleCrossSection[i].area;
                    area0 = vehicleCrossSection[i + 1].area;
                    areaP1 = vehicleCrossSection[i + 2].area;
                }
                else if (i == backIndex) //backward difference for backIndex
                {
                    areaM1 = vehicleCrossSection[i - 2].area;
                    area0 = vehicleCrossSection[i - 1].area;
                    areaP1 = vehicleCrossSection[i].area;
                }
                else                     //central difference for all others
                {
                    areaM1 = vehicleCrossSection[i - 1].area;
                    area0 = vehicleCrossSection[i].area;
                    areaP1 = vehicleCrossSection[i + 1].area;
                }

                double areaSecondDeriv = (areaM1 + areaP1) - 2 * area0;
                areaSecondDeriv *= denom;

                vehicleCrossSection[i].secondAreaDeriv = areaSecondDeriv;
            }

            //and now smooth the derivs
            for (int j = 0; j < derivSmoothingIterations; j++)
            {
                for (int i = 0; i < prevUncorrectedVals.Length; i++)
                    prevUncorrectedVals[i] = 0;     //set all the vals to 0 to prevent screwups between iterations

                for (int i = frontIndex; i <= backIndex; i++)       //deriv smoothing pass
                {
                    for (int k = prevUncorrectedVals.Length - 1; k > 0; k--)
                    {
                        prevUncorrectedVals[k] = prevUncorrectedVals[k - 1];        //shift prev vals down
                    }
                    double curValue = vehicleCrossSection[i].secondAreaDeriv;
                    prevUncorrectedVals[0] = curValue;       //and set the central value


                    for (int k = 0; k < futureUncorrectedVals.Length; k++)          //update future vals
                    {
                        if (i + k < backIndex)
                            futureUncorrectedVals[k] = vehicleCrossSection[i + k + 1].secondAreaDeriv;
                        else
                            futureUncorrectedVals[k] = 0;
                    }
                    curValue = 0;       //zero for coming calculations...

                    double borderScaling = 1;      //factor to correct for the 0s lurking at the borders of the curve...

                    for (int k = 0; k < prevUncorrectedVals.Length; k++)
                    {
                        double val = prevUncorrectedVals[k];
                        double gaussianFactor = gaussianFactors[k];

                        curValue += gaussianFactor * val;        //central and previous values;
                        if (val == 0)
                            borderScaling -= gaussianFactor;
                    }
                    for (int k = 0; k < futureUncorrectedVals.Length; k++)
                    {
                        double val = futureUncorrectedVals[k];
                        double gaussianFactor = gaussianFactors[k + 1];

                        curValue += gaussianFactor * val;      //future values
                        if (val == 0)
                            borderScaling -= gaussianFactor;
                    }
                    if (borderScaling > 0)
                        curValue /= borderScaling;      //and now all of the 0s beyond the edge have been removed

                    vehicleCrossSection[i].secondAreaDeriv = curValue;
                }
            }
        }

        #region Aerodynamics Calculations

        private void CalculateVesselAeroProperties()
        {
            int front, back, numSections;

            _voxel.CrossSectionData(_vehicleCrossSection, _vehicleMainAxis, out front, out back, out _sectionThickness, out _maxCrossSectionArea);

            numSections = back - front;
            _length = _sectionThickness * numSections;

            GaussianSmoothCrossSections(_vehicleCrossSection, 3, FARSettingsScenarioModule.Settings.gaussianVehicleLengthFractionForSmoothing, _sectionThickness, _length, front, back, FARSettingsScenarioModule.Settings.numAreaSmoothingPasses, FARSettingsScenarioModule.Settings.numDerivSmoothingPasses);

            validSectionCount = numSections;
            firstSection = front;
            double invMaxRadFactor = 1f / Math.Sqrt(_maxCrossSectionArea / Math.PI);

            double finenessRatio = _sectionThickness * numSections * 0.5 * invMaxRadFactor;       //vehicle length / max diameter, as calculated from sect thickness * num sections / (2 * max radius) 

            //skin friction and pressure drag for a body, taken from 1978 USAF Stability And Control DATCOM, Section 4.2.3.1, Paragraph A
            double viscousDragFactor = 0;
            viscousDragFactor = 60 / (finenessRatio * finenessRatio * finenessRatio) + 0.0025 * finenessRatio;     //pressure drag for a subsonic / transonic body due to skin friction
            viscousDragFactor++;

            viscousDragFactor /= (double)numSections;   //fraction of viscous drag applied to each section

            double criticalMachNumber = CalculateCriticalMachNumber(finenessRatio);

            _criticalMach = criticalMachNumber * CriticalMachFactorForUnsmoothCrossSection(_vehicleCrossSection, finenessRatio, _sectionThickness);

            _moduleAndAreas.Clear();
            _newAeroSections = new List<FARAeroSection>();
            List<FARAeroPartModule> includedModules = new List<FARAeroPartModule>();
            List<float> weighting = new List<float>();
            HashSet<FARAeroPartModule> tmpAeroModules = new HashSet<FARAeroPartModule>();
            _sonicDragArea = 0;
            for (int i = 0; i <= numSections; i++)  //index in the cross sections
            {
                int index = i + front;      //index along the actual body

                double prevArea, curArea, nextArea;

                curArea = _vehicleCrossSection[index].area;
                if (i == 0)
                    prevArea = 0;
                else
                    prevArea = _vehicleCrossSection[index - 1].area;
                if (i == numSections)
                    nextArea = 0;
                else
                    nextArea = _vehicleCrossSection[index + 1].area;

                FloatCurve xForcePressureAoA0 = new FloatCurve();
                FloatCurve xForcePressureAoA180 = new FloatCurve();

                FloatCurve xForceSkinFriction = new FloatCurve();

                //Potential and Viscous lift calcs
                float potentialFlowNormalForce;
                if(i == 0)
                    potentialFlowNormalForce = (float)(nextArea - curArea);
                else if(i == numSections)
                    potentialFlowNormalForce = (float)(curArea - prevArea);
                else
                    potentialFlowNormalForce = (float)(nextArea - prevArea) * 0.5f;      //calcualted from area change

                float areaChangeMax = (float)Math.Min(nextArea, prevArea) * 0.1f;

                float sonicBaseDrag = 0.21f;

                sonicBaseDrag *= potentialFlowNormalForce;    //area base drag acts over

                if (potentialFlowNormalForce > areaChangeMax)
                    potentialFlowNormalForce = areaChangeMax;
                else if (potentialFlowNormalForce < -areaChangeMax)
                    potentialFlowNormalForce = -areaChangeMax;
                else
                    sonicBaseDrag *= Math.Abs(potentialFlowNormalForce / areaChangeMax);      //some scaling for small changes in cross-section

                double flatnessRatio = _vehicleCrossSection[index].flatnessRatio;
                if (flatnessRatio >= 1)
                    sonicBaseDrag /= (float)flatnessRatio;
                else
                    sonicBaseDrag *= (float)flatnessRatio;

                sonicBaseDrag = Math.Abs(sonicBaseDrag);

                Dictionary<Part, VoxelCrossSection.SideAreaValues> includedPartsAndAreas = _vehicleCrossSection[index].partSideAreaValues;

                double surfaceArea = 0;
                foreach (KeyValuePair<Part, VoxelCrossSection.SideAreaValues> pair in includedPartsAndAreas)
                {
                    VoxelCrossSection.SideAreaValues areas = pair.Value;
                    surfaceArea += areas.iN + areas.iP + areas.jN + areas.jP + areas.kN + areas.kP;
                }

                float viscCrossflowDrag = (float)(Math.Sqrt(curArea / Math.PI) * _sectionThickness * 2d);

                xForceSkinFriction.Add(0f, (float)(surfaceArea * viscousDragFactor), 0, 0);   //subsonic incomp visc drag
                xForceSkinFriction.Add(1f, (float)(surfaceArea * viscousDragFactor), 0, 0);   //transonic visc drag
                xForceSkinFriction.Add(2f, (float)surfaceArea, 0, 0);                     //above Mach 1.4, visc is purely surface drag, no pressure-related components simulated

                float sonicWaveDrag = (float)CalculateTransonicWaveDrag(i, index, numSections, front, _sectionThickness, Math.Min(_maxCrossSectionArea * 2, curArea * 16));//Math.Min(maxCrossSectionArea * 0.1, curArea * 0.25));
                sonicWaveDrag *= (float)FARSettingsScenarioModule.Settings.fractionTransonicDrag;     //this is just to account for the higher drag being felt due to the inherent blockiness of the model being used and noise introduced by the limited control over shape and through voxelization
                float hypersonicDragForward = (float)CalculateHypersonicDrag(prevArea, curArea, _sectionThickness);
                float hypersonicDragBackward = (float)CalculateHypersonicDrag(nextArea, curArea, _sectionThickness);

                float hypersonicMomentForward = (float)CalculateHypersonicMoment(prevArea, curArea, _sectionThickness);
                float hypersonicMomentBackward = (float)CalculateHypersonicMoment(nextArea, curArea, _sectionThickness);

                xForcePressureAoA0.Add(35f, hypersonicDragForward, 0f, 0f);
                xForcePressureAoA180.Add(35f, -hypersonicDragBackward, 0f, 0f);

                float sonicAoA0Drag, sonicAoA180Drag;
                if (sonicBaseDrag > 0)      //occurs with increase in area; force applied at 180 AoA
                {
                    xForcePressureAoA0.Add((float)criticalMachNumber, hypersonicDragForward * 0.4f, 0f, 0f);    //hypersonic drag used as a proxy for effects due to flow separation
                    xForcePressureAoA180.Add((float)criticalMachNumber, (sonicBaseDrag * 0.25f - hypersonicDragBackward * 0.4f), 0f, 0f);

                    sonicAoA0Drag = sonicWaveDrag +hypersonicDragForward * 0.1f;
                    sonicAoA180Drag = -sonicWaveDrag + sonicBaseDrag -hypersonicDragBackward * 0.1f + sonicBaseDrag;
                    //xForcePressureAoA0.Add(1f, sonicWaveDrag + hypersonicDragForward * 0.1f, 0f, 0f);     //positive is force forward; negative is force backward
                    //xForcePressureAoA180.Add(1f, -sonicWaveDrag - hypersonicDragBackward * 0.1f + sonicBaseDrag, 0f, 0f);
                }
                else if (sonicBaseDrag < 0)
                {
                    xForcePressureAoA0.Add((float)criticalMachNumber, (-sonicBaseDrag * 0.25f + hypersonicDragForward * 0.4f), 0f, 0f);
                    xForcePressureAoA180.Add((float)criticalMachNumber, -hypersonicDragBackward * 0.4f, 0f, 0f);

                    sonicAoA0Drag = sonicWaveDrag - sonicBaseDrag + hypersonicDragForward * 0.1f - sonicBaseDrag;
                    sonicAoA180Drag = -sonicWaveDrag - hypersonicDragBackward * 0.1f;

                    //xForcePressureAoA0.Add(1f, sonicWaveDrag + hypersonicDragForward * 0.1f - sonicBaseDrag, 0f, 0f);     //positive is force forward; negative is force backward
                    //xForcePressureAoA180.Add(1f, -sonicWaveDrag - hypersonicDragBackward * 0.1f, 0f, 0f);
                }
                else
                {
                    xForcePressureAoA0.Add((float)criticalMachNumber, hypersonicDragForward * 0.4f, 0f, 0f);
                    xForcePressureAoA180.Add((float)criticalMachNumber, -hypersonicDragBackward * 0.4f, 0f, 0f);

                    sonicAoA0Drag = sonicWaveDrag + hypersonicDragForward * 0.1f;
                    sonicAoA180Drag = -sonicWaveDrag - hypersonicDragBackward * 0.1f;

                    //xForcePressureAoA0.Add(1f, sonicWaveDrag + hypersonicDragForward * 0.1f, 0f, 0f);     //positive is force forward; negative is force backward
                    //xForcePressureAoA180.Add(1f, -sonicWaveDrag - hypersonicDragBackward * 0.1f, 0f, 0f);
                }
                _sonicDragArea -= sonicAoA0Drag;
                float diffSonicHyperAoA0 = Math.Abs(sonicAoA0Drag) - Math.Abs(hypersonicDragForward);
                float diffSonicHyperAoA180 = Math.Abs(sonicAoA180Drag) - Math.Abs(hypersonicDragBackward);


                xForcePressureAoA0.Add(1f, sonicAoA0Drag, 0, 0);
                xForcePressureAoA180.Add(1f, sonicAoA180Drag, 0, 0);

                xForcePressureAoA0.Add(2f, sonicAoA0Drag * 0.5773503f + (1 - 0.5773503f) * hypersonicDragForward);
                xForcePressureAoA180.Add(2f, sonicAoA180Drag * 0.5773503f - (1 - 0.5773503f) * hypersonicDragBackward);

                xForcePressureAoA0.Add(5f, sonicAoA0Drag * 0.2041242f + (1 - 0.2041242f) * hypersonicDragForward, -0.04252587f * diffSonicHyperAoA0, -0.04252587f * diffSonicHyperAoA0);
                xForcePressureAoA180.Add(5f, sonicAoA180Drag * 0.2041242f - (1 - 0.2041242f) * hypersonicDragBackward, -0.04252587f * diffSonicHyperAoA180, -0.04252587f * diffSonicHyperAoA180);

                xForcePressureAoA0.Add(10f, sonicAoA0Drag * 0.1005038f + (1 - 0.1005038f) * hypersonicDragForward, -0.0101519f * diffSonicHyperAoA0, -0.0101519f * diffSonicHyperAoA0);
                xForcePressureAoA180.Add(10f, sonicAoA180Drag * 0.1005038f - (1 - 0.1005038f) * hypersonicDragBackward, -0.0101519f * diffSonicHyperAoA180, -0.0101519f * diffSonicHyperAoA180);

                Vector3 xRefVector;
                if (index == front || index == back)
                    xRefVector = _vehicleMainAxis;
                else
                {
                    xRefVector = (Vector3)(_vehicleCrossSection[index - 1].centroid - _vehicleCrossSection[index + 1].centroid).normalized;
                    Vector3 offMainAxisVec = Vector3.ProjectOnPlane(xRefVector, _vehicleMainAxis);
                    float tanAoA = offMainAxisVec.magnitude / (2f * (float)_sectionThickness);
                    if (tanAoA > 0.17632698070846497347109038686862f)
                    {
                        offMainAxisVec.Normalize();
                        offMainAxisVec *= 0.17632698070846497347109038686862f;
                        xRefVector = _vehicleMainAxis + offMainAxisVec;
                        xRefVector.Normalize();
                    }
                }

                Vector3 nRefVector = Matrix4x4.TRS(Vector3.zero, Quaternion.FromToRotation(_vehicleMainAxis, xRefVector), Vector3.one).MultiplyVector(_vehicleCrossSection[index].flatNormalVector);

                Vector3 centroid = _localToWorldMatrix.MultiplyPoint3x4(_vehicleCrossSection[index].centroid);
                xRefVector = _localToWorldMatrix.MultiplyVector(xRefVector);
                nRefVector = _localToWorldMatrix.MultiplyVector(nRefVector);


                float weightingFactor = 0;

                //weight the forces applied to each part
                foreach (KeyValuePair<Part, VoxelCrossSection.SideAreaValues> pair in includedPartsAndAreas)
                {
                    Part key = pair.Key;
                    if (key == null)
                        continue;

                    if (!key.Modules.Contains("FARAeroPartModule"))
                        continue;

                    FARAeroPartModule m = (FARAeroPartModule)key.Modules["FARAeroPartModule"];
                    if (m != null)
                        includedModules.Add(m);

                    if (_moduleAndAreas.ContainsKey(m))
                        _moduleAndAreas[m] += pair.Value;
                    else
                        _moduleAndAreas[m] = new FARAeroPartModule.ProjectedArea() + pair.Value;

                    weightingFactor += (float)pair.Value.count;
                    weighting.Add((float)pair.Value.count);
                }
                weightingFactor = 1 / weightingFactor;
                for (int j = 0; j < includedModules.Count; j++)
                {
                    weighting[j] *= weightingFactor;
                }


                FARAeroSection section = new FARAeroSection(xForcePressureAoA0, xForcePressureAoA180, xForceSkinFriction, potentialFlowNormalForce, viscCrossflowDrag
                    ,viscCrossflowDrag / (float)(_sectionThickness), (float)flatnessRatio, hypersonicMomentForward, hypersonicMomentBackward,
                    centroid, xRefVector, nRefVector, _localToWorldMatrix, _vehicleMainAxis, includedModules, includedPartsAndAreas, weighting, _partWorldToLocalMatrix);

                _newAeroSections.Add(section);

                for (int j = 0; j < includedModules.Count; j++)
                {
                    FARAeroPartModule a = includedModules[j];
                    tmpAeroModules.Add(a);
                }

                includedModules.Clear();
                weighting.Clear();

            }
            foreach(KeyValuePair<FARAeroPartModule, FARAeroPartModule.ProjectedArea> pair in _moduleAndAreas)
            {
                pair.Key.SetProjectedArea(pair.Value, _localToWorldMatrix);
            }
            _newAeroModules = tmpAeroModules.ToList();

            _newUnusedAeroModules = new List<FARAeroPartModule>();

            for (int i = 0; i < _currentGeoModules.Count; i++)
            {
                FARAeroPartModule aeroModule = _currentGeoModules[i].GetComponent<FARAeroPartModule>();
                if (aeroModule != null && !tmpAeroModules.Contains(aeroModule))
                    _newUnusedAeroModules.Add(aeroModule);
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                _voxel.CleanupVoxel();
                _voxel = null;
            }
        }

        private double CalculateHypersonicMoment(double lowArea, double highArea, double sectionThickness)
        {
            if(lowArea >= highArea)
                return 0;

            double r1, r2;
            r1 = Math.Sqrt(lowArea / Math.PI);
            r2 = Math.Sqrt(highArea / Math.PI);

            double moment = r2 * r2 + r1 * r1 + sectionThickness * sectionThickness * 0.5;
            moment *= 2 * Math.PI;

            double radDiffSq = (r2 - r1);
            radDiffSq *= radDiffSq;

            moment *= radDiffSq;
            moment /= sectionThickness * sectionThickness + radDiffSq;

            return -moment * sectionThickness;
        }

        private double CalculateHypersonicDrag(double lowArea, double highArea, double sectionThickness)
        {
            if (lowArea >= highArea)
                return 0;

            double r1, r2;
            r1 = Math.Sqrt(lowArea / Math.PI);
            r2 = Math.Sqrt(highArea / Math.PI);

            double radDiffSq = r2 - r1;
            radDiffSq *= radDiffSq;

            double drag = sectionThickness * sectionThickness + radDiffSq;
            drag = 2d * Math.PI / drag;
            drag *= radDiffSq * radDiffSq;

            return -drag;        //force is negative 
        }

        private double CalcAllTransonicWaveDrag(VoxelCrossSection[] sections, int front, int numSections, double sectionThickness)
        {
            double drag = 0;
            double Lj;

            for(int j = 0; j < numSections; j++)
            {
                double accumulator = 0;

                Lj = (j + 0.5) * Math.Log(j + 0.5);
                if (j > 0)
                    Lj -= (j - 0.5) * Math.Log(j - 0.5);

                for(int i = j; i < numSections; i++)
                {
                    accumulator += sections[front + i].secondAreaDeriv * sections[front + i - j].secondAreaDeriv;
                }

                drag += accumulator * Lj;
            }

            drag *= -sectionThickness * sectionThickness / Math.PI;
            return drag;
        }

        private double CalculateTransonicWaveDrag(int i, int index, int numSections, int front, double sectionThickness, double cutoff)
        {
            double currentSectAreaCrossSection = MathClampAbs(_vehicleCrossSection[index].secondAreaDeriv, cutoff);

            if (currentSectAreaCrossSection == 0)       //quick escape for 0 cross-section section drag
                return 0;

            double lj2ndTerm = 0;
            double drag = 0;
            int limDoubleDrag = Math.Min(i, numSections - i);
            double sectionThicknessSq = sectionThickness * sectionThickness;

            double lj3rdTerm = Math.Log(sectionThickness) - 1;

            lj2ndTerm = 0.5 * Math.Log(0.5);
            drag = currentSectAreaCrossSection * (lj2ndTerm + lj3rdTerm);


            for (int j = 1; j <= limDoubleDrag; j++)      //section of influence from ahead and behind
            {
                double thisLj = (j + 0.5) * Math.Log(j + 0.5);
                double tmp = thisLj;
                thisLj -= lj2ndTerm;
                lj2ndTerm = tmp;

                tmp = MathClampAbs(_vehicleCrossSection[index + j].secondAreaDeriv, cutoff);
                tmp += MathClampAbs(_vehicleCrossSection[index - j].secondAreaDeriv, cutoff);

                thisLj += lj3rdTerm;

                drag += tmp * thisLj;
            }
            if (i < numSections - i)
            {
                for (int j = 2 * i + 1; j < numSections; j++)
                {
                    double thisLj = (j - i + 0.5) * Math.Log(j - i + 0.5);
                    double tmp = thisLj;
                    thisLj -= lj2ndTerm;
                    lj2ndTerm = tmp;

                    tmp = MathClampAbs(_vehicleCrossSection[j + front].secondAreaDeriv, cutoff);

                    thisLj += lj3rdTerm;

                    drag += tmp * thisLj;
                }
            }
            else if (i > numSections - i)
            {
                for (int j = numSections - 2 * i - 2; j >= 0; j--)
                {
                    double thisLj = (i - j + 0.5) * Math.Log(i - j + 0.5);
                    double tmp = thisLj;
                    thisLj -= lj2ndTerm;
                    lj2ndTerm = tmp;

                    tmp = MathClampAbs(_vehicleCrossSection[j + front].secondAreaDeriv, cutoff);

                    thisLj += lj3rdTerm;

                    drag += tmp * thisLj;
                }
            }

            drag *= sectionThicknessSq;
            drag /= 2 * Math.PI;
            drag *= currentSectAreaCrossSection;
            return drag;
        }

        private double CalculateCriticalMachNumber(double finenessRatio)
        {
            if (finenessRatio > 10)
                return 0.925;
            if (finenessRatio < 1.5)
                return 0.285;
            if (finenessRatio > 4)
            {
                if (finenessRatio > 6)
                    return 0.00625 * finenessRatio + 0.8625;

                return 0.025 * finenessRatio + 0.75;
            }
            else if (finenessRatio < 3)
                return 0.33 * finenessRatio - 0.21;

            return 0.07 * finenessRatio + 0.57;
        }

        private double CriticalMachFactorForUnsmoothCrossSection(VoxelCrossSection[] crossSections, double finenessRatio, double sectionThickness)
        {
            double maxAbsRateOfChange = 0;
            double prevArea = 0;
            double invSectionThickness = 1/ sectionThickness;

            for(int i = 0; i < crossSections.Length; i++)
            {
                double currentArea = crossSections[i].area;
                double absRateOfChange = Math.Abs(currentArea - prevArea) * invSectionThickness;
                if (absRateOfChange > maxAbsRateOfChange)
                    maxAbsRateOfChange = absRateOfChange;
                prevArea = currentArea;
            }

            //double normalizedRateOfChange = maxAbsRateOfChange / _maxCrossSectionArea;

            double maxCritMachAdjustmentFactor = 2 * _maxCrossSectionArea + 5 * maxAbsRateOfChange;
            maxCritMachAdjustmentFactor = 0.5 + _maxCrossSectionArea / maxCritMachAdjustmentFactor;     //will vary based on x = maxAbsRateOfChange / _maxCrossSectionArea from 1 @ x = 0 to 0.5 as x -> infinity

            double critAdjustmentFactor = 4 + finenessRatio;
            critAdjustmentFactor = 6 * (1 - maxCritMachAdjustmentFactor) / critAdjustmentFactor;
            critAdjustmentFactor += maxCritMachAdjustmentFactor;

            if (critAdjustmentFactor > 1)
                critAdjustmentFactor = 1;

            return critAdjustmentFactor;
        }
        #endregion

        double MathClampAbs(double value, double abs)
        {
            if (value < -abs)
                return -abs;
            if (value > abs)
                return abs;
            return value;
        }


    }
}