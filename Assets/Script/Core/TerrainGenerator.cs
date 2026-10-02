using Unity.Profiling;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    private static readonly ProfilerMarker GenerationMarker = new ProfilerMarker("Terrain.GenerateChunk");
    private static readonly ProfilerMarker DataMarker = new ProfilerMarker("Terrain.BuildMeshData");
    private static readonly ProfilerMarker ApplyMarker = new ProfilerMarker("Terrain.ApplyMesh");
    private static readonly ProfilerMarker NormalsMarker = new ProfilerMarker("Terrain.RecalculateNormals");
    private static readonly ProfilerMarker BoundsMarker = new ProfilerMarker("Terrain.RecalculateBounds");
    private float heightMultiplier = 20f;

    private int resolution = 10;

    // Generation is synchronous: Unity copies these buffers into each owned mesh.
    // Concurrent generation would require separate buffers per in-flight request.
    private Vector3[] vertices;
    private int[] triangle;
    private int bufferedResolution = -1;

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
        using var generationScope = GenerationMarker.Auto();
        if (perlinNoiseHeightProvider == null)
            throw new System.InvalidOperationException("Initialize the terrain generator before generating chunks.");
        if (resolution <= 0 || chunkSize <= 0 || float.IsNaN(chunkSize) || float.IsInfinity(chunkSize))
            throw new System.ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size and mesh resolution must be positive and finite.");
        if (terrain == null || !terrain.TryGetComponent<MeshFilter>(out var terrainMeshFilter) || terrainMeshFilter.sharedMesh == null)
            throw new System.ArgumentException("Terrain requires a MeshFilter with an owned mesh.", nameof(terrain));

        using (DataMarker.Auto())
        {
            EnsureMeshBuffers();

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

        }

        var terrainMesh = terrainMeshFilter.sharedMesh;

        using (ApplyMarker.Auto())
        {
            if (terrainMesh.vertexCount != 0 && terrainMesh.vertexCount != vertices.Length)
                terrainMesh.Clear();
            terrainMesh.SetVertices(vertices);
            terrainMesh.triangles = triangle;

            using (NormalsMarker.Auto()) terrainMesh.RecalculateNormals();
            using (BoundsMarker.Auto()) terrainMesh.RecalculateBounds();
        }
    }

    private void EnsureMeshBuffers()
    {
        if (bufferedResolution == resolution) return;

        var nextVertices = new Vector3[(resolution + 1) * (resolution + 1)];
        var nextTriangles = new int[resolution * resolution * 6];
        int triangleIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int v = z * (resolution + 1) + x;
                nextTriangles[triangleIndex++] = v;
                nextTriangles[triangleIndex++] = v + resolution + 1;
                nextTriangles[triangleIndex++] = v + 1;
                nextTriangles[triangleIndex++] = v + 1;
                nextTriangles[triangleIndex++] = v + resolution + 1;
                nextTriangles[triangleIndex++] = v + resolution + 2;
            }
        }
        vertices = nextVertices;
        triangle = nextTriangles;
        bufferedResolution = resolution;
    }

    private void OnDrawGizmos()
    {

    }
}
