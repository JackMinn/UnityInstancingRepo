using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JacksInstancing
{
    [CustomEditor(typeof(SpawningRule))]
    public class SpawningRuleEditor : Editor
    {
        private SpawningRule m_spawnRule;
        private int m_cachedNumTerrainLayers = 0;
        private string[] m_layerStrings;
        private int[] m_layerInts;


        void OnEnable()
        {
            if (target == null)
            {
                return;
            }

            //Setup target
            m_spawnRule = (SpawningRule)target;
        }

        public override void OnInspectorGUI()
        {
            //Monitor for changes
            EditorGUI.BeginChangeCheck();
            int layer = m_spawnRule.m_layer;
            float instanceDensity = m_spawnRule.m_instanceDensity;
            float gridJitter = m_spawnRule.m_gridJitter;
            //float minScaleX = m_spawnRule.m_minScaleX;
            //float maxScaleX = m_spawnRule.m_maxScaleX;
            //float minScaleY = m_spawnRule.m_minScaleY;
            //float maxScaleY = m_spawnRule.m_maxScaleY;
            //float minScaleZ = m_spawnRule.m_minScaleZ;
            //float maxScaleZ = m_spawnRule.m_maxScaleZ;
            //bool randomXAngle = m_spawnRule.m_randomXAngle;
            //bool randomYAngle = m_spawnRule.m_randomYAngle;
            //bool randomZAngle = m_spawnRule.m_randomZAngle;

            if (m_cachedNumTerrainLayers != Terrain.activeTerrain.terrainData.splatPrototypes.Length)
            {
                m_cachedNumTerrainLayers = Terrain.activeTerrain.terrainData.splatPrototypes.Length;
                m_layerStrings = new string[m_cachedNumTerrainLayers + 1];
                m_layerInts = new int[m_cachedNumTerrainLayers + 1];
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
            layer = EditorGUILayout.IntPopup("Terrain Layer: ", layer, m_layerStrings, m_layerInts);
            EditorGUILayout.Space();

            instanceDensity = EditorGUILayout.FloatField("Instance Density", instanceDensity);
            EditorGUILayout.Space();

            gridJitter = EditorGUILayout.Slider("Grid Jitter", gridJitter, 0, 1);
            EditorGUILayout.Space();

            //minScaleX = EditorGUILayout.FloatField("Min Scale X", minScaleX);
            //EditorGUILayout.Space();
            //maxScaleX = EditorGUILayout.FloatField("Max Scale X", maxScaleX);
            //EditorGUILayout.Space();
            //minScaleY = EditorGUILayout.FloatField("Min Scale Y", minScaleY);
            //EditorGUILayout.Space();
            //maxScaleY = EditorGUILayout.FloatField("Max Scale Y", maxScaleY);
            //EditorGUILayout.Space();
            //minScaleZ = EditorGUILayout.FloatField("Min Scale Z", minScaleZ);
            //EditorGUILayout.Space();
            //maxScaleZ = EditorGUILayout.FloatField("Max Scale Z", maxScaleZ);
            //EditorGUILayout.Space();

            //randomXAngle = EditorGUILayout.Toggle("Random X Rot", randomXAngle);
            //EditorGUILayout.Space();
            //randomYAngle = EditorGUILayout.Toggle("Random Y Rot", randomYAngle);
            //EditorGUILayout.Space();
            //randomZAngle = EditorGUILayout.Toggle("Random Z Rot", randomZAngle);
            //EditorGUILayout.Space();

            if (EditorGUI.EndChangeCheck())
            {
                m_spawnRule.m_layer = layer;
                m_spawnRule.m_instanceDensity = instanceDensity;
                m_spawnRule.m_gridJitter = gridJitter;
                //m_spawnRule.m_minScaleX = minScaleX;
                //m_spawnRule.m_maxScaleX = maxScaleX;
                //m_spawnRule.m_minScaleY = minScaleY;
                //m_spawnRule.m_maxScaleY = maxScaleY;
                //m_spawnRule.m_minScaleZ = minScaleZ;
                //m_spawnRule.m_maxScaleZ = maxScaleZ;
                //m_spawnRule.m_randomXAngle = randomXAngle;
                //m_spawnRule.m_randomYAngle = randomYAngle;
                //m_spawnRule.m_randomZAngle = randomZAngle;
            }
        }
    }
}
