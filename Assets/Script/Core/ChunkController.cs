using System.Collections.Generic;
using UnityEngine;

public class ChunkController : MonoBehaviour
{
    [SerializeField]
    private Transform playerPosition;

    [SerializeField]
    private ChunksPoolManager poolManager;
    [SerializeField]
    private TerrainGenerator terrainGenerator;

    private float chunkSize = 20;
    private int chunkBufferCount = 0;
    private int chunkDisableOffset = 4;

    private Vector2Int currentPlayerChunk;

    private bool isUpdating;

    private Dictionary<Vector2Int, GameObject> activeChunkDict = new Dictionary<Vector2Int, GameObject>();

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

        if (currentPlayerChunk != currentPositionChunk) 
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
        isUpdating = true;
        try
        {
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
            RemoveFarChunks();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally
        {
            isUpdating = false;
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
        List<Vector2Int> itemsToRemove = new List<Vector2Int>();
        foreach (Vector2Int chunk in activeChunkDict.Keys)
        {
            if (Vector2Int.Distance(chunk, currentPlayerChunk) >= chunkDisableOffset)
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
