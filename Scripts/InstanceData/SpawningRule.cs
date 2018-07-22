using System.Collections.Generic;
using UnityEngine;


namespace JacksInstancing
{
    [CreateAssetMenu(fileName = "InstanceSpawnRule", menuName = "InstancingSpawnRule", order = 2)]
    [System.Serializable]
    public class SpawningRule : ScriptableObject
    {
        public int m_layer;
        public float m_instanceDensity = 3; //per 100m squared
        public float m_gridJitter;
        //public float m_minScaleX = 1;
        //public float m_maxScaleX = 1;
        //public float m_minScaleY = 1;
        //public float m_maxScaleY = 1;
        //public float m_minScaleZ = 1;
        //public float m_maxScaleZ = 1;
        //public bool m_randomXAngle = false;
        //public bool m_randomYAngle = true;
        //public bool m_randomZAngle = false;

    }
}
