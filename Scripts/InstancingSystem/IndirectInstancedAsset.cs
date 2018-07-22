using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace JacksInstancing
{
    [System.Serializable]
    public struct MaterialAndCullSetting
    {
        [SerializeField] public Material material;
        [SerializeField] public CullMode editorFaceCulling;
    }

    [System.Serializable]
    public struct LODArray
    {
        [SerializeField] public Mesh mesh;
        //[SerializeField] public Material[] materialReferences;
        [SerializeField] public MaterialAndCullSetting[] materialReferences;
        [SerializeField] [Range(0, 1)] public float screenSize;
        [SerializeField] public ShadowCastingMode shadowSetting;
        [SerializeField] public bool objectMotionVectors; //changing this during run time does nothing
    }

    [ExecuteInEditMode]
    public class IndirectInstancedAsset : MonoBehaviour
    {
        [Header("Global Settings")]
        public EnvironmentInstanceData m_instanceData;
        public ComputeShader m_computeShaderReference;
        public int m_instanceCount = 200000;
        public Light m_directionalLight;
        public ShadowCullingManager m_shadowCullingManager;
        public bool m_castShadowIfCulled; //changing this at run time does nothing
        [Range(0, 1)] public float m_coarseCullFarDistance = 1;
        public bool m_debugAppendCount;

        [Header("LOD Settings - Max 4 Levels Supported")]
        public LODArray[] m_LodSettings;
        private int m_cachedNumLods;

        //global variables that rarely get updated
        private ComputeShader m_computeShader;
        [HideInInspector] public Camera m_mainCamera;
        private int m_cachedComputeKernel;
        private int m_cachedShadowComputeKernel;
        private CommandBuffer m_drawShadowCasterCommand = null;
        private CommandBuffer m_shadowCasterCullingCommand = null;
        private CommandBuffer m_drawMotionVectorsCommand = null;
        readonly RenderTargetIdentifier m_motionVectorsRT = new RenderTargetIdentifier(BuiltinRenderTextureType.MotionVectors);
        private Material m_cameraMotionVectorsIndirectMat;
        private Bounds m_drawCallBounds;

        //frame data to enable frustum culling/shadow caster culling
        private Plane[] m_frustumPlanes = new Plane[6];
        private Vector3[] m_frustumCorners = new Vector3[8];
        private Plane[] m_shadowRegionPlanes = new Plane[14];
        private float[] m_frustumPlanesUnrolled = new float[24];
        private float[] m_shadowRegionPlanesUnrolled = new float[56];
        private float[] m_cameraProjectionMatrixUnrolled = new float[16];

        //cached values to notice setting changes
        private ShadowCastingMode[] m_cachedLodShadowSettings;

        //compute buffer for getting shadow region
        private ComputeBuffer m_shadowRegionBuffer;

        //compute buffers and arguments for indirect drawing 
        private ComputeBuffer m_positionBuffer;
        private ComputeBuffer m_rotationBuffer;
        private ComputeBuffer m_customDataBuffer;

        private ComputeBuffer[] _m_appendBuffers;
        public ComputeBuffer[] m_appendBuffers
        {
            get
            {
                return _m_appendBuffers;
            }
            private set { _m_appendBuffers = value; }
        }

        private ComputeBuffer[][] _m_argsBuffersArray;
        public ComputeBuffer[][] m_argsBuffersArray
        {
            get
            {
                return _m_argsBuffersArray;
            }
            private set { _m_argsBuffersArray = value; }
        }

        private uint[][][] m_argsArray;
        private Material[][] m_materials;

        //data describing attributes of all objects managed by this manager
        private Vector4[] m_positions;
        private Vector4[] m_rotations;
        private Vector4[] m_customData;

        private Matrix4x4 m_unjitteredVPMatrix;
        private Matrix4x4 m_previousVPMatrix;

#if UNITY_EDITOR
        //dirty lists for updating the above attributes in the editor
        private List<int> m_dirtyPositionInstances = new List<int>();
        private List<int> m_dirtyRotationInstances = new List<int>();
        private List<int> m_dirtyScaleInstances = new List<int>();
#endif

        private void Start()
        {

        }

        IEnumerator SetPreviousVPMatrix()
        {
            yield return new WaitForEndOfFrame();
            m_previousVPMatrix = GL.GetGPUProjectionMatrix(m_mainCamera.nonJitteredProjectionMatrix, true) * m_mainCamera.worldToCameraMatrix;
        }

        //this should probably be a late update
        private void LateUpdate()
        {
#if (UNITY_EDITOR)
            if (!Application.isPlaying)
                m_mainCamera = UnityEditor.SceneView.GetAllSceneCameras()[0];
#endif

            m_unjitteredVPMatrix = GL.GetGPUProjectionMatrix(m_mainCamera.nonJitteredProjectionMatrix, true) * m_mainCamera.worldToCameraMatrix;
            if (m_previousVPMatrix == null)
                m_previousVPMatrix = m_unjitteredVPMatrix;
            else
                StartCoroutine(SetPreviousVPMatrix());

            //I tried to do this in the motion vector command buffer, but there exists some concurrency issue and the previous matrix is recached before pushing to GPU
            Shader.SetGlobalMatrix("_CustomNonJitteredVP", m_unjitteredVPMatrix);
            Shader.SetGlobalMatrix("_CustomPreviousVP", m_previousVPMatrix);

            bool updateCount = m_instanceData.m_dataDirty;
            bool updateShadows = false;
            bool updateMotionVectors = (m_mainCamera.depthTextureMode & DepthTextureMode.MotionVectors) == DepthTextureMode.MotionVectors;
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                updateShadows = updateShadows | (m_cachedLodShadowSettings[i] != m_LodSettings[i].shadowSetting);
            }
            if (updateCount || updateShadows)
            {
                Debug.Log("Either instance count or shadows setting, updating.");
                Initialize(updateCount, updateShadows);
            }
            if (updateMotionVectors == true && m_drawMotionVectorsCommand == null)
            {
                Debug.Log("Render motion vectors.");
                InitMotionVectorsCommandBuffer();
            }
            UpdateComputeShader();

            for (int i = 0; i < m_cachedNumLods; i++)
            {
                for (int j = 0; j < m_LodSettings[i].mesh.subMeshCount; j++)
                {
                    Graphics.DrawMeshInstancedIndirect(m_LodSettings[i].mesh, j, m_materials[i][j], m_drawCallBounds, m_argsBuffersArray[i][j],
                        0, null, ShadowCastingMode.Off);
                }
            }
        }

        private void InitComputeShader()
        {
            m_instanceCount = m_instanceData.m_instanceCount;
            m_shadowRegionBuffer = m_shadowCullingManager._m_shadowRegionPlanes;
            m_computeShader.SetBuffer(m_cachedShadowComputeKernel, "gpuShadowRegionPlanes", m_shadowRegionBuffer);

            for (int i = 0; i < m_cachedNumLods; i++)
            {
                //create the append buffer for a given lod and bind it to the two compute kernels and all materials of that lod
                m_appendBuffers[i] = new ComputeBuffer(m_instanceCount, 36 * sizeof(float), ComputeBufferType.Counter);
                m_computeShader.SetBuffer(m_cachedComputeKernel, "lod" + i + "Buffer", m_appendBuffers[i]);
                m_computeShader.SetBuffer(m_cachedShadowComputeKernel, "lod" + i + "Buffer", m_appendBuffers[i]); //we can cache the strings for performance
                for (int j = 0; j < m_materials[i].Length; j++)
                {
                    m_materials[i][j].SetBuffer("batchDataBuffer", m_appendBuffers[i]);
                }
            }

            m_positionBuffer = new ComputeBuffer(m_instanceCount, 4 * sizeof(float));
            m_rotationBuffer = new ComputeBuffer(m_instanceCount, 4 * sizeof(float));
            if (m_instanceData.m_UseCustomData)
                m_customDataBuffer = new ComputeBuffer(m_instanceCount, 4 * sizeof(float));
            else
                m_customDataBuffer = null;

            for (int i = 0; i < m_cachedNumLods; i++)
            {
                for (int j = 0; j < m_LodSettings[i].mesh.subMeshCount; j++)
                {
                    m_argsArray[i][j] = new uint[5];
                    m_argsBuffersArray[i][j] = new ComputeBuffer(1, m_argsArray[i][j].Length * sizeof(uint), ComputeBufferType.IndirectArguments);
                    m_argsArray[i][j][0] = (uint)m_LodSettings[i].mesh.GetIndexCount(j);    // - index count per instance,
                    m_argsArray[i][j][1] = (uint)m_instanceCount;                           // - instance count,
                    m_argsArray[i][j][2] = (uint)m_LodSettings[i].mesh.GetIndexStart(j);    // - start index location, 
                    m_argsArray[i][j][3] = (uint)0;                                         // - base vertex location
                    m_argsArray[i][j][4] = (uint)0;                                         // - start instance location.
                    m_argsBuffersArray[i][j].SetData(m_argsArray[i][j]);
                }
            }

            m_instanceData.m_dataDirty = false;
#if (UNITY_EDITOR)
            if (!Application.isPlaying)
            {
                SceneViewInstancing.UnregisterIndirectInstancedAsset(this);
                SceneViewInstancing.RegisterIndirectInstancedAsset(this);
            }
#endif

            m_positions = m_instanceData.m_positions;
            m_rotations = m_instanceData.m_rotations;
            m_customData = m_instanceData.m_customData;

            Vector4 aabbExtents = m_LodSettings[0].mesh.bounds.extents;
            aabbExtents.w = InstancingUtilities.BoundingSphereFromAABB(aabbExtents.x, aabbExtents.y, aabbExtents.z);
            Vector4 aabbCenter = m_LodSettings[0].mesh.bounds.center;
            Vector4 screenSpaceLODSize = Vector4.zero;
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                screenSpaceLODSize[i] = Mathf.Pow((m_LodSettings[i].screenSize * 0.5f), 2);
            }
            Vector2 screenDim = new Vector2(m_mainCamera.pixelWidth, m_mainCamera.pixelHeight);

            m_computeShader.SetVector("boundingExtents", aabbExtents);
            m_computeShader.SetVector("boundingCenter", aabbCenter);
            m_computeShader.SetVector("screenSpaceLODSize", screenSpaceLODSize);
            m_computeShader.SetVector("screenDim", screenDim);
            m_computeShader.SetInt("numLODs", (int)m_cachedNumLods);
            int[] shadowSettingsArray = new int[m_cachedNumLods];
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                shadowSettingsArray[i] = (int)m_LodSettings[i].shadowSetting;
            }
            m_computeShader.SetInts("castShadowsLOD", shadowSettingsArray);

            m_positionBuffer.SetData(m_positions);
            m_rotationBuffer.SetData(m_rotations);
            if (m_customDataBuffer != null)
                m_customDataBuffer.SetData(m_customData);

            m_computeShader.SetBuffer(m_cachedComputeKernel, "rotationBuffer", m_rotationBuffer);
            m_computeShader.SetBuffer(m_cachedComputeKernel, "positionBuffer", m_positionBuffer);
            if (m_customDataBuffer != null)
                m_computeShader.SetBuffer(m_cachedComputeKernel, "customDataBuffer", m_customDataBuffer);

            m_computeShader.SetBuffer(m_cachedShadowComputeKernel, "rotationBuffer", m_rotationBuffer);
            m_computeShader.SetBuffer(m_cachedShadowComputeKernel, "positionBuffer", m_positionBuffer);
            if (m_customDataBuffer != null)
                m_computeShader.SetBuffer(m_cachedShadowComputeKernel, "customDataBuffer", m_customDataBuffer);
        }

        private void UpdateComputeShader()
        {
            if (m_debugAppendCount)
            {
                m_argsBuffersArray[0][0].GetData(m_argsArray[0][0]);
                m_argsBuffersArray[1][0].GetData(m_argsArray[1][0]);
                m_argsBuffersArray[2][0].GetData(m_argsArray[2][0]);
                Debug.Log("Previous frame append count: " + m_argsArray[0][0][1].ToString("f3") + " and lod1: " + m_argsArray[1][0][1].ToString("f3")
                             + " and lod2: " + m_argsArray[2][0][1].ToString("f3"));
            }

#if UNITY_EDITOR
            if (m_dirtyPositionInstances.Count > 0 || m_dirtyRotationInstances.Count > 0 || m_dirtyScaleInstances.Count > 0)
            {
                if (m_dirtyPositionInstances.Count > 0 || m_dirtyScaleInstances.Count > 0)
                {
                    m_positionBuffer.SetData(m_instanceData.m_positions);
                    m_dirtyPositionInstances.Clear();
                    m_dirtyScaleInstances.Clear();
                }
                if (m_dirtyRotationInstances.Count > 0)
                {
                    m_rotationBuffer.SetData(m_instanceData.m_rotations);
                    m_dirtyRotationInstances.Clear();
                }
            }
#endif

            InstancingUtilities.GetFrustumPlanes(m_frustumPlanes, m_mainCamera);
            Vector4 screenSpaceLODSize = Vector4.zero;
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                screenSpaceLODSize[i] = Mathf.Pow((m_LodSettings[i].screenSize * 0.5f), 2);
            }
            Vector4 frustumBoundingSphere = InstancingUtilities.GenerateFrustumBoundingSphere(m_mainCamera, m_coarseCullFarDistance);
            int[] shadowSettingsArray = new int[m_cachedNumLods];
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                shadowSettingsArray[i] = (int)m_LodSettings[i].shadowSetting;
            }

            InstancingUtilities.UnrollFloats(m_frustumPlanes, m_frustumPlanesUnrolled);
            InstancingUtilities.UnrollFloats(m_shadowRegionPlanes, m_shadowRegionPlanesUnrolled);
            InstancingUtilities.UnrollFloats(m_mainCamera.projectionMatrix, m_cameraProjectionMatrixUnrolled);

            m_computeShader.SetVector("cameraData", m_mainCamera.transform.position);
            m_computeShader.SetFloats("cameraProjectionMatrix", m_cameraProjectionMatrixUnrolled);
            m_computeShader.SetFloats("frustumPlanes", m_frustumPlanesUnrolled);
            m_computeShader.SetFloats("shadowRegionPlanes", m_shadowRegionPlanesUnrolled);
            m_computeShader.SetVector("screenSpaceLODSize", screenSpaceLODSize);                                 //should not occur every update
            m_computeShader.SetFloat("Time", Time.time);
            m_computeShader.SetInts("castShadowsLOD", shadowSettingsArray);                                      //should not occur every update
            m_computeShader.SetVector("frustumBoundingSphere", frustumBoundingSphere);

            for (int i = 0; i < m_cachedNumLods; i++)
            {
                m_appendBuffers[i].SetCounterValue(0);
            }
            m_computeShader.Dispatch(m_cachedComputeKernel, Mathf.CeilToInt(m_instanceCount / 256f), 1, 1);

            for (int i = 0; i < m_cachedNumLods; i++)
            {
                for (int j = 0; j < m_LodSettings[i].mesh.subMeshCount; j++)
                {
                    ComputeBuffer.CopyCount(m_appendBuffers[i], m_argsBuffersArray[i][j], 1 * sizeof(uint));
                }
            }
        }

        public void Clear()
        {
            if (m_positionBuffer != null) { m_positionBuffer.Release(); }
            if (m_rotationBuffer != null) { m_rotationBuffer.Release(); }
            if (m_customDataBuffer != null) { m_customDataBuffer.Release(); }

            m_positionBuffer = null;
            m_rotationBuffer = null;
            m_customDataBuffer = null;

            for (int i = 0; i < m_cachedNumLods; i++)
            {
                if (m_appendBuffers != null)
                {
                    if (m_appendBuffers[i] != null)
                        m_appendBuffers[i].Release();
                    m_appendBuffers[i] = null;
                }

                if (m_argsBuffersArray != null)
                {
                    for (int j = 0; j < m_LodSettings[i].mesh.subMeshCount; j++)
                    {
                        if (m_argsBuffersArray[i][j] != null)
                            m_argsBuffersArray[i][j].Release();
                        m_argsBuffersArray[i][j] = null;
                    }
                }
            }

            ClearOnlyCommandBuffers();
        }

        //might want to name this to InitShadowsCommandBuffers()
        private void InitCommandBuffers()
        {
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                m_cachedLodShadowSettings[i] = m_LodSettings[i].shadowSetting;
            }

            m_shadowCasterCullingCommand = new CommandBuffer();
            m_shadowCasterCullingCommand.name = "Shadow Caster Culling";
            m_shadowCasterCullingCommand.DispatchCompute(m_computeShader, m_cachedShadowComputeKernel, Mathf.CeilToInt(m_instanceCount / 256f), 1, 1);
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                for (int j = 0; j < m_LodSettings[i].mesh.subMeshCount; j++)
                {
                    m_shadowCasterCullingCommand.CopyCounterValue(m_appendBuffers[i], m_argsBuffersArray[i][j], 1 * sizeof(uint));
                }
            }

            m_drawShadowCasterCommand = new CommandBuffer();
            m_drawShadowCasterCommand.name = "Draw Instanced Shadow Casters";
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                if (m_cachedLodShadowSettings[i] != ShadowCastingMode.Off)
                {
                    for (int j = 0; j < m_LodSettings[i].mesh.subMeshCount; j++)
                    {
                        int shadowCasterPass = m_materials[i][j].FindPass("SHADOWCASTER");
                        m_drawShadowCasterCommand.DrawMeshInstancedIndirect(m_LodSettings[i].mesh, j, m_materials[i][j], shadowCasterPass, m_argsBuffersArray[i][j], 0);
                    }
                }
            }

            //if lod0 doesnt draw shadows then no lod draw shadows for now
            if (m_cachedLodShadowSettings[0] != ShadowCastingMode.Off)
            {
                //the shadow caster culling pass finds shadow casters not visible to the camera, this lets us disable that at will
                if (m_castShadowIfCulled)
                    m_mainCamera.AddCommandBuffer(CameraEvent.BeforeLighting, m_shadowCasterCullingCommand);

                m_directionalLight.AddCommandBuffer(LightEvent.BeforeShadowMapPass, m_drawShadowCasterCommand);
            }
        }

        private void InitMotionVectorsCommandBuffer()
        {
            //render motion vectors
            m_drawMotionVectorsCommand = new CommandBuffer();
            m_drawMotionVectorsCommand.name = "Draw Instanced Motion Vectors";
            //Render targets are the motion vectors render target and the gbuffer depth render target
            m_drawMotionVectorsCommand.SetRenderTarget(m_motionVectorsRT, BuiltinRenderTextureType.ResolvedDepth);
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                if (m_LodSettings[i].objectMotionVectors == true)
                {
                    for (int j = 0; j < m_LodSettings[i].mesh.subMeshCount; j++)
                    {
                        int motionVectorPass = m_materials[i][j].FindPass("MOTIONVECTORS");
                        if (motionVectorPass > -1)
                        {
                            m_drawMotionVectorsCommand.DrawMeshInstancedIndirect(m_LodSettings[i].mesh, j, m_materials[i][j], motionVectorPass, m_argsBuffersArray[i][j], 0);
                        }
                    }
                }
            }

            //No motion vectors if highest LOD doesnt use them, can be changed later
            if (m_LodSettings[0].objectMotionVectors == true)
                m_mainCamera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, m_drawMotionVectorsCommand); //need to test if this will write into the correct place
        }

        private void ClearOnlyCommandBuffers()
        {
            if (m_shadowCasterCullingCommand != null && m_mainCamera != null)
            {
                m_mainCamera.RemoveCommandBuffer(CameraEvent.BeforeLighting, m_shadowCasterCullingCommand);
                m_shadowCasterCullingCommand.Clear();
            }
            if (m_drawShadowCasterCommand != null && m_directionalLight != null)
            {
                m_directionalLight.RemoveCommandBuffer(LightEvent.BeforeShadowMapPass, m_drawShadowCasterCommand);
                m_drawShadowCasterCommand.Clear();
            }
            if (m_drawMotionVectorsCommand != null && m_mainCamera != null)
            {
                m_mainCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, m_drawMotionVectorsCommand);
                m_drawMotionVectorsCommand.Clear();
            }

            m_shadowCasterCullingCommand = null;
            m_drawShadowCasterCommand = null;
            m_drawMotionVectorsCommand = null;
        }

        private void Initialize(bool updateCount = true, bool updateShadows = true)
        {
            Debug.Log("Calling Initialize IIC");

            if (m_mainCamera == null) { m_mainCamera = Camera.main; }

            //we need to reinitialize all buffers on the gpu for indirect drawing, this also requires recreation of command buffers
            if (updateCount) { Clear(); InitComputeShader(); InitCommandBuffers(); }

            //we need only to alter the command buffers
            if (updateShadows && !updateCount) { ClearOnlyCommandBuffers(); InitCommandBuffers(); }
        }

