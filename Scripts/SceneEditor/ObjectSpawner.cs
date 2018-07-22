#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JacksInstancing
{
    public struct SelectableIcon
    {
        public IndirectInstancedAsset asset;
        public Texture2D icon;
        public bool isSelected;
    }

    //This is currently a WIP and has no actual spawning functionality yet. 
    public class ObjectSpawner : EditorWindow
    {
        private static ObjectSpawner m_thisWindow;
        private static Vector2 m_minWindowSize = new Vector2(300, 500);
        private static float m_buttonDimension = 68f;
        private static List<SelectableIcon> m_activeIcons = new List<SelectableIcon>();

        private static GUIStyle selectedStyle;

        [MenuItem("Window/Jack's Instancing Manager")]
        static void Init()
        {
            // Get existing open window or if none, make a new one:
            ObjectSpawner m_thisWindow = (ObjectSpawner)EditorWindow.GetWindow(typeof(ObjectSpawner));
            m_thisWindow.minSize = m_minWindowSize;
            m_thisWindow.Show();
        }

        void OnGUI()
        {
            if (m_thisWindow == null)
                m_thisWindow = (ObjectSpawner)EditorWindow.GetWindow(typeof(ObjectSpawner));
            if (m_thisWindow != null)
                m_thisWindow.minSize = m_minWindowSize;

            //selectedStyle = EditorStyles.miniButton;
            //selectedStyle.normal.background = Texture2D.blackTexture;

            GUILayout.Label("Jack's Custom Instancing Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            int registeredCount = SceneViewInstancing.m_registeredInstancedAssets.Count;
            for (int i = 0; i < registeredCount; i++)
            {
                IndirectInstancedAsset asset = SceneViewInstancing.m_registeredInstancedAssets[i];
                if (!m_activeIcons.Exists(x => x.asset == asset))
                {
                    SelectableIcon newIcon = new SelectableIcon();
                    newIcon.asset = asset;
                    newIcon.icon = null;
                    newIcon.isSelected = false;
                    m_activeIcons.Add(newIcon);
                }
            }

            //repaint unloaded icons and remove icons of assets that are no longer active in the scene
            for(int i = 0; i < m_activeIcons.Count; i++)
            {
                Mesh instancedObject = m_activeIcons[i].asset.m_LodSettings[0].mesh;
                if (instancedObject != null)
                {
                    bool isLoadingAssetPreview = AssetPreview.IsLoadingAssetPreview(instancedObject.GetInstanceID());
                    SelectableIcon icon = m_activeIcons[i];
                    icon.icon = AssetPreview.GetAssetPreview(instancedObject);
                    if (!icon.icon)
                    {
                        // We have a static preview it just hasn't been loaded yet. Repaint until we have it loaded.
                        if (isLoadingAssetPreview)
                            Repaint();
                        icon.icon = AssetPreview.GetMiniThumbnail(instancedObject);
                    }
                    m_activeIcons[i] = icon; //we cannot modify 1 part of a struct, we need to make a copy, modify it, then set the original to the copy
                }

                IndirectInstancedAsset asset = m_activeIcons[i].asset;
                if (!SceneViewInstancing.m_registeredInstancedAssets.Contains(asset))
                    m_activeIcons.RemoveAt(i);
            }

            int windowWidth = (int)EditorGUIUtility.currentViewWidth;
            int numColumns = Mathf.FloorToInt(windowWidth / m_buttonDimension);
            int numRows = Mathf.CeilToInt((float)registeredCount / numColumns);
            GUILayout.BeginArea(new Rect(10, 40, numColumns * m_buttonDimension, numRows * m_buttonDimension), Texture2D.whiteTexture, EditorStyles.helpBox);
            for (int i = 0; i < m_activeIcons.Count; i++)
            {
                if(i % numColumns == 0)
                    GUILayout.BeginHorizontal();

                SelectableIcon icon = m_activeIcons[i];
                if (icon.isSelected)
                    GUI.backgroundColor = Color.blue;
                else
                    GUI.backgroundColor = Color.white;

                if (GUILayout.Button(icon.icon, GUILayout.Width(64), GUILayout.Height(64)))
                {
                    icon.isSelected = !icon.isSelected;
                    m_activeIcons[i] = icon;
                    Debug.Log(icon.isSelected);
                }
                    
                if (i % numColumns == (numColumns-1))
                    GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        private static void HighlightSelected()
        {
            for(int i = 0; i < m_activeIcons.Count; i++)
            {

            }
        }
    }
}

#endif