using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    private float heightMultiplier = 20f;

    private int resolution = 10;

    private PerlinNoiseHeightProvider perlinNoiseHeightProvider;

    public bool Initialize()
    {
        perlinNoiseHeightProvider = PerlinNoiseHeightProvider.GetInstance();
        if (perlinNoiseHeightProvider == null)
        {
            Debug.LogError("Terrain generation requires a scene height provider.", this);
            return false;
        }
        return true;
    }
    public void GenerateMeshData(Vector2Int chunkCoord, float chunkSize, GameObject terrain)
    {
        if (perlinNoiseHeightProvider == null)
            throw new System.InvalidOperationException("Initialize the terrain generator before generating chunks.");
        if (resolution <= 0 || chunkSize <= 0 || float.IsNaN(chunkSize) || float.IsInfinity(chunkSize))
            throw new System.ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size and mesh resolution must be positive and finite.");
        if (terrain == null || !terrain.TryGetComponent<MeshFilter>(out var terrainMeshFilter) || terrainMeshFilter.sharedMesh == null)
            throw new System.ArgumentException("Terrain requires a MeshFilter with an owned mesh.", nameof(terrain));

        Vector3[] vertices = new Vector3[(resolution + 1) * (resolution + 1)];
        int[] triangle = new int[resolution * resolution * 6];

        float stepOffset = chunkSize / resolution;
        int vertexIndex = 0;
        for (int z = 0; z <= resolution; z++)
        {
            for ( int x = 0; x <= resolution; x++)
            {
                vertices[vertexIndex] = new Vector3(x * stepOffset, 0, z * stepOffset);
                var worldPos = new Vector3((chunkCoord.x * chunkSize) + (x * stepOffset) +1000, 0, (chunkCoord.y * chunkSize) + (z * stepOffset) + 1000);
                vertices[vertexIndex].y = perlinNoiseHeightProvider.GetHeilghtForTerrain(worldPos, heightMultiplier);
                vertexIndex++;
            }
        }

        int triangleIndex = 0;
        int v = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                v = z * (resolution + 1) + x;
                // Setting first triangle 
                triangle[triangleIndex++] = v;
                triangle[triangleIndex++] = v + resolution + 1;
                triangle[triangleIndex++] = v + 1;

                // Setting second triangle
                triangle[triangleIndex++] = v + 1;
                triangle[triangleIndex++] = v + resolution + 1;
                triangle[triangleIndex++] = v + resolution + 2;
            }
        }

        var terrainMesh = terrainMeshFilter.sharedMesh;

        terrainMesh.SetVertices(vertices);
        terrainMesh.triangles = triangle;

        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();
    }

    private void OnDrawGizmos()
    {
        
    }
}
