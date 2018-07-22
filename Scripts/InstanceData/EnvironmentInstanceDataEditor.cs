using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JacksInstancing
{
    [CustomEditor(typeof(EnvironmentInstanceData))]
    public class EnvironmentInstanceDataEditor : Editor
    {
        private EnvironmentInstanceData m_instanceData;
        private int m_cachedNumTerrainLayers = 0;
        private string[] m_layerStrings;
        private int[] m_layerInts;

        SerializedProperty spawningRules;


        void OnEnable()
        {
            if (target == null)
            {
                return;
            }

            //Setup target
            m_instanceData = (EnvironmentInstanceData)target;
            spawningRules = serializedObject.FindProperty("m_spawningRules");
        }

        public override void OnInspectorGUI()
        {
            //Monitor for changes
            EditorGUI.BeginChangeCheck();
            int instanceCount = m_instanceData.m_instanceCount;
            bool randomCustomData = m_instanceData.m_UseCustomData;
            int layer = m_instanceData.m_legacyLayer;
            //int instanceDensity = m_instanceData.m_instanceDensity;
            //float gridJitter = m_instanceData.m_gridJitter;
            ScaleMode scaleMode = m_instanceData.m_scaleMode;
            float minScaleX = m_instanceData.m_minScaleX;
            float maxScaleX = m_instanceData.m_maxScaleX;
            float minScaleY = m_instanceData.m_minScaleY;
            float maxScaleY = m_instanceData.m_maxScaleY;
            float minScaleZ = m_instanceData.m_minScaleZ;
            float maxScaleZ = m_instanceData.m_maxScaleZ;
            bool randomXAngle = m_instanceData.m_randomXAngle;
            bool randomYAngle = m_instanceData.m_randomYAngle;
            bool randomZAngle = m_instanceData.m_randomZAngle;

            instanceCount = EditorGUILayout.IntField("Instance Count", instanceCount);
            EditorGUILayout.Space();

            randomCustomData = EditorGUILayout.Toggle("Random Custom Data", randomCustomData);
            EditorGUILayout.Space();

            if (m_cachedNumTerrainLayers != Terrain.activeTerrain.terrainData.splatPrototypes.Length)
            {
                m_cachedNumTerrainLayers = Terrain.activeTerrain.terrainData.splatPrototypes.Length;
                m_layerStrings = new string[m_cachedNumTerrainLayers+1];
                m_layerInts = new int[m_cachedNumTerrainLayers+1];
            }
            for (int i = 0; i < m_cachedNumTerrainLayers; i++)
            {
                m_layerStrings[i] = "Layer " + i;
                m_layerInts[i] = i;
            }
            if (m_layerStrings.Length > m_cachedNumTerrainLayers && m_layerInts.Length > m_cachedNumTerrainLayers)
            {
                m_layerStrings[m_cachedNumTerrainLayers] = "All Layers";
                m_layerInts[m_cachedNumTerrainLayers] = -1;
            }
            layer = EditorGUILayout.IntPopup("Legacy Terrain Layer: ", layer, m_layerStrings, m_layerInts);
            EditorGUILayout.Space();

            //instanceDensity = EditorGUILayout.IntField("Instance Density", instanceDensity);
            //EditorGUILayout.Space();

            //gridJitter = EditorGUILayout.Slider("Grid Jitter", gridJitter, 0, 1);
            //EditorGUILayout.Space();

            EditorGUILayout.PropertyField(spawningRules, new GUIContent("Spawning Rules"), true);
            EditorGUILayout.Space();

            scaleMode = (ScaleMode)EditorGUILayout.EnumPopup("Scale Mode", scaleMode);
            EditorGUILayout.Space();

            minScaleX = EditorGUILayout.FloatField("Min Scale X", minScaleX);
            EditorGUILayout.Space();
            maxScaleX = EditorGUILayout.FloatField("Max Scale X", maxScaleX);
            EditorGUILayout.Space();
            minScaleY = EditorGUILayout.FloatField("Min Scale Y", minScaleY);
            EditorGUILayout.Space();
            maxScaleY = EditorGUILayout.FloatField("Max Scale Y", maxScaleY);
            EditorGUILayout.Space();
            minScaleZ = EditorGUILayout.FloatField("Min Scale Z", minScaleZ);
            EditorGUILayout.Space();
            maxScaleZ = EditorGUILayout.FloatField("Max Scale Z", maxScaleZ);
            EditorGUILayout.Space();

            randomXAngle = EditorGUILayout.Toggle("Random X Rot", randomXAngle);
            EditorGUILayout.Space();
            randomYAngle = EditorGUILayout.Toggle("Random Y Rot", randomYAngle);
            EditorGUILayout.Space();
            randomZAngle = EditorGUILayout.Toggle("Random Z Rot", randomZAngle);
            EditorGUILayout.Space();

            if (GUILayout.Button("Grid Spawning"))
            {
                m_instanceData.GenerateGridInstances();
                return;
            }

            if (GUILayout.Button("Legacy Spawning"))
            {
                m_instanceData.GenerateData();
                return;
            }

            if (EditorGUI.EndChangeCheck())
            {
                m_instanceData.m_instanceCount = instanceCount;
                m_instanceData.m_UseCustomData = randomCustomData;
                m_instanceData.m_legacyLayer = layer;
                //m_instanceData.m_instanceDensity = instanceDensity;
                //m_instanceData.m_gridJitter = gridJitter;
                m_instanceData.m_scaleMode = scaleMode;
                m_instanceData.m_minScaleX = minScaleX;
                m_instanceData.m_maxScaleX = maxScaleX;
                m_instanceData.m_minScaleY = minScaleY;
                m_instanceData.m_maxScaleY = maxScaleY;
                m_instanceData.m_minScaleZ = minScaleZ;
                m_instanceData.m_maxScaleZ = maxScaleZ;
                m_instanceData.m_randomXAngle = randomXAngle;
                m_instanceData.m_randomYAngle = randomYAngle;
                m_instanceData.m_randomZAngle = randomZAngle;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
