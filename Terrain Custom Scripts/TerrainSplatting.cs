#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class TerrainSplatting : MonoBehaviour {
    public float m_zoom;

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
        //TestTerrain();
	}

    private void TestTerrain()
    {
        Terrain terrain = Terrain.activeTerrain;
        Texture2D splat0 = terrain.terrainData.alphamapTextures[0];
                           //terrain.terrainData.splatPrototypes[0].texture;
        Debug.Log(splat0.GetPixel(1, 1));
    }

    public void SetSplatMap()
    {
        Terrain t = Terrain.activeTerrain;
        float[,,] splatMap = new float[t.terrainData.alphamapWidth, t.terrainData.alphamapHeight, 2];

        // For each point on the alphamap...
        for (int y = 0; y < t.terrainData.alphamapHeight; y++)
        {
            for (int x = 0; x < t.terrainData.alphamapWidth; x++)
            {
                // Get the normalized terrain coordinate that
                // corresponds to the the point.
                float normX = x * 1f / (t.terrainData.alphamapWidth - 1);
                float normY = y * 1f / (t.terrainData.alphamapHeight - 1);
                normX *= m_zoom;
                normY *= m_zoom;

                float alpha = Mathf.PerlinNoise(normX, normY);
                alpha = Mathf.Pow(alpha, 0.95f);
                alpha -= Random.Range(0f, 0.05f);
                alpha = alpha > 0.5f ? 1 : 0;
                splatMap[x, y, 0] = alpha;
                splatMap[x, y, 1] = 1 - alpha;
            }
        }

        t.terrainData.SetAlphamaps(0, 0, splatMap);
    }
}

#endif