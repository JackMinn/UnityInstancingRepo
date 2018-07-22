using System.Collections.Generic;
using UnityEngine;


namespace JacksInstancing
{
    [System.Serializable]
    public struct SpawnRule
    {
        [SerializeField] public int layer;
        [SerializeField] public float density;
        [SerializeField] public float gridJitter;
    }

    public enum ScaleMode {
        Uniform,
        Freeform,
        LockXZ
    };


    [CreateAssetMenu(fileName = "InstanceSpawnData", menuName = "IndirectInstancing", order = 1)]
    public class EnvironmentInstanceData : ScriptableObject
    {
        public string m_instanceName = "Insert Instance Name";
        public int m_instanceCount;
        public bool m_UseCustomData = true;
        public int m_legacyLayer;
        //public int m_instanceDensity = 3; //per 100m squared
        //[Range(0, 1)] public float m_gridJitter;
        public ScaleMode m_scaleMode = ScaleMode.Uniform;
        public float m_minScaleX = 1;
        public float m_maxScaleX = 1;
        public float m_minScaleY = 1;
        public float m_maxScaleY = 1;
        public float m_minScaleZ = 1;
        public float m_maxScaleZ = 1;
        public bool m_randomXAngle = false;
        public bool m_randomYAngle = true;
        public bool m_randomZAngle = false;

        public SpawningRule[] m_spawningRules;


        [HideInInspector] public bool m_dataDirty = false;

        [HideInInspector] public Vector4[] m_positions;
        [HideInInspector] public Vector4[] m_rotations;
        [HideInInspector] public Vector4[] m_customData;

        [HideInInspector] public Terrain m_activeTerrain;

