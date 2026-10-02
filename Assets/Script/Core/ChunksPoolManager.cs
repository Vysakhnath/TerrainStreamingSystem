using System.Collections.Generic;
using UnityEngine;

public class ChunksPoolManager : MonoBehaviour
{
    [SerializeField]
    private Material terrainMat;

    private readonly Stack<GameObject> poolOfChunks = new Stack<GameObject>();
    private readonly HashSet<GameObject> pooledChunks = new HashSet<GameObject>();
    // Keep meshes independently so cleanup works even if Unity destroys objects first.
    private readonly Dictionary<GameObject, Mesh> ownedChunks = new Dictionary<GameObject, Mesh>();

    public bool ValidateConfiguration()
    {
        if (terrainMat != null) return true;
        Debug.LogError("The chunk pool requires a terrain material.", this);
        return false;
    }

    public void SetPool((Vector2Int, GameObject) poolObject)
    {
        var terrain = poolObject.Item2;
        if (terrain == null || !ownedChunks.ContainsKey(terrain))
            throw new System.ArgumentException("Only live chunks owned by this pool can be returned.", nameof(poolObject));
        if (!pooledChunks.Add(terrain))
            throw new System.InvalidOperationException("The chunk has already been returned to the pool.");

        terrain.SetActive(false);
        terrain.transform.SetParent(transform);
        poolOfChunks.Push(terrain);
    }

    public (Vector2Int, GameObject) GetChunkFromPool()
    {
        while (poolOfChunks.Count > 0)
        {
            var terrain = poolOfChunks.Pop();
            pooledChunks.Remove(terrain);
            if (terrain != null) return (default, terrain);
            ReleaseChunk(terrain);
        }

        if (terrainMat == null)
            throw new System.InvalidOperationException("The chunk pool requires a terrain material.");

        var chunk = new GameObject("Terrain chunk");
        chunk.SetActive(false);
        chunk.transform.SetParent(transform);
        var mesh = new Mesh { name = "Terrain chunk mesh" };
        ownedChunks.Add(chunk, mesh);
        chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
        chunk.AddComponent<MeshRenderer>().sharedMaterial = terrainMat;
        return (default, chunk);
    }

    public void Reset()
    {
        // Reset only available objects; checked-out chunks remain owned and usable.
        while (poolOfChunks.Count > 0) ReleaseChunk(poolOfChunks.Pop());
        pooledChunks.Clear();
    }

    private void ReleaseChunk(GameObject chunk)
    {
        if (ownedChunks.TryGetValue(chunk, out var mesh))
        {
            if (mesh != null) Destroy(mesh);
            ownedChunks.Remove(chunk);
        }
        if (chunk != null) Destroy(chunk);
    }

    private void OnDestroy()
    {
        foreach (var item in ownedChunks)
        {
            if (item.Value != null) Destroy(item.Value);
            if (item.Key != null) Destroy(item.Key);
        }
        ownedChunks.Clear();
        poolOfChunks.Clear();
        pooledChunks.Clear();
    }
}
