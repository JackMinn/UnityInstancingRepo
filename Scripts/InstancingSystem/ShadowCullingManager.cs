using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace JacksInstancing
{
    [ExecuteInEditMode]
    public class ShadowCullingManager : MonoBehaviour
    {
        public Light m_directionalLight;
        public ComputeShader m_computeShader;
        public int m_yAxisDownsample = 1;
        [Range(0, 1)] public float m_maxShadowRange = 0.7f;
        private bool m_readMaxDepth; //private for now since its meaningless
        public bool m_readShadowRegionPlanes;

        private Camera m_mainCamera;
        private Vector4 m_cachedCameraData;
        private CommandBuffer m_computeShadowBoundingRegion;

        private int m_cachedComputeMaxDepthKernel;
        private int m_cachedComputeShadowRegionPlanesKernel;
        private ComputeBuffer m_maxDepthBuffer;
        private ComputeBuffer m_shadowRegionPlanes;
        public ComputeBuffer _m_shadowRegionPlanes
        {
            get
            {
                if (m_shadowRegionPlanes == null) { m_shadowRegionPlanes = new ComputeBuffer(m_shadowRegionPlanesArray.Length, 4 * sizeof(float)); }
                return m_shadowRegionPlanes;
            }
            private set { m_shadowRegionPlanes = value; }
        }
        private uint[] m_maxDepthArray = new uint[1] { 0 };
        private Vector4[] m_shadowRegionPlanesArray = new Vector4[12];

        private Plane[] m_frustumPlanes = new Plane[6];
        private float[] m_frustumPlanesUnrolled = new float[24];
        private float[] m_cameraToWorldMatrixUnrolled = new float[16];

        // Use this for initialization
        private void Awake()
        {
            m_mainCamera = Camera.main;
            m_cachedComputeMaxDepthKernel = m_computeShader.FindKernel("ComputeMaxDepth");
            m_cachedComputeShadowRegionPlanesKernel = m_computeShader.FindKernel("ComputeShadowRegionPlanes");

            Initialize();
        }

        public void Update()
        {
            UpdateComputeShader();

            if (m_readMaxDepth || m_readShadowRegionPlanes)
                StartCoroutine(DebugInfo());
        }

        private void InitComputeShader()
        {
            m_maxDepthBuffer = new ComputeBuffer(1, sizeof(uint));
            if (m_shadowRegionPlanes == null)
                m_shadowRegionPlanes = new ComputeBuffer(m_shadowRegionPlanesArray.Length, 4 * sizeof(float));

            m_maxDepthBuffer.SetData(m_maxDepthArray);
            m_computeShader.SetBuffer(m_cachedComputeMaxDepthKernel, "maxDepth", m_maxDepthBuffer);

            m_shadowRegionPlanes.SetData(m_shadowRegionPlanesArray);
            m_computeShader.SetBuffer(m_cachedComputeShadowRegionPlanesKernel, "readOnlyMaxDepth", m_maxDepthBuffer);
            m_computeShader.SetBuffer(m_cachedComputeShadowRegionPlanesKernel, "shadowRegionPlanes", m_shadowRegionPlanes);
        }

        private void UpdateComputeShader()
        {
            m_cachedCameraData.Set(m_mainCamera.nearClipPlane, m_mainCamera.farClipPlane,
                                   m_mainCamera.fieldOfView * Mathf.Deg2Rad, m_mainCamera.aspect);

            InstancingUtilities.GetFrustumPlanes(m_frustumPlanes, m_mainCamera);
            InstancingUtilities.UnrollFloats(m_frustumPlanes, m_frustumPlanesUnrolled);
            InstancingUtilities.UnrollFloats(m_mainCamera.transform.localToWorldMatrix, m_cameraToWorldMatrixUnrolled);

            //Used to linearize Z buffer values. x is (1 - far / near), y is (far / near), z is (x / far) and w is (y / far).
            float farOverNear = m_mainCamera.farClipPlane / m_mainCamera.nearClipPlane;
            Vector4 _ZBufferParams = new Vector4(1f - farOverNear, farOverNear, 1, 1);

            m_computeShader.SetTextureFromGlobal(m_cachedComputeMaxDepthKernel, "_DepthTexture", "_CameraDepthTexture");
            m_computeShader.SetVector("_ZBufferParams", _ZBufferParams);
            m_computeShader.SetFloat("maxShadowRange", m_maxShadowRange);
            m_computeShader.SetInt("yAxisDownsample", m_yAxisDownsample);

            m_computeShader.SetFloats("frustumPlanes", m_frustumPlanesUnrolled);
            m_computeShader.SetVector("camData", m_cachedCameraData);
            m_computeShader.SetFloats("cameraToWorldMatrix", m_cameraToWorldMatrixUnrolled);
            m_computeShader.SetVector("lightDir", m_directionalLight.transform.forward);
        }

        private IEnumerator DebugInfo()
        {
            yield return new WaitForEndOfFrame();

            GetDebugInfo();
        }

        public void GetDebugInfo()
        {
            //useless for now since GPU will reset max depth to 0, so this will always read 0
            if (m_readMaxDepth)
            {
                m_maxDepthBuffer.GetData(m_maxDepthArray);
                float depth = m_maxDepthArray[0] / 4294967295f;
                Debug.Log("Previous depth buffer max value: " + depth.ToString("f7"));
                m_maxDepthArray[0] = 0;
            }

            if (m_readShadowRegionPlanes)
            {
                m_shadowRegionPlanes.GetData(m_shadowRegionPlanesArray);
                for (int i = 1; i < m_shadowRegionPlanesArray[0].x + 1; i++)
                {
                    Debug.Log("Index " + i + " is: " + m_shadowRegionPlanesArray[i].ToString("f3"));
                }
            }
        }

        public void Clear()
        {
            if (m_shadowRegionPlanes != null) { m_shadowRegionPlanes.Release(); }
            if (m_maxDepthBuffer != null) { m_maxDepthBuffer.Release(); }

            m_shadowRegionPlanes = null;
            m_maxDepthBuffer = null;

            if (m_computeShadowBoundingRegion != null && m_mainCamera != null)
            {
                m_mainCamera.RemoveCommandBuffer(CameraEvent.AfterGBuffer, m_computeShadowBoundingRegion);
                m_computeShadowBoundingRegion.Clear();
            }

            m_computeShadowBoundingRegion = null;
        }

        private void InitCommandBuffers()
        {
            m_computeShadowBoundingRegion = new CommandBuffer();
            m_computeShadowBoundingRegion.name = "Compute Shadow Bounding Region";
            int gridDim_x = Mathf.CeilToInt((m_mainCamera.pixelWidth / 128f) * 0.5f);
            m_computeShadowBoundingRegion.DispatchCompute(m_computeShader, m_cachedComputeMaxDepthKernel, gridDim_x, m_mainCamera.pixelHeight / m_yAxisDownsample, 1);
            m_computeShadowBoundingRegion.DispatchCompute(m_computeShader, m_cachedComputeShadowRegionPlanesKernel, 1, 1, 1);

            m_mainCamera.AddCommandBuffer(CameraEvent.AfterGBuffer, m_computeShadowBoundingRegion);
        }

        private void Initialize()
        {
            Clear();
            InitComputeShader();
            InitCommandBuffers();
        }

        private void OnDisable()
        {
            Clear();
        }

        void OnEnable()
        {

        }

    }
}