#if UNITY_EDITOR
        public void SetComputeBuffer(ComputeBuffer bufferToSet, string bufferName)
        {
            m_computeShader.SetBuffer(m_cachedComputeKernel, bufferName, bufferToSet);
        }

        public void ReloadAllComputerBuffers()
        {
            m_positionBuffer.SetData(m_instanceData.m_positions);
            m_rotationBuffer.SetData(m_instanceData.m_rotations);
        }

        public void AddDirtyPosition(int instanceID)
        {
            if (!m_dirtyPositionInstances.Contains(instanceID))
                m_dirtyPositionInstances.Add(instanceID);
        }
        public void AddDirtyRotation(int instanceID)
        {
            if (!m_dirtyRotationInstances.Contains(instanceID))
                m_dirtyRotationInstances.Add(instanceID);
        }
        public void AddDirtyScale(int instanceID)
        {
            if (!m_dirtyScaleInstances.Contains(instanceID))
                m_dirtyScaleInstances.Add(instanceID);
        }
#endif

        private void OnDisable()
        {
            Clear();

            Debug.Log("Disabled");
#if (UNITY_EDITOR)
            if (!Application.isPlaying)
                SceneViewInstancing.UnregisterIndirectInstancedAsset(this);
#endif
        }

        private void OnEnable()
        {
            Debug.Log("Enabled");

            m_mainCamera = Camera.main;
#if (UNITY_EDITOR)
            if (!Application.isPlaying)
            {
                if (UnityEditor.SceneView.GetAllSceneCameras().Length > 0)
                    m_mainCamera = UnityEditor.SceneView.GetAllSceneCameras()[0];
            }
#endif
            m_computeShader = (ComputeShader)Instantiate(m_computeShaderReference);
            m_cachedNumLods = Mathf.Min(m_LodSettings.Length, 4);
            m_cachedComputeKernel = m_computeShader.FindKernel("DrawPrep");
#if (UNITY_EDITOR)
            if (!Application.isPlaying)
                m_cachedComputeKernel = m_computeShader.FindKernel("EditorDrawPrep");
#endif
            m_cachedShadowComputeKernel = m_computeShader.FindKernel("ShadowDrawPrep");
            m_drawCallBounds = new Bounds(Vector3.zero, new Vector3(1440.0f, 600.0f, 1440.0f)); //can maybe have this be something we read from the asset data

            //Initialize all arrays      
            m_appendBuffers = new ComputeBuffer[m_cachedNumLods];
            m_argsBuffersArray = new ComputeBuffer[m_cachedNumLods][];
            m_argsArray = new uint[m_cachedNumLods][][];
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                m_argsBuffersArray[i] = new ComputeBuffer[m_LodSettings[i].mesh.subMeshCount];
                m_argsArray[i] = new uint[m_LodSettings[i].mesh.subMeshCount][];
            }
            m_cachedLodShadowSettings = new ShadowCastingMode[m_cachedNumLods];

            m_materials = new Material[m_cachedNumLods][];

            //Instantiate all the materials so that we do not write over the original materials
            //for (int i = 0; i < m_cachedNumLods; i++)
            //{
            //    m_materials[i] = new Material[m_LodSettings[i].materialReferences.Length];
            //    for (int j = 0; j < m_LodSettings[i].materialReferences.Length; j++)
            //    {
            //        m_materials[i][j] = Instantiate(m_LodSettings[i].materialReferences[j]) as Material;
            //    }
            //}

            //opted to not instantiate so editor gives immediate feedback to material changes
            for (int i = 0; i < m_cachedNumLods; i++)
            {
                int numMaterials = m_LodSettings[i].materialReferences.Length;
                m_materials[i] = new Material[numMaterials];
                for (int j = 0; j < numMaterials; j++)
                {
                    m_materials[i][j] = m_LodSettings[i].materialReferences[j].material;
                }
            }

            Initialize();

#if (UNITY_EDITOR)
            if (!Application.isPlaying)
                SceneViewInstancing.RegisterIndirectInstancedAsset(this);
#endif
        }

    }
}

//Without dynamic parallelism a quad-tree wont improve things. Using more draw calls hurts performance, and low occupancy will also hurt performance
//Organizing the scene also didnt seem to help for some reason even though it should mean less divergence, need to look more into that. 

//[https://forum.unity.com/threads/unity-5-lighting-shadows-coordinates-and-stuff-shader-pro-needed.376875/]