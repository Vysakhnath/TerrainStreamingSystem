using UnityEngine;

// Optional diagnostics; release players contain no overlay Update/OnGUI work.
[DefaultExecutionOrder(1000)]
public sealed class StreamingDebugOverlay : MonoBehaviour
{
    [SerializeField] private ChunkController controller;
    [SerializeField] private ChunksPoolManager pool;
    [SerializeField] private bool showOverlay = true;
    [SerializeField] private bool showBoundaries = true;
    [SerializeField] private float boundaryHeight = 25f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private readonly System.Text.StringBuilder text = new System.Text.StringBuilder(512);
    private readonly GUIContent content = new GUIContent();
    private GUIStyle labelStyle;
    private float nextRefresh;
    private double intervalPeak;
    private int intervalGeneratedPeak;
    private Vector2Int displayedTile;
    private int displayedPending;
    private bool displayedPaused;
    public string DisplayText => content.text;

    private void Awake()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-terrainBenchmark") >= 0)
            enabled = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3))
        {
            showOverlay = !showOverlay;
            nextRefresh = 0;
        }
    }

    private void LateUpdate()
    {
        if (!showOverlay || controller == null || pool == null) return;
        intervalPeak = System.Math.Max(intervalPeak, controller.StreamingMillisecondsThisFrame);
        int generated = controller.enabled ? controller.GeneratedChunksThisFrame : 0;
        intervalGeneratedPeak = System.Math.Max(intervalGeneratedPeak, generated);
        Vector2Int tile = controller.CurrentPlayerChunk;
        bool stateChanged = tile != displayedTile || (controller.PendingChunkCount == 0) != (displayedPending == 0) || controller.GenerationPaused != displayedPaused;
        if (!stateChanged && Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        displayedTile = tile; displayedPending = controller.PendingChunkCount; displayedPaused = controller.GenerationPaused;
        text.Clear();
        text.Append("STREAMING DEBUG  [F3 hide]\nDrone tile: ").Append(tile.x).Append(", ").Append(tile.y);
        text.Append("\nActive: ").Append(controller.ActiveChunkCount).Append("   Queued: ").Append(controller.PendingChunkCount);
        text.Append("\nPooled: ").Append(pool.PooledChunkCount).Append("   Owned: ").Append(pool.OwnedChunkCount);
        text.Append("\nLast sampled frame — generated: ").Append(generated);
        text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "   CPU: {0:F2} ms", controller.StreamingMillisecondsThisFrame);
        text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "\nInterval peak — generated: {0}   CPU: {1:F2} ms", intervalGeneratedPeak, intervalPeak);
        text.Append("\nStatus: ").Append(!controller.enabled ? "disabled" : controller.GenerationPaused ? "ERROR / paused" : controller.PendingChunkCount > 0 ? "filling coverage" : "coverage complete");
        text.Append("\nStreaming errors: ").Append(controller.StreamingErrorCount);
        if (!string.IsNullOrEmpty(controller.LastStreamingError))
        {
            string error = controller.LastStreamingError;
            text.Append("\nLast error: ").Append(error, 0, System.Math.Min(error.Length, 200));
        }
        content.text = text.ToString();
        intervalPeak = 0;
        intervalGeneratedPeak = 0;
    }

    private void OnGUI()
    {
        if (!showOverlay || controller == null || pool == null) return;
        if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        float height = string.IsNullOrEmpty(controller.LastStreamingError) ? 225 : 300;
        GUI.Box(new Rect(12, 12, 480, height), GUIContent.none);
        GUI.Label(new Rect(24, 22, 456, height - 20), content, labelStyle);
    }
#endif

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showBoundaries || controller == null) return;
        Vector2Int tile = controller.CurrentPlayerChunk;
        Vector3 center = new Vector3((tile.x + 0.5f) * controller.ChunkSize, boundaryHeight, (tile.y + 0.5f) * controller.ChunkSize);
        Color previous = Gizmos.color;
        DrawBoundary(center, controller.ChunkSize, Color.white);
        DrawBoundary(center, (2 * controller.RequestedRadius + 1) * controller.ChunkSize, Color.cyan);
        DrawBoundary(center, (2 * controller.RetentionRadius + 1) * controller.ChunkSize, new Color(1f, 0.65f, 0.1f));
        Gizmos.color = previous;
    }
    private static void DrawBoundary(Vector3 center, float side, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, new Vector3(side, 0.05f, side));
    }
#endif
}
