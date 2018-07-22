#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JacksInstancing
{
    //Singleton so that there is only one undo state for the instancing system.
    public class UndoStateInstance
    {
        private static UndoState m_internal_thisUndoState;
        public static UndoState m_instance
        {
            get
            {
                if (m_internal_thisUndoState == null)
                {
                    Debug.Log("Making new SO.");
                    m_internal_thisUndoState = ScriptableObject.CreateInstance<UndoState>();
                }
                return m_internal_thisUndoState;
            }
        }
    }

    //Stores the delta of changes as an undo component via an SO, rather than storing the entire instancing data as that is too memory intensive
    public class UndoState : ScriptableObject
    {
        public List<Vector4> m_savedData = new List<Vector4>();
        public List<Vector2> m_instanceInfo = new List<Vector2>();
        public long  m_modifiedCounter = 0;
        public Tool m_lastUsedTool;

        public void SetUndoState(List<Vector4> data, List<Vector2> instance)
        {
            Debug.Log("Calling set undo state.");
            m_savedData.Clear();
            m_instanceInfo.Clear();
            m_modifiedCounter = 0;
            if (data.Count == instance.Count)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    Debug.Log("adding");
                    m_savedData.Add(data[i]);
                    m_instanceInfo.Add(instance[i]);
                }
            }
            else
            {
                Debug.Log("Count of instance data and instance list do not match, this will produce unintended behavior and has been denied.");
            }
        }

        public void IncrementModifiedCounter()
        {
            m_modifiedCounter++;
        }

    }
}

#endif