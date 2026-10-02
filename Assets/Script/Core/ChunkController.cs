using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class ChunkController : MonoBehaviour
{
    private static readonly ProfilerMarker StreamingMarker = new ProfilerMarker("Terrain.StreamingUpdate");
    private static readonly ProfilerMarker SelectionMarker = new ProfilerMarker("Terrain.SelectChunks");
    private static readonly ProfilerMarker ReleaseMarker = new ProfilerMarker("Terrain.ReleaseChunks");

    public int ActiveChunkCount => activeChunkDict.Count;
    [SerializeField]
    private Transform playerPosition;

    [SerializeField]
    private ChunksPoolManager poolManager;
    [SerializeField]
    private TerrainGenerator terrainGenerator;

    private float chunkSize = 20;

    [SerializeField, Min(0)]
    [Tooltip("Requested square radius in tiles. 2 requests a 5 by 5 square.")]
    private int chunkBufferCount = 2;

    [SerializeField, Min(0)]
    [Tooltip("Extra rings in which previously generated tiles remain active.")]
    private int chunkRetentionMargin = 1;

    private int lastChunkBufferCount = -1;
    private int lastChunkRetentionMargin = -1;

    private Vector2Int currentPlayerChunk;

    private bool isUpdating;

    private Dictionary<Vector2Int, GameObject> activeChunkDict = new Dictionary<Vector2Int, GameObject>();

    private void OnValidate()
    {
        chunkBufferCount = Mathf.Max(0, chunkBufferCount);
        chunkRetentionMargin = Mathf.Max(0, chunkRetentionMargin);
    }

    private void Start()
    {
        if (playerPosition == null || poolManager == null || terrainGenerator == null)
        {
            Debug.LogError("Assign the tracked transform, chunk pool, and terrain generator before starting terrain streaming.", this);
            enabled = false;
            return;
        }
        if (!poolManager.ValidateConfiguration() || !terrainGenerator.Initialize())
        {
            enabled = false;
            return;
        }
        currentPlayerChunk = GetChunkFromCoord(playerPosition.position);
        UpdateChunk();
    }

    private void Update()
    {
        if (playerPosition == null || poolManager == null || terrainGenerator == null)
        {
            Debug.LogError("A terrain streaming dependency was destroyed. Streaming has been disabled.", this);
            enabled = false;
            return;
        }
        Vector2Int currentPositionChunk = GetChunkFromCoord(playerPosition.position);

        if (currentPlayerChunk != currentPositionChunk ||
            lastChunkBufferCount != chunkBufferCount ||
            lastChunkRetentionMargin != chunkRetentionMargin)
        {
            currentPlayerChunk = currentPositionChunk;
            UpdateChunk();
        }
    }
    private Vector2Int GetChunkFromCoord(Vector3 position)
    {
        int posX = Mathf.FloorToInt(position.x / chunkSize);
        int posY = Mathf.FloorToInt(position.z / chunkSize);
        Vector2Int chunk = new Vector2Int(posX, posY);
        return chunk;
    }

    private void UpdateChunk()
    {
        if (isUpdating) return;
        using var streamingScope = StreamingMarker.Auto();
        isUpdating = true;
        try
        {
            RemoveFarChunks();
            List<Vector2Int> activeChunkList = GenerateChunkList();
            foreach (Vector2Int chunk in activeChunkList)
            {
                if (!activeChunkDict.ContainsKey(chunk))
                {
                    var poolObject = poolManager.GetChunkFromPool();
                    try
                    {
                        SetTerrainProperty(poolObject, chunk);
                    }
                    catch
                    {
                        poolManager.SetPool(poolObject);
                        throw;
                    }
                }
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally
        {
            isUpdating = false;
            lastChunkBufferCount = chunkBufferCount;
            lastChunkRetentionMargin = chunkRetentionMargin;
        }
    }

    private void SetTerrainProperty((Vector2Int, GameObject) poolObject, Vector2Int chunk)
    {
        poolObject.Item1 = chunk;
        poolObject.Item2.name = "GameObject at "+ chunk.ToString();
        poolObject.Item2.transform.SetParent(transform);

        terrainGenerator.GenerateMeshData(chunk, chunkSize, poolObject.Item2);
        poolObject.Item2.transform.position = new Vector3(chunk.x * chunkSize, 0, chunk.y * chunkSize);
        poolObject.Item2.SetActive(true);
        activeChunkDict.Add(poolObject.Item1, poolObject.Item2);
    }

    private void RemoveFarChunks()
    {
        using var releaseScope = ReleaseMarker.Auto();
        List<Vector2Int> itemsToRemove = new List<Vector2Int>();
        int retentionRadius = chunkBufferCount + chunkRetentionMargin;
        foreach (Vector2Int chunk in activeChunkDict.Keys)
        {
            Vector2Int offset = chunk - currentPlayerChunk;
            int squareDistance = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));
            if (squareDistance > retentionRadius)
            {
                itemsToRemove.Add(chunk);
            }
        }

        foreach(Vector2Int chunk in itemsToRemove)
        {
            poolManager.SetPool((chunk, activeChunkDict[chunk]));
            activeChunkDict.Remove(chunk);
        }
    }

    private List<Vector2Int> GenerateChunkList()
    {
        using var selectionScope = SelectionMarker.Auto();
        List<Vector2Int> chunkList = new List<Vector2Int>();

        Vector2Int chunk;
        for (int x = currentPlayerChunk.x - chunkBufferCount; x <= currentPlayerChunk.x + chunkBufferCount; x++)
        {
            for (int y = currentPlayerChunk.y - chunkBufferCount; y <= currentPlayerChunk.y + chunkBufferCount; y++)
            {
                chunk = new Vector2Int(x, y);
                chunkList.Add(chunk);
            }
        }

        return chunkList;
    }
}
