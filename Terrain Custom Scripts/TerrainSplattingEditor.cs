#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainSplatting))]
public class TerrainSplattingEditor : Editor
{
    TerrainSplatting t;
    SerializedProperty m_zoom;

    void OnEnable()
    {
        t = (TerrainSplatting)target;
        m_zoom = serializedObject.FindProperty("m_zoom");
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.PropertyField(m_zoom);
        EditorGUILayout.Separator();
        if (GUILayout.Button("Set Splat Map"))
        {
            //Undo.RecordObject(t, "Set Splat Map");
            t.SetSplatMap();
        }
        serializedObject.ApplyModifiedProperties();
    }
}

#endif