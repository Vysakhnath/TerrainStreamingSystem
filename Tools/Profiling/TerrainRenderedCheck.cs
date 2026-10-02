#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

// Included only in the isolated validation player. Uses the real controller Update.
[DefaultExecutionOrder(-1000)]
public sealed class TerrainRenderedCheck : MonoBehaviour
{
    ChunkController controller;
    ChunksPoolManager pool;
    StreamingDebugOverlay overlay;
    Transform drone;
    string output;
    bool running, normalShot, fastShot, teleportShot, completed;
    bool gpuNanoseconds;
    double started;
    float normalSpeed;
    Vector3 originalCameraPosition;
    ProfilerRecorder draws, batches, triangles, gpu;
    readonly List<Sample> samples = new List<Sample>(6000);
    [Serializable] struct Sample
    {
        public int phase, active, pending, pooled, generated;
        public double seconds, frameMs, streamingMs, gpuMs;
        public long drawCalls, batchCount, triangleCount;
    }
    [Serializable] sealed class Result
    {
        public string unity, graphicsDevice, graphicsAPI, processor, timestampUtc, methodology;
        public bool drawCounterAvailable, batchCounterAvailable, triangleCounterAvailable, gpuCounterAvailable;
        public int finalActive, finalPending, finalOwned, streamingErrors;
        public int screenWidth, screenHeight;
        public float normalSpeed, maxSharedEdgeNormalAngleDegrees;
        public Vector3 originalCameraPosition;
        public Sample[] samples;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-terrainRenderedCheck") < 0) return;
        var check = new GameObject("Rendered streaming check").AddComponent<TerrainRenderedCheck>();
        int index = Array.IndexOf(args, "-renderedOutput");
        check.output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "RenderedCheck");
        Directory.CreateDirectory(check.output);
        Application.logMessageReceived += check.OnLog;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
        Profiler.logFile = Path.Combine(check.output, "rendered-capture.raw");
        Profiler.enableBinaryLog = true; Profiler.enableAllocationCallstacks = false;
        Profiler.SetAreaEnabled(ProfilerArea.CPU, true); Profiler.SetAreaEnabled(ProfilerArea.Rendering, true);
        Profiler.SetAreaEnabled(ProfilerArea.GPU, true); Profiler.enabled = true;
        check.controller = FindFirstObjectByType<ChunkController>();
        check.pool = FindFirstObjectByType<ChunksPoolManager>();
        check.overlay = FindFirstObjectByType<StreamingDebugOverlay>();
        var movement = FindFirstObjectByType<DroneMovementController>();
        check.normalSpeed = (float)typeof(DroneMovementController).GetField("speed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(movement);
        movement.enabled = false; check.drone = movement.transform;
    }
    IEnumerator Start()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("Rendered check requires a graphics device.");
        yield return new WaitForSecondsRealtime(1);
        while (controller.PendingChunkCount > 0)
        {
            if (controller.GenerationPaused) throw new InvalidOperationException("Initial coverage failed.");
            yield return null;
        }
        originalCameraPosition = Camera.main.transform.position;
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "original-camera.png"));
        yield return null; yield return null;
        // Inspect flight above the height field without changing the saved scene camera.
        drone.position = new Vector3(0, 35, 0);
        Camera.main.transform.localPosition = new Vector3(0, 20, -30);
        Camera.main.transform.localRotation = Quaternion.Euler(45, 0, 0);
        draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
        triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
        gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
        gpuNanoseconds = gpu.Valid && gpu.UnitType.ToString() == "TimeNanoseconds";
        started = Time.realtimeSinceStartupAsDouble;
        running = true;
        yield return new WaitForSecondsRealtime(18);
        running = false;
        bool rendered = false;
        foreach (var sample in samples) if (sample.drawCalls > 0 && sample.triangleCount > 0) rendered = true;
        if (!rendered) throw new InvalidOperationException("No rendered draw/triangle samples. Keep the player window visible; hidden/occluded windows can suspend rendering.");
        if (controller.PendingChunkCount != 0 || controller.StreamingErrorCount != 0 || !overlay.enabled || string.IsNullOrEmpty(overlay.DisplayText))
            throw new InvalidOperationException("Rendered check did not finish coverage with healthy diagnostics.");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "complete-coverage.png"));
        yield return null; yield return null;
        bool gpuAvailable = false;
        foreach (var sample in samples) if (sample.gpuMs > 0) gpuAvailable = true;
        var result = new Result {
            unity = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName,
            graphicsAPI = SystemInfo.graphicsDeviceType.ToString(), processor = SystemInfo.processorType,
            timestampUtc = DateTime.UtcNow.ToString("O"),
            methodology = "Windowed Windows development player, 60 Hz target, VSync off, overlay visible, CPU/Rendering/GPU profiler enabled; no Deep Profiling. Real controller Update with default 4 chunks/2 ms budget. Normal saved Drone speed for 5 s, fast 300 units/s for 5 s, then teleport and hold for 8 s. Camera raised for flight inspection after original-camera screenshot with initial coverage complete. Frame intervals include pacing, rendering, overlay, profiling and screenshot overhead. Recorder values describe the previous completed frame. GPU values are unavailable unless positive nanosecond counter samples exist. Single-machine smoke check, not release performance.",
            drawCounterAvailable = draws.Valid, batchCounterAvailable = batches.Valid, triangleCounterAvailable = triangles.Valid,
            gpuCounterAvailable = gpuAvailable, finalActive = controller.ActiveChunkCount,
            finalPending = controller.PendingChunkCount, finalOwned = pool.OwnedChunkCount,
            streamingErrors = controller.StreamingErrorCount, samples = samples.ToArray(),
            screenWidth = Screen.width, screenHeight = Screen.height, normalSpeed = normalSpeed,
            originalCameraPosition = originalCameraPosition, maxSharedEdgeNormalAngleDegrees = SharedEdgeNormalAngle()
        };
        File.WriteAllText(Path.Combine(output, "rendered-check.json"), JsonUtility.ToJson(result, true));
        File.WriteAllText(Path.Combine(output, "completed.txt"), "Rendered route completed; overlay populated; final coverage complete; no streaming errors.");
        completed = true; Application.Quit(0);
    }
    void Update()
    {
        if (!running) return;
        double elapsed = Time.realtimeSinceStartupAsDouble - started;
        if (elapsed < 5) drone.position = new Vector3((float)elapsed * normalSpeed, 35, 0);
        else if (elapsed < 10) drone.position = new Vector3(normalSpeed * 5 + (float)(elapsed - 5) * 300, 35, 0);
        else drone.position = new Vector3(-5000, 35, 5000);
    }
    void LateUpdate()
    {
        if (!running) return;
        double elapsed = Time.realtimeSinceStartupAsDouble - started;
        int phase = elapsed < 5 ? 0 : elapsed < 10 ? 1 : 2;
        if (samples.Count >= 6000) throw new InvalidOperationException("Rendered sample capacity exceeded.");
        samples.Add(new Sample {
            phase = phase, seconds = elapsed, frameMs = Time.unscaledDeltaTime * 1000,
            streamingMs = controller.StreamingMillisecondsThisFrame, active = controller.ActiveChunkCount,
            pending = controller.PendingChunkCount, pooled = pool.PooledChunkCount, generated = controller.GeneratedChunksThisFrame,
            drawCalls = draws.Valid ? draws.LastValue : -1, batchCount = batches.Valid ? batches.LastValue : -1,
            triangleCount = triangles.Valid ? triangles.LastValue : -1,
            gpuMs = gpuNanoseconds && gpu.LastValue > 0 ? gpu.LastValue * 0.000001 : -1
        });
        if (!normalShot && elapsed >= 4) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "normal-flight.png")); normalShot = true; }
        if (!fastShot && elapsed >= 9) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "fast-flight.png")); fastShot = true; }
        if (!teleportShot && phase == 2) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "teleport-filling.png")); teleportShot = true; }
    }
    void OnLog(string message, string trace, LogType type)
    {
        if (completed || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        File.WriteAllText(Path.Combine(output, "failure.txt"), message + "\n" + trace); Application.Quit(1);
    }
    float SharedEdgeNormalAngle()
    {
        var active = (Dictionary<Vector2Int, GameObject>)typeof(ChunkController).GetField("activeChunkDict", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
        var left = active[controller.CurrentPlayerChunk].GetComponent<MeshFilter>().sharedMesh.normals;
        var right = active[controller.CurrentPlayerChunk + Vector2Int.right].GetComponent<MeshFilter>().sharedMesh.normals;
        int side = Mathf.RoundToInt(Mathf.Sqrt(left.Length));
        float maximum = 0;
        for (int z = 0; z < side; z++) maximum = Mathf.Max(maximum, Vector3.Angle(left[z * side + side - 1], right[z * side]));
        return maximum;
    }
    void OnDestroy()
    {
        draws.Dispose(); batches.Dispose(); triangles.Dispose(); gpu.Dispose();
        Application.logMessageReceived -= OnLog;
    }
}
#endif
