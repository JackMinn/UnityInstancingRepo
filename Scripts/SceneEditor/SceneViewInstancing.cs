#if (UNITY_EDITOR) 

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace JacksInstancing
{

    [ExecuteInEditMode]
    public class SceneViewInstancing : MonoBehaviour
    {
        public static SceneViewInstancing m_sceneWindowInstance;
        [HideInInspector] public static List<IndirectInstancedAsset> m_registeredInstancedAssets = new List<IndirectInstancedAsset>();
        private static List<Vector2> m_selectedInstancedAssets = new List<Vector2>();

        private static Dictionary<IndirectInstancedAsset, CommandBuffer> m_registeredCommandBuffers = new Dictionary<IndirectInstancedAsset, CommandBuffer>();
        private static Dictionary<IndirectInstancedAsset, Material[][]> m_registeredMaterialArrays = new Dictionary<IndirectInstancedAsset, Material[][]>();
        private static Dictionary<IndirectInstancedAsset, ComputeBuffer[]> m_registeredEditorComputeBuffers = new Dictionary<IndirectInstancedAsset, ComputeBuffer[]>();

        private static Event m_currentEvent;
        private static Vector2 m_mousePosition;
        private static Vector2 m_lastSelectedInstancedObject;
        private static int m_lastPressedCharacter = 0;

        public Camera m_debugCamera;
        public RenderTexture m_debugRT;
        public Material m_copyDepthMat;

        private static Camera _m_internalEditorCamera;
        private static Camera m_editorCamera
        {
            get
            {
                if (_m_internalEditorCamera == null) { Camera[] sceneViewCameras = SceneView.GetAllSceneCameras();
                    _m_internalEditorCamera = sceneViewCameras.Length > 0 ? sceneViewCameras[0] : null; }
                return _m_internalEditorCamera;
            }
            set { _m_internalEditorCamera = value; }
        }

        private static Texture2D m_pickingColorTex;
        public static RenderTexture m_pickingColorRT;
        private static RenderTexture m_pickingDepthRT;
        private static CommandBuffer m_clearRTCommand;
        private static CommandBuffer m_copyRTCommand;

        private static Vector2 m_trueScreenDimensions;

        public void OnEnable()
        {
            Debug.Log("Scene Instancing Manager enabled.");
            m_sceneWindowInstance = this;

            SceneView.onSceneGUIDelegate += UpdateSceneView;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            Undo.undoRedoPerformed += OnUndo;

            ReloadInstancedDrawing();
        }

        public void OnDisable()
        {
            ClearCommandBuffers();

            SceneView.onSceneGUIDelegate -= UpdateSceneView;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            Undo.undoRedoPerformed -= OnUndo;

            if (m_pickingColorRT != null)
                m_pickingColorRT.Release();

            if (m_pickingDepthRT != null)
                m_pickingDepthRT.Release();
        }


        #region Reload Scene Rendering State
        private void OnSceneSaved(Scene scene)
        {
            Debug.Log("Scene was saved");
            ReloadInstancedDrawing();
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsSaved()
        {
            Debug.Log("All scripts were reloaded");
            ReloadInstancedDrawing();
        }

        //for asset postprocessor
        public static void ReloadThis()
        {
            if (m_sceneWindowInstance.gameObject.activeInHierarchy)
            {
                Debug.Log("Scene Window is reloading itself");
                ReloadInstancedDrawingTimer();
            }
        }

        static private IEnumerator WaitAndSetActive(GameObject go)
        {
            Debug.Log("Will turn on in 2s");
            yield return new WaitForSecondsRealtime(2);
            go.SetActive(true);
        }

        private static void ReloadInstancedDrawing()
        {
            IndirectInstancedAsset[] instancedAssets = FindObjectsOfType<IndirectInstancedAsset>();

            for (int i = 0; i < instancedAssets.Length; i++)
            {
                if (instancedAssets[i].gameObject.activeInHierarchy)
                {
                    instancedAssets[i].gameObject.SetActive(false);
                    instancedAssets[i].gameObject.SetActive(true);
                }
            }
        }

        private static void ReloadInstancedDrawingTimer()
        {
            IndirectInstancedAsset[] instancedAssets = FindObjectsOfType<IndirectInstancedAsset>();

            for (int i = 0; i < instancedAssets.Length; i++)
            {
                if (instancedAssets[i].gameObject.activeInHierarchy)
                {
                    instancedAssets[i].gameObject.SetActive(false);
                    m_sceneWindowInstance.StartCoroutine(WaitAndSetActive(instancedAssets[i].gameObject));
                }
            }
        }
        #endregion

        private void UpdateSceneView(SceneView sceneView)
        {
            CheckGraphicsResourceDimensions();
            ExecuteRegisterEvents();
            if (m_clearRTCommand == null)
                InitCommandBuffers();

            m_debugCamera.transform.SetPositionAndRotation(m_editorCamera.transform.position, m_editorCamera.transform.rotation);
            m_debugCamera.nearClipPlane = m_editorCamera.nearClipPlane;
            m_debugCamera.farClipPlane = m_editorCamera.farClipPlane;
            m_debugCamera.aspect = m_editorCamera.aspect;
            m_debugCamera.fieldOfView = m_editorCamera.fieldOfView;
            m_debugCamera.projectionMatrix = m_editorCamera.projectionMatrix;

            m_currentEvent = Event.current;

            if (m_currentEvent.character != 0)
                m_lastPressedCharacter = m_currentEvent.character;

            m_mousePosition = m_currentEvent.mousePosition;
            m_mousePosition.y = m_trueScreenDimensions.y - m_mousePosition.y;

            HandleGUIEvent();       
        }

        //https://answers.unity.com/questions/463207/how-do-you-make-a-custom-handle-respond-to-the-mou.html
        private static void HandleGUIEvent()
        {
            int controlID = GUIUtility.GetControlID(m_sceneWindowInstance.GetHashCode(), FocusType.Passive);

            if (m_currentEvent.keyCode == KeyCode.Escape)
            {
                DeselectInstancedAssets();
            }

            if (m_currentEvent.button != 1) //if the mouse right button is pressed, we just exit because it only pans the camera rotation around
            {
                if (DetectInstancedAssets())
                {
                    //this needs to be reset each frame, because the unity editor over rides it with its own default control each frame
                    if (m_currentEvent.rawType == EventType.Repaint || m_currentEvent.rawType == EventType.Layout)
                        HandleUtility.AddDefaultControl(controlID);


                    if (Tools.current != Tool.None && Tools.current != Tool.View) //so we have 1 of the 4 object control tools
                    {
                        switch (m_currentEvent.GetTypeForControl(controlID))
                        {
                            case EventType.MouseDown:
                                if (HandleUtility.nearestControl == controlID && m_currentEvent.button == 0) //if the mouse is not in range of any control, use the one we set to default
                                {
                                    GUIUtility.hotControl = controlID;
                                    m_currentEvent.Use();
                                }
                                break;

                            case EventType.MouseUp:
                                if (GUIUtility.hotControl == controlID && m_currentEvent.button == 0)       //if our control is the hot control, then we can process 
                                {
                                    GUIUtility.hotControl = 0;
                                    m_currentEvent.Use();
                                    Tools.handleRotation = Quaternion.identity; //selection has swapped, so we want to reset the rotation handle

                                    if (m_currentEvent.shift)
                                    {
                                        AddSelectedInstancedAssets();
                                    }
                                    else
                                    {
                                        Selection.activeTransform = null;
                                        SetSelectedInstancedAssets();
                                    }
                                }
                                break;
                        }
                    }
                }
                else
                {
                    if (Tools.current != Tool.None && Tools.current != Tool.View) //so we have 1 of the 4 object control tools
                    {
                        switch (m_currentEvent.GetTypeForControl(controlID))
                        {
                            case EventType.Used:                                 //event was used by unity editor, meaning we clicked on one of its objects or handles and it used the event
                                if (!m_currentEvent.shift)
                                    DeselectInstancedAssets();
                                break;
                        }
                    }
                }
            }

            DrawHandle();
        }

        private static Vector3 m_currentScale = Vector3.one;
        private static List<Vector4> m_selectedInitialInstances = new List<Vector4>();
        private static List<Vector4> m_selectedInitialGameObjects = new List<Vector4>();
        private static void DrawHandle()
        {
            Tools.hidden = false;
            if (m_selectedInstancedAssets.Count > 0)
            {
                Tools.hidden = true;

                Vector2 lastSelectedInstance = m_lastSelectedInstancedObject;
                IndirectInstancedAsset asset = m_registeredInstancedAssets[(int)lastSelectedInstance.x];
                Vector3 lastSelectedInstancePosition = asset.m_instanceData.m_positions[(int)lastSelectedInstance.y];
                Vector4 vec4Rotation = asset.m_instanceData.m_rotations[(int)lastSelectedInstance.y];
                Quaternion lastSelectedInstanceRotation = InstancingUtilities.QuaternionFromVector(vec4Rotation);

                if (Tools.pivotMode == PivotMode.Center)
                {
                    Bounds selectedObjectsBounds = new Bounds(lastSelectedInstancePosition, Vector3.zero);
                    for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                    {
                        lastSelectedInstance = m_selectedInstancedAssets[i];
                        asset = m_registeredInstancedAssets[(int)lastSelectedInstance.x];
                        lastSelectedInstancePosition = asset.m_instanceData.m_positions[(int)lastSelectedInstance.y];
                        selectedObjectsBounds.Encapsulate(lastSelectedInstancePosition);
                    }
                    for (int i = 0; i < Selection.transforms.Length; i++)
                    {
                        selectedObjectsBounds.Encapsulate(Selection.transforms[i].position);
                    }
                    lastSelectedInstancePosition = selectedObjectsBounds.center;
                }

                float handleSize = HandleUtility.GetHandleSize(lastSelectedInstancePosition);

                Quaternion previousRotation;
                if (Tools.pivotRotation == PivotRotation.Global)
                    previousRotation = Tools.handleRotation;
                else
                    previousRotation = lastSelectedInstanceRotation;
                
                EditorGUI.BeginChangeCheck();
                Transform[] selectedGameObjects = Selection.transforms;
                switch (Tools.current)
                {
                    case Tool.Move:

                        if (m_currentEvent.type == EventType.MouseDown)
                        {
                            Debug.Log("Rebuilding initial lists.");
                            //build a list of initial sizes of selected instances
                            m_selectedInitialInstances.Clear();
                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Debug.Log("adding");
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                Vector3 position = m_registeredInstancedAssets[(int)currentInstance.x].m_instanceData.m_positions[(int)currentInstance.y];
                                m_selectedInitialInstances.Add(position);
                                Debug.Log("Count is: " + m_selectedInitialInstances.Count);
                            }

                            //build a list of initial sizes of selected game objects
                            m_selectedInitialGameObjects.Clear();
                            for (int i = 0; i < selectedGameObjects.Length; i++)
                            {
                                Vector3 position = selectedGameObjects[i].localPosition;
                                m_selectedInitialGameObjects.Add(position);
                            }
                        }

                        Vector3 newPosition;
                        if (Tools.pivotRotation == PivotRotation.Global)
                            newPosition = Handles.PositionHandle(lastSelectedInstancePosition, Quaternion.identity);
                        else
                            newPosition = Handles.PositionHandle(lastSelectedInstancePosition, lastSelectedInstanceRotation);

                        float xAxisMovement = newPosition.x - lastSelectedInstancePosition.x;
                        float yAxisMovement = newPosition.y - lastSelectedInstancePosition.y;
                        float zAxisMovement = newPosition.z - lastSelectedInstancePosition.z;

                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoStateInstance.m_instance.m_lastUsedTool = Tool.Move;
                            //set undo state here, else last used tool will change on mouse down and cause incorrect undo history based on tool swapping
                            UndoStateInstance.m_instance.SetUndoState(m_selectedInitialInstances, m_selectedInstancedAssets); 

                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                IndirectInstancedAsset currentAsset = m_registeredInstancedAssets[(int)currentInstance.x];

                                Undo.RegisterCompleteObjectUndo(UndoStateInstance.m_instance, "Undo instanced move");
                                UndoStateInstance.m_instance.IncrementModifiedCounter();

                                if (Mathf.Abs(xAxisMovement) > 0.01f)
                                    currentAsset.m_instanceData.m_positions[(int)currentInstance.y].x += xAxisMovement;
                                if (Mathf.Abs(yAxisMovement) > 0.01f)
                                    currentAsset.m_instanceData.m_positions[(int)currentInstance.y].y += yAxisMovement;
                                if (Mathf.Abs(zAxisMovement) > 0.01f)
                                    currentAsset.m_instanceData.m_positions[(int)currentInstance.y].z += zAxisMovement;

                                if (Mathf.Abs(xAxisMovement) > 0.01f || Mathf.Abs(yAxisMovement) > 0.01f || Mathf.Abs(zAxisMovement) > 0.01f)
                                    currentAsset.AddDirtyPosition((int)currentInstance.y);
                            }

                            Undo.RecordObjects(selectedGameObjects, "Undo game object move");
                            for (int i = 0; i < selectedGameObjects.Length; i++)
                            {
                                Vector3 displacedPosition = selectedGameObjects[i].position;
                                if (Mathf.Abs(xAxisMovement) > 0.01f)
                                    displacedPosition.x += xAxisMovement;
                                if (Mathf.Abs(yAxisMovement) > 0.01f)
                                    displacedPosition.y += yAxisMovement;
                                if (Mathf.Abs(zAxisMovement) > 0.01f)
                                    displacedPosition.z += zAxisMovement;

                                selectedGameObjects[i].position = displacedPosition;
                            }
                        }
                        break;
                    case Tool.Rotate:

                        //allows rotation back to (0,0,0) with shift + n
                        if (m_currentEvent.keyCode == KeyCode.N && m_currentEvent.shift)
                        {
                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                IndirectInstancedAsset currentAsset = m_registeredInstancedAssets[(int)currentInstance.x];
                                currentAsset.m_instanceData.m_rotations[(int)currentInstance.y] = new Vector4(0, 0, 0, 1); //The identity quaternion
                                currentAsset.AddDirtyRotation((int)currentInstance.y);                          
                            }
                            //also reset the gizmo, this isnt really needed as the next frame will already be correct and 1 frame is not noticeable, but added for consistency
                            Tools.handleRotation = Quaternion.identity;
                            previousRotation = Quaternion.identity;
                        }

                        if (m_currentEvent.type == EventType.MouseDown)
                        {
                            Debug.Log("Rebuilding initial lists.");
                            //build a list of initial sizes of selected instances
                            m_selectedInitialInstances.Clear();
                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                Vector4 rotation = m_registeredInstancedAssets[(int)currentInstance.x].m_instanceData.m_rotations[(int)currentInstance.y];
                                m_selectedInitialInstances.Add(rotation);
                            }

                            //build a list of initial sizes of selected game objects
                            m_selectedInitialGameObjects.Clear();
                            for (int i = 0; i < selectedGameObjects.Length; i++)
                            {
                                Vector4 rotation = InstancingUtilities.VectorFromQuaternion(selectedGameObjects[i].rotation);
                                m_selectedInitialGameObjects.Add(rotation);
                            }
                        }

                        Quaternion newRotation = Handles.RotationHandle(previousRotation, lastSelectedInstancePosition); //The new rotation value modified by the user's interaction with the handle. 
                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoStateInstance.m_instance.m_lastUsedTool = Tool.Rotate;
                            UndoStateInstance.m_instance.SetUndoState(m_selectedInitialInstances, m_selectedInstancedAssets);

                            Quaternion delta = Quaternion.Inverse(previousRotation) * newRotation;
                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                IndirectInstancedAsset currentAsset = m_registeredInstancedAssets[(int)currentInstance.x];
                                Quaternion instancedObjectRotation = InstancingUtilities.QuaternionFromVector(currentAsset.m_instanceData.m_rotations[(int)currentInstance.y]);

                                Undo.RegisterCompleteObjectUndo(UndoStateInstance.m_instance, "Undo instanced rotation");
                                UndoStateInstance.m_instance.IncrementModifiedCounter();

                                //lhs happens first, so if we want a local space rotation, we do a delta rotation in the objects frame of reference by object quaternion on lhs
                                //if we want a world space rotation, then lhs is the delta, and the objects rotation happens after, in the world space frame of reference
                                if (Tools.pivotRotation == PivotRotation.Local)
                                    instancedObjectRotation = instancedObjectRotation * delta;
                                else
                                    instancedObjectRotation = delta * instancedObjectRotation;

                                currentAsset.m_instanceData.m_rotations[(int)currentInstance.y] = InstancingUtilities.VectorFromQuaternion(instancedObjectRotation);
                                currentAsset.AddDirtyRotation((int)currentInstance.y);
                            }

                            Undo.RecordObjects(selectedGameObjects, "Undo game object rotation");
                            for (int i = 0; i < selectedGameObjects.Length; i++)
                            {
                                Quaternion gameObjectRotation = selectedGameObjects[i].rotation;
                                if (Tools.pivotRotation == PivotRotation.Local)
                                    gameObjectRotation = gameObjectRotation * delta;
                                else
                                    gameObjectRotation = delta * gameObjectRotation;
                                selectedGameObjects[i].rotation = gameObjectRotation;
                            }

                            //https://math.stackexchange.com/questions/40164/how-do-you-rotate-a-vector-by-a-unit-quaternion
                            Tools.handleRotation = newRotation;
                        }
                        break;
                    case Tool.Scale:

                        //allows scaling back to (1,1,1) with shift + n
                        if(m_currentEvent.keyCode == KeyCode.N && m_currentEvent.shift)
                        {
                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                IndirectInstancedAsset currentAsset = m_registeredInstancedAssets[(int)currentInstance.x];
                                currentAsset.m_instanceData.m_positions[(int)currentInstance.y].w = InstancingUtilities.PackScaleVectorToFloat(Vector3.one);
                                currentAsset.AddDirtyScale((int)currentInstance.y);
                            }
                        }

                        if (m_currentEvent.type == EventType.MouseDown)
                        {
                            m_currentScale = Vector3.one;

                            Debug.Log("Rebuilding initial lists.");
                            //build a list of initial sizes of selected instances
                            m_selectedInitialInstances.Clear();
                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                float scale = m_registeredInstancedAssets[(int)currentInstance.x].m_instanceData.m_positions[(int)currentInstance.y].w;
                                m_selectedInitialInstances.Add(new Vector4(scale, 0, 0, 0));
                            }

                            //build a list of initial sizes of selected game objects
                            m_selectedInitialGameObjects.Clear();
                            for(int i = 0; i < selectedGameObjects.Length; i++)
                            {
                                Vector3 scale = selectedGameObjects[i].localScale;
                                m_selectedInitialGameObjects.Add(scale);
                            }
                        }

                        Vector3 newScale = Handles.ScaleHandle(m_currentScale, lastSelectedInstancePosition, lastSelectedInstanceRotation, handleSize);
                        if (EditorGUI.EndChangeCheck())
                        {
                            UndoStateInstance.m_instance.m_lastUsedTool = Tool.Scale;
                            UndoStateInstance.m_instance.SetUndoState(m_selectedInitialInstances, m_selectedInstancedAssets);

                            m_currentScale = newScale;
                            for (int i = 0; i < m_selectedInstancedAssets.Count; i++)
                            {
                                Vector2 currentInstance = m_selectedInstancedAssets[i];
                                IndirectInstancedAsset currentAsset = m_registeredInstancedAssets[(int)currentInstance.x];

                                Undo.RegisterCompleteObjectUndo(UndoStateInstance.m_instance, "Undo instanced scaling");
                                UndoStateInstance.m_instance.IncrementModifiedCounter();

                                currentAsset.m_instanceData.ModifyScale((int)currentInstance.y, m_currentScale, m_selectedInitialInstances[i].x);

                                currentAsset.AddDirtyScale((int)currentInstance.y);
                            }

                            Undo.RecordObjects(selectedGameObjects, "Undo game object scaling");
                            for (int i = 0; i < selectedGameObjects.Length; i++)
                            {
                                Vector3 objectScale = InstancingUtilities.MultiplyVector3(m_selectedInitialGameObjects[i], m_currentScale);
                                selectedGameObjects[i].localScale = objectScale;
                            }
                        }

                        break;
                }
            }
        }

        //this is a very heavy way of undoing, as it will reload ALL buffers for ALL assets on EVERY redo, but I cant find a better approach for now
        private static void OnUndo()
        {
            switch (UndoStateInstance.m_instance.m_lastUsedTool)
            {
                case Tool.Move:
                    for (int i = 0; i < UndoStateInstance.m_instance.m_instanceInfo.Count; i++)
                    {
                        Vector2 currentAsset = UndoStateInstance.m_instance.m_instanceInfo[i];
                        Vector3 data = UndoStateInstance.m_instance.m_savedData[i];
                        m_registeredInstancedAssets[(int)currentAsset.x].m_instanceData.m_positions[(int)currentAsset.y].Set(
                            data.x, data.y, data.z, m_registeredInstancedAssets[(int)currentAsset.x].m_instanceData.m_positions[(int)currentAsset.y].w);
                    }
                    break;

                case Tool.Rotate:
                    for (int i = 0; i < UndoStateInstance.m_instance.m_instanceInfo.Count; i++)
                    {
                        Vector2 currentAsset = UndoStateInstance.m_instance.m_instanceInfo[i];
                        Vector4 data = UndoStateInstance.m_instance.m_savedData[i];
                        m_registeredInstancedAssets[(int)currentAsset.x].m_instanceData.m_rotations[(int)currentAsset.y] = data;
                    }
                    break;

                case Tool.Scale:
                    for (int i = 0; i < UndoStateInstance.m_instance.m_instanceInfo.Count; i++)
                    {
                        Vector2 currentAsset = UndoStateInstance.m_instance.m_instanceInfo[i];
                        Vector4 data = UndoStateInstance.m_instance.m_savedData[i];
                        m_registeredInstancedAssets[(int)currentAsset.x].m_instanceData.m_positions[(int)currentAsset.y].w = data.x;
                    }
                    break;
            }
        
            for (int i = 0; i < m_registeredInstancedAssets.Count; i++)
            {
                m_registeredInstancedAssets[i].ReloadAllComputerBuffers();
            }
        }

        private static bool DetectInstancedAssets()
        {
            if (m_pickingColorRT != null && m_pickingColorTex != null && IsInSceneBounds(m_mousePosition, m_trueScreenDimensions))
            {
                RenderTexture currentRT = RenderTexture.active;
                //Graphics.Blit(m_pickingColorRT, m_debugRT);
                RenderTexture.active = m_pickingColorRT;
                m_pickingColorTex.ReadPixels(new Rect(m_mousePosition.x, m_trueScreenDimensions.y - m_mousePosition.y, 1, 1), 0, 0, false);
                RenderTexture.active = currentRT;

                Vector2 decoded = Vector2.zero;
                bool instancedAssetDetected = DecodeInstanceFromColor(m_pickingColorTex.GetPixel(0, 0), ref decoded);
                if (instancedAssetDetected)
                {
                    int objectID = (int)decoded.x;
                    int instanceID = (int)decoded.y;
                    //Debug.Log("Object ID is: " + objectID + " and Instance ID is: " + instanceID);
                    IndirectInstancedAsset selectedAsset = m_registeredInstancedAssets[objectID];
                    Vector3 position = selectedAsset.m_instanceData.m_positions[instanceID];
                    //Debug.Log("Position of selected asset is: " + position.ToString("f3"));
                }
                return instancedAssetDetected;
            }
            return false;
        }

        private static void DeselectInstancedAssets()
        {
            m_selectedInstancedAssets.Clear();
        }

        private static void SetSelectedInstancedAssets()
        {
            Vector2 decoded = Vector2.zero;
            bool instancedAssetDetected = DecodeInstanceFromColor(m_pickingColorTex.GetPixel(0, 0), ref decoded);
            if (instancedAssetDetected)
            {
                m_selectedInstancedAssets.Clear();
                m_selectedInstancedAssets.Add(decoded);
                m_lastSelectedInstancedObject = decoded;
            }
        }

        //this needs to add on either through shift click, but also by iterating over a rect for rect drag select, unless we make a rect specific function as well??
        private static void AddSelectedInstancedAssets()
        {
            Vector2 decoded = Vector2.zero;
            bool instancedAssetDetected = DecodeInstanceFromColor(m_pickingColorTex.GetPixel(0, 0), ref decoded);
            if (instancedAssetDetected)
            {            
                if (!m_selectedInstancedAssets.Contains(decoded))
                {
                    m_selectedInstancedAssets.Add(decoded);
                } else
                {
                    m_selectedInstancedAssets.Remove(decoded);  //shift clicking an already selected object will unselect
                }
                m_lastSelectedInstancedObject = decoded; 
            }
        }

        //validity checking functions
        private static void CheckGraphicsResourceDimensions()
        {
            if(m_editorCamera.activeTexture != null)
                m_trueScreenDimensions = new Vector2(m_editorCamera.activeTexture.width, m_editorCamera.activeTexture.height);

            if (m_pickingColorRT == null)
            {
                Debug.Log("Render Target was null, creating a new Render Target.");
                m_pickingColorRT = new RenderTexture((int)m_trueScreenDimensions.x, (int)m_trueScreenDimensions.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                m_pickingColorRT.Create();
                m_pickingDepthRT = new RenderTexture((int)m_trueScreenDimensions.x, (int)m_trueScreenDimensions.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                m_pickingDepthRT.Create();
                m_sceneWindowInstance.m_debugCamera.targetTexture = m_pickingDepthRT;
                ClearCommandBuffers();
                InitCommandBuffers();
                RemakeAllAssetCommandBuffers();
            }
            else if (m_pickingColorRT.width != (int)m_trueScreenDimensions.x || m_pickingColorRT.height != (int)m_trueScreenDimensions.y)
            {
                Debug.Log("Render Target dimensions do not match scene camera dimensions. Creating a new Render Target.");
                m_pickingColorRT.Release();
                m_pickingColorRT = new RenderTexture((int)m_trueScreenDimensions.x, (int)m_trueScreenDimensions.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                m_pickingColorRT.Create();
                m_pickingDepthRT.Release();
                m_pickingDepthRT = new RenderTexture((int)m_trueScreenDimensions.x, (int)m_trueScreenDimensions.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                m_pickingDepthRT.Create();
                m_sceneWindowInstance.m_debugCamera.targetTexture = m_pickingDepthRT;
                ClearCommandBuffers();
                InitCommandBuffers();
                RemakeAllAssetCommandBuffers();
            }

            if (m_pickingColorTex == null)
            {
                Debug.Log("Picking Texture was null, creating a new Picking Texture.");
                m_pickingColorTex = new Texture2D(m_pickingColorRT.width, m_pickingColorRT.height, TextureFormat.ARGB32, false);
            }
            else if (m_pickingColorTex.width != m_pickingColorRT.width || m_pickingColorTex.height != m_pickingColorRT.height)
            {
                Debug.Log("Picking Texture dimensions do not match render target dimensions. Creating a new Picking Texture.");
                m_pickingColorTex = new Texture2D(m_pickingColorRT.width, m_pickingColorRT.height, TextureFormat.ARGB32, false);
            }
        }

        #region Command Buffer Control
        private static void InitCommandBuffers()
        {
            if (m_editorCamera != null && m_clearRTCommand == null && m_pickingColorRT != null && m_pickingColorTex != null)
            {
                m_clearRTCommand = new CommandBuffer();
                m_clearRTCommand.name = "Clear Render Target";

                //clear the picking RT's color and depth buffer, then blit the depth buffer from the debug camera into the picking RT
                m_clearRTCommand.SetRenderTarget(m_pickingColorRT);
                m_clearRTCommand.ClearRenderTarget(true, true, Color.black);
                m_clearRTCommand.Blit(m_sceneWindowInstance.m_debugCamera.activeTexture, m_pickingColorRT, m_sceneWindowInstance.m_copyDepthMat);
                m_sceneWindowInstance.m_debugCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, m_clearRTCommand);

                //this only blits into the debug render texture and honestly isnt necessary, but can leave it here for now
                m_copyRTCommand = new CommandBuffer();
                m_copyRTCommand.name = "Copy Render Target";
                m_copyRTCommand.Blit(m_pickingColorRT, m_sceneWindowInstance.m_debugRT);
                m_sceneWindowInstance.m_debugCamera.AddCommandBuffer(CameraEvent.AfterImageEffects, m_copyRTCommand);
            }
        }

        private static void ClearCommandBuffers()
        {
            if (m_clearRTCommand != null && m_sceneWindowInstance.m_debugCamera != null)
            {
                m_sceneWindowInstance.m_debugCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, m_clearRTCommand);
                m_clearRTCommand.Clear();
            }
            if (m_copyRTCommand != null && m_sceneWindowInstance.m_debugCamera != null)
            {
                m_sceneWindowInstance.m_debugCamera.RemoveCommandBuffer(CameraEvent.AfterImageEffects, m_copyRTCommand);
                m_copyRTCommand.Clear();
            }

            m_clearRTCommand = null;
            m_copyRTCommand = null;
        }

        private static void RemakeAllAssetCommandBuffers()
        {
            IndirectInstancedAsset asset;
            CommandBuffer toRemake;
            Material[][] colorPickingMatArray;
            for (int k = 0; k < m_registeredInstancedAssets.Count; k++)
            {
                asset = m_registeredInstancedAssets[k];
                int numLods = asset.m_LodSettings.Length;
                toRemake = m_registeredCommandBuffers[asset];
                toRemake.Clear();
                colorPickingMatArray = m_registeredMaterialArrays[asset];

                toRemake.SetRenderTarget(m_pickingColorRT);
                for (int i = 0; i < numLods; i++)
                {
                    for (int j = 0; j < asset.m_LodSettings[i].mesh.subMeshCount; j++)
                    {
                        if (colorPickingMatArray[i][j] != null)
                            toRemake.DrawMeshInstancedIndirect(asset.m_LodSettings[i].mesh, j, colorPickingMatArray[i][j], 0, asset.m_argsBuffersArray[i][j], 0);
                    }
                }
            }
        }
        #endregion

        #region Register and Unregister Indirect Instanced Assets
        public static void RegisterIndirectInstancedAsset(IndirectInstancedAsset assetToRegister)
        {
            if(!m_registeredInstancedAssets.Contains(assetToRegister))
            {
                m_assetsToRegister.Add(assetToRegister);
            }
        }

        public static void UnregisterIndirectInstancedAsset(IndirectInstancedAsset assetToUnregister)
        {
            m_assetsToRegister.Remove(assetToUnregister);
            if (m_registeredInstancedAssets.Remove(assetToUnregister))
            {
                OnUnregisterIndirectInstancedAsset(assetToUnregister);
            }
        }

        private static List<IndirectInstancedAsset> m_assetsToRegister = new List<IndirectInstancedAsset>();

        private static void ExecuteRegisterEvents()
        {
            for(int i = 0; i < m_assetsToRegister.Count; i++)
            {
                if (!m_registeredInstancedAssets.Contains(m_assetsToRegister[i]))
                {
                    OnRegisterIndirectInstancedAsset(m_assetsToRegister[i]);
                }
            }
            m_assetsToRegister.Clear();
        }

        private static void OnRegisterIndirectInstancedAsset(IndirectInstancedAsset assetToRegister)
        {
            m_registeredInstancedAssets.Add(assetToRegister);
            Debug.Log("Registered an indirect instanced asset");

            int objectID = m_registeredInstancedAssets.IndexOf(assetToRegister);
            int numLods = assetToRegister.m_LodSettings.Length;

            //initialize materials and gpu buffers, and bind the gpu buffers to their respective materials
            Material[][] colorPickingMatArray = new Material[numLods][];
            ComputeBuffer[] editorAppendBuffer = new ComputeBuffer[numLods];
            for (int i = 0; i < numLods; i++)
            {
                editorAppendBuffer[i] = new ComputeBuffer(assetToRegister.m_instanceCount, sizeof(float), ComputeBufferType.Counter);
                assetToRegister.SetComputeBuffer(editorAppendBuffer[i], "lod" + i + "IDBuffer");

                colorPickingMatArray[i] = new Material[assetToRegister.m_LodSettings[i].materialReferences.Length];
                for (int j = 0; j < colorPickingMatArray[i].Length; j++)
                {
                    colorPickingMatArray[i][j] = new Material(Shader.Find("JacksInstancing/ColorPickerShader"));
                    colorPickingMatArray[i][j].SetBuffer("batchDataBuffer", assetToRegister.m_appendBuffers[i]);
                    colorPickingMatArray[i][j].SetBuffer("instanceIDBuffer", editorAppendBuffer[i]);

                    int cullMode = (int)assetToRegister.m_LodSettings[i].materialReferences[j].editorFaceCulling;
                    colorPickingMatArray[i][j].SetInt("_CullMode", cullMode);

                    colorPickingMatArray[i][j].SetFloat("_ObjectID", objectID);

                    Debug.Log("Pass count is: " + assetToRegister.m_LodSettings[i].materialReferences[j].material.passCount);
                    for(int k = 0; k < assetToRegister.m_LodSettings[i].materialReferences[j].material.passCount; k++)
                    {
                        Debug.Log(assetToRegister.m_LodSettings[i].materialReferences[j].material.GetPassName(k));
                    }
                }
            }
            m_registeredMaterialArrays.Add(assetToRegister, colorPickingMatArray);
            m_registeredEditorComputeBuffers.Add(assetToRegister, editorAppendBuffer);

            //initialize the command buffer to draw this instanced asset
            CommandBuffer pickingColorCommand = new CommandBuffer();
            pickingColorCommand.name = assetToRegister.name +  " Color Picking Draw";

            pickingColorCommand.SetRenderTarget(m_pickingColorRT);
            for (int i = 0; i < numLods; i++)
            {
                for (int j = 0; j < assetToRegister.m_LodSettings[i].mesh.subMeshCount; j++)
                {
                    if(colorPickingMatArray[i][j] != null)
                        pickingColorCommand.DrawMeshInstancedIndirect(assetToRegister.m_LodSettings[i].mesh, j, colorPickingMatArray[i][j], 0, assetToRegister.m_argsBuffersArray[i][j], 0);
                }
            }

            m_sceneWindowInstance.m_debugCamera.AddCommandBuffer(CameraEvent.BeforeImageEffects, pickingColorCommand);
            m_registeredCommandBuffers.Add(assetToRegister, pickingColorCommand);
        }

        private static void OnUnregisterIndirectInstancedAsset(IndirectInstancedAsset assetToUnregister)
        {
            Debug.Log("Unregistered an indirect instanced asset");

            //successfully unregistering an asset implies the indexes of assets in the registered list might change, which will change objectIDs
            //the shaders global objectID parameter must be updated to reflect this change
            DeselectInstancedAssets();

            IndirectInstancedAsset asset;
            Material[][] toUpdate;
            for (int objectID = 0; objectID < m_registeredInstancedAssets.Count; objectID++)
            {
                asset = m_registeredInstancedAssets[objectID];
                toUpdate = m_registeredMaterialArrays[asset];
                for(int i = 0; i < toUpdate.Length; i++)
                {
                    for(int j = 0; j < toUpdate[i].Length; j++)
                    {
                        toUpdate[i][j].SetFloat("_ObjectID", objectID);
                    }
                }
            }

            if (m_registeredCommandBuffers != null && m_registeredCommandBuffers.ContainsKey(assetToUnregister))
            {
                CommandBuffer toClear = m_registeredCommandBuffers[assetToUnregister];
                if (toClear != null && m_sceneWindowInstance.m_debugCamera != null)
                {
                    m_sceneWindowInstance.m_debugCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, toClear);
                    toClear.Clear();
                }
                m_registeredCommandBuffers.Remove(assetToUnregister);
            }

            if(m_registeredMaterialArrays != null && m_registeredMaterialArrays.ContainsKey(assetToUnregister))
            {
                Material[][] toClear = m_registeredMaterialArrays[assetToUnregister];
                if(toClear != null)
                {
                    ClearObjectArray<Material>(toClear, x => { DestroyImmediate(x, false); Debug.Log("Destroying Material"); });
                }
                m_registeredMaterialArrays.Remove(assetToUnregister);
            }

            if (m_registeredEditorComputeBuffers != null && m_registeredEditorComputeBuffers.ContainsKey(assetToUnregister))
            {
                ComputeBuffer[] toClear = m_registeredEditorComputeBuffers[assetToUnregister];
                if (toClear != null)
                {
                    ClearObjectArray<ComputeBuffer>(toClear, x => { if (x != null) { x.Release(); x = null; } });
                }
                m_registeredEditorComputeBuffers.Remove(assetToUnregister);
            }
        }
        #endregion

        #region Helper Functions
        public delegate void Delete<T>(T obj);
        public static void ClearObjectArray<T>(T[] arrayToClear, Delete<T> d)
        {
            for (int i = 0; i < arrayToClear.Length; i++)
            {
                d(arrayToClear[i]);
            }
        }
        public static void ClearObjectArray<T>(T[][] arrayToClear, Delete<T> d)
        {
            for(int i = 0; i < arrayToClear.Length; i++)
            {
                for(int j = 0; j < arrayToClear[i].Length; j++)
                {
                    d(arrayToClear[i][j]);
                }
            }
        }

        private static bool DecodeInstanceFromColor(Color encoded, ref Vector2 decoded)
        {
            encoded *= 255f;

            float instanceID = encoded.r;
            instanceID += encoded.g * 255;
            instanceID += encoded.b * 255 * 255;

            decoded.x = encoded.a;
            decoded.y = instanceID;

            //we support 255 instanced objects from 0 to 254, the value 255 means no instanced object was detected
            return decoded.x == 255 ? false : true;
        }

        private static bool IsInSceneBounds(Vector2 mousePosition, Vector2 screenDimensions)
        {
            float x = screenDimensions.x;
            float y = screenDimensions.y;
            if (mousePosition.x >= 0 && mousePosition.x <= x && mousePosition.y >= 0 && mousePosition.y <= y)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion
    }
}

#endif
