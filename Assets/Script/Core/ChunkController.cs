using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Profiling;
using UnityEngine;

public class ChunkController : MonoBehaviour
{
    private static readonly ProfilerMarker StreamingMarker = new ProfilerMarker("Terrain.StreamingUpdate");
    private static readonly ProfilerMarker SelectionMarker = new ProfilerMarker("Terrain.SelectChunks");
    private static readonly ProfilerMarker ReleaseMarker = new ProfilerMarker("Terrain.ReleaseChunks");
    private static readonly ProfilerMarker GenerationQueueMarker = new ProfilerMarker("Terrain.ProcessQueue");

    public int ActiveChunkCount => activeChunkDict.Count;
    public int PendingChunkCount => requestedChunks.Count - nextRequest;
    public int GeneratedChunksThisFrame { get; private set; }
    public bool GenerationPaused => generationPaused;
    public Vector2Int CurrentPlayerChunk => initialized || playerPosition == null ? currentPlayerChunk : GetChunkFromCoord(playerPosition.position);
    public float ChunkSize => chunkSize;
    public int RequestedRadius => chunkBufferCount;
    public int RetentionRadius => chunkBufferCount + chunkRetentionMargin;
    public int StreamingErrorCount { get; private set; }
    public string LastStreamingError { get; private set; }
    public double StreamingMillisecondsThisFrame => streamingFrame == Time.frameCount ? streamingMilliseconds : 0;
    private int streamingFrame = -1;
    private double streamingMilliseconds;
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

    [SerializeField, Min(1)]
    private int maxChunksPerFrame = 4;
    [SerializeField, Min(0)]
    [Tooltip("Soft budget for streaming work. 0 disables the time limit. One chunk cannot be interrupted.")]
    private float generationBudgetMilliseconds = 2f;

    private int lastChunkBufferCount = -1;
    private int lastChunkRetentionMargin = -1;

    private Vector2Int currentPlayerChunk;

    private bool isUpdating;
    private bool initialized, generationPaused;
    private int nextRequest;
    private int lastGenerationFrame = -1;
    private System.Comparison<Vector2Int> requestComparison;

    private Dictionary<Vector2Int, GameObject> activeChunkDict = new Dictionary<Vector2Int, GameObject>();
    private readonly List<Vector2Int> requestedChunks = new List<Vector2Int>();
    private readonly List<Vector2Int> itemsToRemove = new List<Vector2Int>();

    private void OnValidate()
    {
        chunkBufferCount = Mathf.Max(0, chunkBufferCount);
        chunkRetentionMargin = Mathf.Max(0, chunkRetentionMargin);
        maxChunksPerFrame = Mathf.Max(1, maxChunksPerFrame);
        generationBudgetMilliseconds = float.IsNaN(generationBudgetMilliseconds) || float.IsInfinity(generationBudgetMilliseconds)
            ? 2f : Mathf.Max(0, generationBudgetMilliseconds);
    }

    private void Start()
    {
        if (playerPosition == null || poolManager == null || terrainGenerator == null)
        {
            RecordError("Assign the tracked transform, chunk pool, and terrain generator before starting terrain streaming.");
            Debug.LogError("Assign the tracked transform, chunk pool, and terrain generator before starting terrain streaming.", this);
            enabled = false;
            return;
        }
        if (!poolManager.ValidateConfiguration() || !terrainGenerator.Initialize())
        {
            RecordError("Terrain dependencies failed validation. See the Console for details.");
            enabled = false;
            return;
        }
        currentPlayerChunk = GetChunkFromCoord(playerPosition.position);
        requestComparison = CompareRequests;
        initialized = true;
        UpdateChunk(true);
    }

    private void Update()
    {
        if (!initialized) return;
        if (playerPosition == null || poolManager == null || terrainGenerator == null)
        {
            RecordError("A terrain streaming dependency was destroyed. Streaming has been disabled.");
            Debug.LogError("A terrain streaming dependency was destroyed. Streaming has been disabled.", this);
            enabled = false;
            return;
        }
        Vector2Int currentPositionChunk = GetChunkFromCoord(playerPosition.position);

        bool refresh = currentPlayerChunk != currentPositionChunk ||
            lastChunkBufferCount != chunkBufferCount ||
            lastChunkRetentionMargin != chunkRetentionMargin;
        currentPlayerChunk = currentPositionChunk;
        if (lastGenerationFrame != Time.frameCount) GeneratedChunksThisFrame = 0;
        if (refresh || (PendingChunkCount > 0 && !generationPaused)) UpdateChunk(refresh);
    }
    private Vector2Int GetChunkFromCoord(Vector3 position)
    {
        int posX = Mathf.FloorToInt(position.x / chunkSize);
        int posY = Mathf.FloorToInt(position.z / chunkSize);
        Vector2Int chunk = new Vector2Int(posX, posY);
        return chunk;
    }

    private void UpdateChunk(bool refresh)
    {
        if (isUpdating) return;
        using var streamingScope = StreamingMarker.Auto();
        isUpdating = true;
        long started = Stopwatch.GetTimestamp();
        try
        {
            if (refresh)
            {
                RemoveFarChunks();
                GenerateChunkList();
                generationPaused = false;
            }
            if (generationPaused || lastGenerationFrame == Time.frameCount) return;
            lastGenerationFrame = Time.frameCount;
            GeneratedChunksThisFrame = 0;
            using var queueScope = GenerationQueueMarker.Auto();
            while (PendingChunkCount > 0 && GeneratedChunksThisFrame < Mathf.Max(1, maxChunksPerFrame))
            {
                Vector2Int chunk = requestedChunks[nextRequest];
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
                nextRequest++;
                GeneratedChunksThisFrame++;
                double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                if (generationBudgetMilliseconds > 0 && elapsedMs >= generationBudgetMilliseconds) break;
            }
        }
        catch (System.Exception exception)
        {
            // Keep the failed request queued; retry only after a neighborhood/configuration change.
            generationPaused = true;
            RecordError(exception.Message);
            Debug.LogException(exception, this);
        }
        finally
        {
            if (streamingFrame != Time.frameCount) streamingMilliseconds = 0;
            streamingFrame = Time.frameCount;
            streamingMilliseconds += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
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
        itemsToRemove.Clear();
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

    private void GenerateChunkList()
    {
        using var selectionScope = SelectionMarker.Auto();
        requestedChunks.Clear();
        nextRequest = 0;

        Vector2Int chunk;
        for (int x = currentPlayerChunk.x - chunkBufferCount; x <= currentPlayerChunk.x + chunkBufferCount; x++)
        {
            for (int y = currentPlayerChunk.y - chunkBufferCount; y <= currentPlayerChunk.y + chunkBufferCount; y++)
            {
                chunk = new Vector2Int(x, y);
                if (!activeChunkDict.ContainsKey(chunk)) requestedChunks.Add(chunk);
            }
        }

        requestedChunks.Sort(requestComparison);
    }

    private int CompareRequests(Vector2Int a, Vector2Int b)
    {
        Vector2Int da = a - currentPlayerChunk, db = b - currentPlayerChunk;
        long distanceA = (long)da.x * da.x + (long)da.y * da.y;
        long distanceB = (long)db.x * db.x + (long)db.y * db.y;
        int comparison = distanceA.CompareTo(distanceB);
        if (comparison != 0) return comparison;
        comparison = a.x.CompareTo(b.x);
        return comparison != 0 ? comparison : a.y.CompareTo(b.y);
    }

    private void RecordError(string message)
    {
        StreamingErrorCount++;
        LastStreamingError = message;
    }
}
