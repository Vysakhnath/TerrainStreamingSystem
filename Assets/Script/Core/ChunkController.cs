using System.Collections.Generic;
using System.Linq;
using UnityEditor.EditorTools;
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
    private int halfChunk;

    private Vector2Int currentPlayerChunk;

    private bool isUpdating;

    private Dictionary<Vector2Int, GameObject> activeChunkDict = new Dictionary<Vector2Int, GameObject>();

    private void Start()
    {
        halfChunk =(int) chunkSize / 2;
        currentPlayerChunk = GetChunkFromCoord(playerPosition.position);
        UpdateChunk();
    }

    private void Update()
    {
        Vector2Int currentPositionChunk = GetChunkFromCoord(playerPosition.position);

        if (currentPlayerChunk != currentPositionChunk) 
        {
            currentPlayerChunk = currentPositionChunk;
            UpdateChunk();
        }
    }
    private Vector2Int GetChunkFromCoord(Vector3 position)
    {
        /*var absX = Mathf.Abs(position.x) - halfChunk;
        int posX = (int)(absX/chunkSize);
        if (absX > 0)
            posX += 1;
        posX *= (int)Mathf.Sign(position.x);

        var absY = Mathf.Abs(position.z) - halfChunk;
        int posY = (int)((absY / chunkSize));
        if (absY > 0)
            posY += 1;
        posY *=(int) Mathf.Sign(position.z);*/

        int posX = Mathf.FloorToInt(position.x / chunkSize);
        int posY = Mathf.FloorToInt(position.z / chunkSize);
        Vector2Int chunk = new Vector2Int(posX, posY);
        return chunk;
    }

    private void UpdateChunk()
    {
        //TODO: optimise the code
        if (isUpdating) return;
        isUpdating = true;
        if (activeChunkDict.Count == 0)
        {
            List<Vector2Int> activeChunkList = GenerateChunkList();

            foreach(Vector2Int chunk in activeChunkList)
            {
                var poolObject = poolManager.GetChunkFromPool();

                SetTerrainProperty(poolObject, chunk);
            }
        }
        else
        {
            List<Vector2Int> activeChunkList = GenerateChunkList();

            foreach (Vector2Int chunk in activeChunkList)
            {
                if (!activeChunkDict.Keys.Contains(chunk))
                {
                    var poolObject = poolManager.GetChunkFromPool();

                    SetTerrainProperty(poolObject, chunk);
                }
            }
            RemoveFarChunks();
        }
        isUpdating = false;
    }

    private void SetTerrainProperty((Vector2Int, GameObject) poolObject, Vector2Int chunk)
    {
        poolObject.Item1 = chunk;
        poolObject.Item2.name = "GameObject at "+ chunk.ToString();
        poolObject.Item2.transform.SetParent(transform);
        //poolObject.Item2.transform.localScale = new Vector3(2, 2, 2);
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
                poolManager.SetPool((chunk, activeChunkDict[chunk]));
                itemsToRemove.Add(chunk);
            }
        }

        foreach(Vector2Int chunk in itemsToRemove)
        {
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
