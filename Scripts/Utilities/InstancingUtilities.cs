using System.Collections.Generic;
using UnityEngine;

namespace JacksInstancing
{
    public class InstancingUtilities
    {

        public static void UnrollFloats(Plane[] rolled, float[] unrolled)
        {
            for (int i = 0; i < rolled.Length; i++)
            {
                int tempIndex = i * 4;
                unrolled[tempIndex] = rolled[i].normal.x;
                unrolled[tempIndex + 1] = rolled[i].normal.y;
                unrolled[tempIndex + 2] = rolled[i].normal.z;
                unrolled[tempIndex + 3] = rolled[i].distance;
            }
        }

        public static void UnrollFloats(Matrix4x4 rolled, float[] unrolled)
        {
            for (int i = 0; i < 16; i++)
            {
                unrolled[i] = rolled[i];
            }
        }

        public static Vector4 HomogeneousFromVector3(Vector3 vec)
        {
            return new Vector4(vec.x, vec.y, vec.z, 1);
        }

        public static Quaternion QuaternionFromVector(Vector4 vec)
        {
            vec.Normalize();
            return new Quaternion(vec.x, vec.y, vec.z, vec.w);
        }

        public static Vector4 VectorFromQuaternion(Quaternion quat)
        {
            return new Vector4(quat.x, quat.y, quat.z, quat.w);
        }

        public static Vector3 MultiplyVector3(Vector3 vec1, Vector3 vec2)
        {
            return new Vector3(vec1.x * vec2.x, vec1.y * vec2.y, vec1.z * vec2.z);
        }

        //Using max lets us have dynamic resolution, in somehwat the same vain as floats do.
        public static float PackScaleVectorToFloat(Vector3 scale)
        {
            float max = Mathf.Max(scale.x, scale.y, scale.z);
            max = Mathf.Ceil(max);
            scale.Scale(new Vector3(1f / max, 1f / max, 1f / max));

            //due to catastrophic cancellation, the accuracy on x isnt as good, so we clip it earlier to prevent discontinuities
            float packedX = Mathf.Min(Mathf.Round(scale.x * 255), 250);  
            float packedY = Mathf.Min(Mathf.Round(scale.y * 255), 253) * 255;
            float packedZ = Mathf.Min(Mathf.Round(scale.z * 255), 253) * 255 * 255;

            //max is always larger than 1... although that is not necessarily a good assumption to make
            max *= 255 * 255 * 255; 
            return packedX + packedY + packedZ + max;
        }

        public static Vector3 UnpackScaleVectorFromFloat(float value)
        {
            float x = (value % 255f)/255f;
            value = Mathf.Floor(value / 255);
            float y = (value % 255f)/255f;
            value = Mathf.Floor(value / 255);
            float z = (value % 255f)/255f;

            value = Mathf.Floor(value / 255);
            float max = value;

            Vector3 scale = new Vector3(x * max, y * max, z * max);
            return scale;
        }

        public static float BoundingSphereFromAABB(float x, float y, float z)
        {
            Vector3 minPoint = new Vector3(-x, -y, -z);
            Vector3 maxPoint = new Vector3(x, y, z);
            float distance = Vector3.Magnitude(maxPoint - minPoint);
            return distance * 0.5f;
        }

        //https://lxjk.github.io/2017/04/15/Calculate-Minimal-Bounding-Sphere-of-Frustum.html
        public static Vector4 GenerateFrustumBoundingSphere(Camera targetCamera, float coarseCullFarDistance = 1)
        {
            float k = Mathf.Sqrt(1 + Mathf.Pow((targetCamera.pixelHeight / targetCamera.pixelWidth), 2)) * Mathf.Tan(targetCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float kk = k * k;
            Vector3 center;
            float radius;

            float farPlane = coarseCullFarDistance * targetCamera.farClipPlane;
            float nearPlane = targetCamera.nearClipPlane;
            float farMinusNear = farPlane - nearPlane;
            float farPlusNear = farPlane + nearPlane;

            if (kk >= (farMinusNear / farPlusNear))
            {
                center = new Vector3(0, 0, farPlane);
                radius = k * farPlane;
            }
            else
            {
                center = new Vector3(0, 0, 0.5f * farPlusNear * (1 + kk));
                radius = 0.5f * Mathf.Sqrt((farMinusNear * farMinusNear) + 2 * (farPlane * farPlane + nearPlane * nearPlane) * kk + (farPlusNear * farPlusNear * kk * kk));
            }

            center = targetCamera.transform.localToWorldMatrix * (new Vector4(center.x, center.y, center.z, 1));

            return new Vector4(center.x, center.y, center.z, radius);
        }

        private static Plane[] m_tempPlanes = new Plane[6];
        public static void GetFrustumPlanes(Plane[] m_frustumPlanes, Camera targetCamera)
        {
            m_tempPlanes = GeometryUtility.CalculateFrustumPlanes(targetCamera);
            //use ordering to match shadow caster culling planes
            m_frustumPlanes[0] = m_tempPlanes[0];
            m_frustumPlanes[1] = m_tempPlanes[1];
            m_frustumPlanes[2] = m_tempPlanes[3];
            m_frustumPlanes[3] = m_tempPlanes[2];
            m_frustumPlanes[4] = m_tempPlanes[4];
            m_frustumPlanes[5] = m_tempPlanes[5];
        }
    }

#if (UNITY_EDITOR)
    public class EditorInstancingUtilities
    {

        public static void GetDesiredObjectTypesInScene<T>(List<T> desiredObjectList)
        {
            GameObject[] rootObjectsInScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < rootObjectsInScene.Length; i++)
            {
                T desiredObjectInScene = rootObjectsInScene[i].GetComponent<T>();
                if (desiredObjectInScene != null)
                {
                    desiredObjectList.Add(desiredObjectInScene);
                }
            }
        }
    }
#endif
}