        public void GenerateData()
        {
            Debug.Log("Current instance count is: " + m_instanceCount + ". New instance count is: " + m_instanceCount + ". Generating Data for " + m_instanceName);

            m_positions = new Vector4[m_instanceCount];
            m_rotations = new Vector4[m_instanceCount];
            if (m_UseCustomData)
                m_customData = new Vector4[m_instanceCount];
            else
                m_customData = null;

            PopulateDataArrays();
            m_dataDirty = true;
#if (UNITY_EDITOR)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        //legacy function to build arrays
        private void PopulateDataArrays()
        {
            int spawnedCount = 0;
            Terrain t = Terrain.activeTerrain;
            while (spawnedCount < m_positions.Length)
            {
                float x = Random.Range(-720f, 720f);
                float y = 0;
                float z = Random.Range(-720f, 720f);

                float u = (x + 720f) / t.terrainData.size.x;
                float v = (z + 720f) / t.terrainData.size.z;
                u = Mathf.Round(u * t.terrainData.alphamapWidth);
                v = Mathf.Round(v * t.terrainData.alphamapHeight);

                Color c = t.terrainData.alphamapTextures[0].GetPixel((int)u, (int)v);
                float layer = Mathf.Max(c.r, c.g, c.b, c.a);

                if (m_legacyLayer != -1)
                {
                    if (Mathf.Approximately(c[m_legacyLayer], layer))
                    {
                        float size = InstancingUtilities.PackScaleVectorToFloat(GenerateScale());

                        Vector3 position = new Vector3(x, y, z);
                        m_positions[spawnedCount] = new Vector4(position.x, position.y, position.z, size);
                        m_rotations[spawnedCount] = GenerateRotation();
                        if (m_customData != null)
                            m_customData[spawnedCount] = GenerateCustomData();
                        spawnedCount++;
                    }
                }
                else if (m_legacyLayer == -1)
                {
                    float size = InstancingUtilities.PackScaleVectorToFloat(GenerateScale());

                    Vector3 position = new Vector3(x, y, z);
                    m_positions[spawnedCount] = new Vector4(position.x, position.y, position.z, size);
                    m_rotations[spawnedCount] = GenerateRotation();
                    if (m_customData != null)
                        m_customData[spawnedCount] = GenerateCustomData();
                    spawnedCount++;
                }
            }
        }

        private Vector3 GenerateScale()
        {
            float scaleX = 0;
            float scaleY = 0;
            float scaleZ = 0;
            switch (m_scaleMode)
            {
                case ScaleMode.Uniform:
                    scaleX = Random.Range(m_minScaleX, m_maxScaleX);
                    scaleY = scaleX;
                    scaleZ = scaleX;
                    break;
                case ScaleMode.Freeform:
                    scaleX = Random.Range(m_minScaleX, m_maxScaleX);
                    scaleY = Random.Range(m_minScaleY, m_maxScaleY);
                    scaleZ = Random.Range(m_minScaleZ, m_maxScaleZ);
                    break;
                case ScaleMode.LockXZ:
                    scaleX = Random.Range(m_minScaleX, m_maxScaleX);
                    scaleY = Random.Range(m_minScaleY, m_maxScaleY);
                    scaleZ = scaleX;
                    break;
            }
            

            return new Vector3(scaleX, scaleY, scaleZ);
        }

        private Vector4 GenerateRotation()
        {
            float xAngle = m_randomXAngle ? Random.Range(0f, 1f) * 360f : 0;
            float yAngle = m_randomYAngle ? Random.Range(0f, 1f) * 360f : 0;
            float zAngle = m_randomZAngle ? Random.Range(0f, 1f) * 360f : 0;
            Quaternion temp = Quaternion.Euler(xAngle, yAngle, zAngle);
            return InstancingUtilities.VectorFromQuaternion(temp);
        }

        private Vector4 GenerateCustomData()
        {
            float red = Random.Range(0.5f, 1.0f);
            red = Random.Range(0.7f, 1.1f);
            float green = Random.Range(0.5f, 1.0f);
            green = Random.Range(0.85f, 1.1f);
            float blue = Random.Range(0.5f, 1.0f);
            blue = Random.Range(0.6f, 1.0f);
            float zOffset = Random.Range(0.3f, 300000f);
            return new Vector4(red, green, blue, zOffset);
        }

        public void ModifyScale(int index, Vector3 scale, float original)
        {
            //unpack, vector multiply to scale, repack, store
            Vector3 originalUnpacked = InstancingUtilities.UnpackScaleVectorFromFloat(original);
            originalUnpacked.Scale(scale);
            float modifiedPacked = InstancingUtilities.PackScaleVectorToFloat(originalUnpacked);
            m_positions[index].w = modifiedPacked;
        }

        public void GenerateGridInstances()
        {
            Terrain t = Terrain.activeTerrain;
            Vector3 terrainSize = t.terrainData.size;

            Vector3[] totalSpawnedInstances = new Vector3[0];
            for (int i = 0; i < m_spawningRules.Length; i++)
            {
                int sqrtMaxInstances = Mathf.CeilToInt(Mathf.Sqrt(Mathf.Abs((terrainSize.x * terrainSize.z * m_spawningRules[i].m_instanceDensity / 100f))));
                float stepSize = 1f / sqrtMaxInstances;

                float maxJitter1D = Mathf.Clamp01(m_spawningRules[i].m_gridJitter) * stepSize * 0.5f; //divide by 2 because we use jitter as +/-
                Vector3 maxJitter = new Vector3(maxJitter1D, 0f, maxJitter1D);
                maxJitter.Scale(terrainSize); //the jitter needs to be in world size

                Vector3[] possibleInstances = new Vector3[sqrtMaxInstances * sqrtMaxInstances];
                int spawnCount = 0;
                //Unity terrains do not have their origin at the center, but rather at the bottom left hand side, this is why the for loops can start from 0
                for (int x = 0; x < sqrtMaxInstances; x++)
                {
                    for (int z = 0; z < sqrtMaxInstances; z++)
                    {
                        float worldStepX = x * stepSize * terrainSize.x;
                        float worldStepZ = z * stepSize * terrainSize.z;
                        Vector3 pos = new Vector3(t.GetPosition().x + worldStepX, 0, t.GetPosition().z + worldStepZ);
                        pos += new Vector3(Random.Range(-1f, 1f) * maxJitter.x, 0, Random.Range(-1f, 1f) * maxJitter.z);

                        if (m_spawningRules[i].m_layer != -1)
                        {
                            Color layerWeight = new Color(0, 0, 0, 0);
                            if (GetSplatWeightAtLocation(pos, t, ref layerWeight))
                            {
                                if (layerWeight[m_spawningRules[i].m_layer] > 0 && layerWeight[m_spawningRules[i].m_layer] > Random.Range(0f, 1f))
                                {
                                    //we can successfully generate something now!
                                    possibleInstances[spawnCount] = pos;
                                    spawnCount++;
                                }
                            }
                        }
                        else if (m_spawningRules[i].m_layer == -1)
                        {
                            possibleInstances[spawnCount] = pos;
                            spawnCount++;
                        }
                    }
                }

                totalSpawnedInstances = MergeArraysWithCount<Vector3>(totalSpawnedInstances, possibleInstances, spawnCount);
            }

            m_positions = new Vector4[totalSpawnedInstances.Length];
            m_rotations = new Vector4[totalSpawnedInstances.Length];
            if (m_UseCustomData)
                m_customData = new Vector4[totalSpawnedInstances.Length];

            for (int i = 0; i < totalSpawnedInstances.Length; i++)
            {
                m_positions[i] = new Vector4(totalSpawnedInstances[i].x, totalSpawnedInstances[i].y, totalSpawnedInstances[i].z, InstancingUtilities.PackScaleVectorToFloat(GenerateScale()));
                m_rotations[i] = GenerateRotation();
                if (m_customData != null)
                    m_customData[i] = GenerateCustomData();
            }

            m_instanceCount = totalSpawnedInstances.Length;

            Debug.Log("Number of instances generated: " + totalSpawnedInstances.Length);
            m_dataDirty = true;
#if (UNITY_EDITOR)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        //takes a position in world space and transforms to terrain's local space for sampling (which is bottom left at (0,0))
        private bool GetSplatWeightAtLocation(Vector3 pos, Terrain t, ref Color outColor)
        {
            float u = (pos.x - t.transform.position.x) / t.terrainData.size.x;
            float v = (pos.z - t.transform.position.z) / t.terrainData.size.z;
            if (u > 1 || v > 1)
            {
                Debug.Log("World position is outside of terrain's bounds. Cannot Sample.");
                return false;
            }
            u = u * t.terrainData.alphamapWidth;
            v = v * t.terrainData.alphamapHeight;

            int x1 = Mathf.FloorToInt(u);
            int z1 = Mathf.FloorToInt(v);
            int x2 = Mathf.CeilToInt(u);
            int z2 = Mathf.CeilToInt(v);
            float fracX = u - x1;
            float fracZ = v - z1;

            //only programmed to handle 1 splat map at the moment, so 4 textures
            Color sample1 = t.terrainData.alphamapTextures[0].GetPixel(x1, z1);
            Color sample2 = t.terrainData.alphamapTextures[0].GetPixel(x2, z1);
            Color sample3 = t.terrainData.alphamapTextures[0].GetPixel(x1, z2);
            Color sample4 = t.terrainData.alphamapTextures[0].GetPixel(x2, z2);

            //bilinear interpolation
            Color result = Color.Lerp(
                Color.Lerp(sample1, sample2, fracX),
                Color.Lerp(sample3, sample4, fracX),
                fracZ);

            outColor = result;
            return true;
        }







        //dont use this for now, it needs refining and it didnt even seem to improve warp coherency, compute shader speed remained unchanged
        private void SortPositionArray(Vector4[] positions)
        {
            float dimX = 16;
            float dimZ = 16;
            float spacingX = 1440 / (dimX * 2);
            float spacingZ = 1440 / (dimZ * 2);

            Vector3[] centerPositions = new Vector3[(int)(dimX * dimZ)];
            List<Vector4>[] listArray = new List<Vector4>[(int)(dimX * dimZ)];

            //We now need to populate this array of center positions
            for (int j = 0; j < dimZ; j++)
            {
                for (int i = 0; i < dimX; i++)
                {
                    centerPositions[(int)(j * dimZ) + i] = new Vector3(i * spacingX * 2 + spacingX, 0, i * spacingZ * 2 + spacingZ);
                    listArray[(int)(j * dimZ) + i] = new List<Vector4>();
                }
            }

            for (int i = 0; i < positions.Length; i++)
            {
                float maxDistance = 0;
                int listIndex = 0;
                for (int j = 0; j < centerPositions.Length; j++)
                {
                    Vector3 directionVec = new Vector3(centerPositions[j].x - positions[i].x, centerPositions[j].y - positions[i].y, centerPositions[j].z - positions[i].z);
                    float tempDistance = directionVec.sqrMagnitude;
                    if (tempDistance > maxDistance)
                    {
                        maxDistance = tempDistance;
                        listIndex = j;
                    }
                }
                listArray[listIndex].Add(positions[i]);
            }

            //now copy each list back into the original array - consider sorting the list first later
            int positionArrayIndex = 0;
            for (int i = 0; i < listArray.Length; i++)
            {
                listArray[i].CopyTo(positions, positionArrayIndex);
                positionArrayIndex += listArray[i].Count;
            }
        }

        private T[] MergeArraysWithCount<T>(T[] destinationArray, T[] sourceArray, int count)
        {
            T[] result = new T[destinationArray.Length + count];
            destinationArray.CopyTo(result, 0);
            for(int i = 0; i < count; i++)
            {
                result[destinationArray.Length + i] = sourceArray[i];
            }
            return result;
        }
    }
}
