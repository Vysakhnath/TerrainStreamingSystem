#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

// Copied into an isolated project's Assets only by the profiling runner.
public sealed class TerrainBenchmark : MonoBehaviour
{
    static readonly string[] MarkerNames = {
        "Terrain.StreamingUpdate", "Terrain.SelectChunks", "Terrain.ReleaseChunks", "Terrain.PoolAcquire",
        "Terrain.GenerateChunk", "Terrain.BuildMeshData", "Terrain.ApplyMesh",
        "Terrain.RecalculateNormals", "Terrain.RecalculateBounds", "Terrain.ProcessQueue"
    };
    static readonly FieldInfo BufferField = typeof(ChunkController).GetField("chunkBufferCount", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo MarginField = typeof(ChunkController).GetField("chunkRetentionMargin", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo CapField = typeof(ChunkController).GetField("maxChunksPerFrame", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo BudgetField = typeof(ChunkController).GetField("generationBudgetMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic);
    ChunkController controller;
    ChunksPoolManager pool;
    Transform drone;
    Action initialize, updateStreaming;
    string outputDirectory;
    float soakSeconds;
    bool complete;
    ProfilerRecorder[] activeRecorders;
    ProfilerRecorder gcAllocatedRecorder;
    readonly Report report = new Report();
    readonly List<Scenario> scenarios = new List<Scenario>();

    [Serializable] public sealed class Report
    {
        public string timestampUtc, unityVersion, operatingSystem, processor, graphicsDevice, sourceRevision, mode, methodology;
        public int processorCount, systemMemoryMB, targetFrameRate;
        public bool profilerEnabled;
        public Scenario[] scenarios;
    }
    [Serializable] public sealed class Distribution
    {
        public int samples;
        public double mean, p50, p95, p99, max;
    }
    [Serializable] public sealed class MarkerMetric
    {
        public string name;
        public long calls;
        public double totalMilliseconds, meanMillisecondsPerCall;
        public Distribution millisecondsPerFrameWithCalls;
    }
    [Serializable] public sealed class Scenario
    {
        public string name;
        public int buffer, frames, activeMax, pooledMax, ownedStart, ownedEnd, ownedMax, gcCollections;
        public double elapsedSeconds;
        public int pendingMax, pendingEnd, maxChunksPerFrame, coverageFrames;
        public double budgetMilliseconds, coverageSeconds;
        public long gcAllocatedBytes, unityUsedStart, unityUsedEnd, unityReservedStart, unityReservedEnd, monoUsedStart, monoUsedEnd;
        public Distribution streamingMilliseconds, frameIntervalMilliseconds, gcAllocatedBytesPerFrame;
        public MarkerMetric[] markers;
    }
    struct FrameSample
    {
        public double streamingMs, frameMs;
        public long allocated;
        public int active, pooled, owned, pending, generated;
    }
    struct MemorySample
    {
        public double seconds;
        public long unityUsed, unityReserved, monoUsed;
        public int active, pooled, owned;
    }
    static string Argument(string key, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-terrainBenchmark") < 0) return;
        var runner = new GameObject("Terrain benchmark").AddComponent<TerrainBenchmark>();
        DontDestroyOnLoad(runner.gameObject);
        runner.outputDirectory = Argument("-benchmarkOutput", Path.Combine(Application.persistentDataPath, "TerrainBenchmark"));
        runner.soakSeconds = float.Parse(Argument("-benchmarkSoakSeconds", "180"), CultureInfo.InvariantCulture);
        if (runner.soakSeconds < 0 || runner.soakSeconds > 600) throw new ArgumentOutOfRangeException("benchmarkSoakSeconds", "Use 0 to 600 seconds.");
        Directory.CreateDirectory(runner.outputDirectory);
        Profiler.logFile = Path.Combine(runner.outputDirectory, "cpu-capture.raw");
        Profiler.enableBinaryLog = true;
        Profiler.enableAllocationCallstacks = false;
        Profiler.SetAreaEnabled(ProfilerArea.CPU, true);
        Profiler.SetAreaEnabled(ProfilerArea.Memory, true);
        Profiler.enabled = true;
        Application.logMessageReceived += runner.OnLog;
        SceneManager.sceneLoaded += runner.OnSceneLoaded;
        runner.ConfigureScene();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        RuntimeHelpers.RunClassConstructor(typeof(ChunkController).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TerrainGenerator).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ChunksPoolManager).TypeHandle);
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ConfigureScene();
    void ConfigureScene()
    {
        controller = UnityEngine.Object.FindFirstObjectByType<ChunkController>();
        pool = UnityEngine.Object.FindFirstObjectByType<ChunksPoolManager>();
        drone = UnityEngine.Object.FindFirstObjectByType<DroneMovementController>().transform;
        controller.enabled = false;
        CapField.SetValue(controller, 1000); BudgetField.SetValue(controller, 0f);
        drone.GetComponent<DroneMovementController>().enabled = false;
        initialize = (Action)typeof(ChunkController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Action), controller);
        updateStreaming = (Action)typeof(ChunkController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Action), controller);
    }
    IEnumerator Start()
    {
        report.timestampUtc = DateTime.UtcNow.ToString("O");
        report.unityVersion = Application.unityVersion;
        report.operatingSystem = SystemInfo.operatingSystem;
        report.processor = SystemInfo.processorType;
        report.processorCount = SystemInfo.processorCount;
        report.systemMemoryMB = SystemInfo.systemMemorySize;
        report.graphicsDevice = SystemInfo.graphicsDeviceName;
        report.sourceRevision = Argument("-benchmarkRevision", "unspecified");
        report.mode = SystemInfo.graphicsDeviceType.ToString();
        report.targetFrameRate = Application.targetFrameRate;
        report.profilerEnabled = Profiler.enabled;
        report.methodology = "Windows standalone development player; no Deep Profiling. Controller auto-Update disabled; cached delegates invoke its original Start/Update once per sampled frame. GC Allocated In Frame uses Unity ProfilerRecorder for completed sampled frames across all threads, including engine/harness allocations; measurement arrays are preallocated before a settling frame. Frame intervals include pacing/engine work. Profiler marker samples sum calls per frame; nested times are inclusive. CSV writes and summaries occur after measurement. Memory includes engine and preallocated harness/recorder buffers. Headless mode does not measure rendering/GPU costs.";
        // Warm managed/native call paths before comparing fresh-terrain startup workloads.
        BufferField.SetValue(controller, 0); MarginField.SetValue(controller, 0);
        initialize(); drone.position = new Vector3(20, 0, 0); updateStreaming();
        yield return null;
        SceneManager.LoadScene("MainScene"); yield return null; yield return null;
        int[] buffers = { 2, 3, 5 };
        for (int b = 0; b < buffers.Length; b++)
        {
            if (b > 0) { SceneManager.LoadScene("MainScene"); yield return null; yield return null; }
            if (controller.ActiveChunkCount != 0) throw new InvalidOperationException("Controller ran before benchmark setup.");
            BufferField.SetValue(controller, buffers[b]);
            MarginField.SetValue(controller, 1);
            drone.position = Vector3.zero;
            yield return Measure("startup", buffers[b], 1, 0);
            yield return Measure("continuous", buffers[b], 240, 0);
            yield return Measure("teleport", buffers[b], 24, 0);
            if (buffers[b] == 5 && soakSeconds > 0)
            {
                // Warm the pool and let transient scene/startup work settle before the soak.
                for (int i = 0; i < 120; i++) { drone.position = new Vector3(i * 20, 0, (i % 8) * 20); updateStreaming(); yield return null; }
                for (int i = 0; i < 120; i++) yield return null;
                yield return Measure("soak", buffers[b], 0, soakSeconds);
            }
            SceneManager.LoadScene("MainScene"); yield return null; yield return null;
            BufferField.SetValue(controller, buffers[b]); MarginField.SetValue(controller, 1);
            CapField.SetValue(controller, 4); BudgetField.SetValue(controller, 2f);
            drone.position = Vector3.zero;
            yield return Measure("budget-startup", buffers[b], 512, 0);
            yield return Measure("budget-teleport", buffers[b], 512, 0);
            yield return Measure("budget-rapid", buffers[b], 512, 0);
        }
        report.scenarios = scenarios.ToArray();
        File.WriteAllText(Path.Combine(outputDirectory, "summary.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(outputDirectory, "completed.txt"), "All scenarios completed without runtime errors.");
        complete = true;
        UnityEngine.Debug.Log("Terrain benchmark completed: " + outputDirectory);
        Application.Quit(0);
    }
    IEnumerator Measure(string name, int buffer, int frameLimit, float duration)
    {
        int capacity = duration > 0 ? Mathf.CeilToInt(duration * 240) + 1000 : frameLimit + 4;
        var samples = new FrameSample[capacity];
        var memories = new MemorySample[Mathf.CeilToInt(duration) + 4];
        var recorders = new ProfilerRecorder[MarkerNames.Length];
        activeRecorders = recorders;
        for (int i = 0; i < recorders.Length; i++)
        {
            recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerNames[i], capacity + 4,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            if (!recorders[i].Valid) throw new InvalidOperationException("Missing profiler marker: " + MarkerNames[i]);
        }
        var result = new Scenario { name = name, buffer = buffer, ownedStart = pool.OwnedChunkCount,
            maxChunksPerFrame = (int)CapField.GetValue(controller), budgetMilliseconds = (float)BudgetField.GetValue(controller) };
        bool budgeted = name.StartsWith("budget-");
        gcAllocatedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", capacity + 4);
        if (!gcAllocatedRecorder.Valid) throw new InvalidOperationException("Missing GC Allocated In Frame counter.");
        yield return null; // Exclude recorder/array setup from the sampled frames.
        int collectionsBefore = GC.CollectionCount(0);
        var initialMemory = Memory(0);
        result.unityUsedStart = initialMemory.unityUsed; result.unityReservedStart = initialMemory.unityReserved; result.monoUsedStart = initialMemory.monoUsed;
        memories[0] = initialMemory;
        int memoryCount = 1, count = 0;
        long start = Stopwatch.GetTimestamp(), previous = start;
        double elapsed = 0, nextMemory = 1;
        while (duration > 0 ? elapsed < duration : count < frameLimit)
        {
            if (count >= samples.Length) throw new InvalidOperationException("Frame sample capacity exceeded; verify frame pacing.");
            long frameStart = Stopwatch.GetTimestamp();
            elapsed = Seconds(frameStart - start);
            if (name == "continuous") drone.position = new Vector3((count + 1) * 20, 0, (count / 30 % 2) * 20);
            else if (name == "teleport") drone.position = new Vector3((count % 2 == 0 ? 1 : -1) * (50000 + count * 2000), 0, count * 3000);
            else if (name == "soak") drone.position = new Vector3((float)elapsed * 100, 0, Mathf.Sin((float)elapsed * 0.2f) * 100);
            else if (name == "budget-teleport" && count == 0) drone.position = new Vector3(500000, 0, -500000);
            else if (name == "budget-rapid" && count < 24) drone.position = new Vector3((count % 2 == 0 ? 1 : -1) * (50000 + count * 2000), 0, count * 3000);

            long cpuStart = Stopwatch.GetTimestamp();
            if (name == "startup" || (name == "budget-startup" && count == 0)) initialize(); else updateStreaming();
            long cpuEnd = Stopwatch.GetTimestamp();

            samples[count] = new FrameSample {
                streamingMs = Seconds(cpuEnd - cpuStart) * 1000,
                frameMs = count == 0 ? double.NaN : Seconds(frameStart - previous) * 1000,
                active = controller.ActiveChunkCount, pooled = pool.PooledChunkCount, owned = pool.OwnedChunkCount,
                pending = controller.PendingChunkCount, generated = controller.GeneratedChunksThisFrame
            };
            int retentionSide = (buffer + 1) * 2 + 1;
            if (samples[count].active > retentionSide * retentionSide || samples[count].owned != samples[count].active + samples[count].pooled)
                throw new InvalidOperationException("Chunk ownership/count invariant failed.");

            result.activeMax = Math.Max(result.activeMax, samples[count].active);
            result.pooledMax = Math.Max(result.pooledMax, samples[count].pooled);
            result.ownedMax = Math.Max(result.ownedMax, samples[count].owned);
            result.pendingMax = Math.Max(result.pendingMax, samples[count].pending);
            if (samples[count].pending > (buffer * 2 + 1) * (buffer * 2 + 1) || samples[count].generated > result.maxChunksPerFrame || controller.GenerationPaused)
                throw new InvalidOperationException("Queue/count budget invariant failed.");
            bool covered = budgeted && (name != "budget-rapid" || count >= 23) && controller.PendingChunkCount == 0;
            if (covered) { result.coverageFrames = count + 1; result.coverageSeconds = Seconds(cpuEnd - start); }
            if (duration > 0 && elapsed >= nextMemory)
            {
                memories[memoryCount++] = Memory(elapsed);
                nextMemory = Math.Floor(elapsed) + 1;
            }
            previous = frameStart;
            count++;
            yield return null;
            if (covered) break;
            elapsed = Seconds(Stopwatch.GetTimestamp() - start);
        }
        result.gcCollections = GC.CollectionCount(0) - collectionsBefore;
        result.frames = count;
        result.elapsedSeconds = Seconds(Stopwatch.GetTimestamp() - start);
        result.ownedEnd = pool.OwnedChunkCount;
        result.pendingEnd = controller.PendingChunkCount;
        if (budgeted && result.pendingEnd != 0) throw new InvalidOperationException("Coverage did not complete within the measurement window.");
        var endMemory = Memory(result.elapsedSeconds);
        result.unityUsedEnd = endMemory.unityUsed; result.unityReservedEnd = endMemory.unityReserved; result.monoUsedEnd = endMemory.monoUsed;
        memories[memoryCount++] = endMemory;
        gcAllocatedRecorder.Stop();
        var gcRecorded = gcAllocatedRecorder.ToArray();
        gcAllocatedRecorder.Dispose();
        // Stop/ToArray also exposes the unfinished current frame (Count == 0).
        // Only completed counter samples belong to the measurement window.
        var gcValues = new List<ProfilerRecorderSample>();
        foreach (var value in gcRecorded) if (value.Count > 0) gcValues.Add(value);
        if (gcValues.Count < count) throw new InvalidOperationException("Incomplete frame allocation recording.");
        int gcOffset = gcValues.Count - count;
        for (int i = 0; i < count; i++)
        {
            samples[i].allocated = gcValues[gcOffset + i].Value;
            result.gcAllocatedBytes += samples[i].allocated;
        }
        // A fresh generator allocates one buffer pair; later chunks reuse it.
        if ((name == "startup" || name == "budget-startup") && result.gcAllocatedBytes < 3852)
        {
            string values = "";
            foreach (var value in gcValues) values += value.Value + ":" + value.Count + ",";
            throw new InvalidOperationException("Allocation counter did not capture known mesh array allocations. Enabled=" + Profiler.enabled + "; samples=" + values);
        }
        result.markers = new MarkerMetric[recorders.Length];
        for (int i = 0; i < recorders.Length; i++)
        {
            recorders[i].Stop();
            var values = recorders[i].ToArray();
            recorders[i].Dispose();
            var times = new List<double>();
            var metric = new MarkerMetric { name = MarkerNames[i] };
            foreach (var value in values)
            {
                if (value.Count == 0) continue;
                metric.calls += value.Count;
                double ms = value.Value * 0.000001;
                metric.totalMilliseconds += ms;
                times.Add(ms);
            }
            metric.meanMillisecondsPerCall = metric.calls == 0 ? 0 : metric.totalMilliseconds / metric.calls;
            metric.millisecondsPerFrameWithCalls = Stats(times.ToArray());
            result.markers[i] = metric;
        }
        activeRecorders = null;
        long generated = result.markers[4].calls;
        int required = (buffer * 2 + 1) * (buffer * 2 + 1);
        if ((name == "startup" && generated != required) || (name == "teleport" && generated != required * frameLimit))
            throw new InvalidOperationException("Recorded generation count does not match scenario workload: " + generated);
        if ((name == "budget-startup" || name == "budget-teleport") && generated != required)
            throw new InvalidOperationException("Budgeted generation count does not match completed coverage.");
        var cpu = new double[count]; var frames = new List<double>(); var allocations = new double[count];
        for (int i = 0; i < count; i++) { cpu[i] = samples[i].streamingMs; allocations[i] = samples[i].allocated; if (!double.IsNaN(samples[i].frameMs)) frames.Add(samples[i].frameMs); }
        result.streamingMilliseconds = Stats(cpu);
        result.frameIntervalMilliseconds = Stats(frames.ToArray());
        result.gcAllocatedBytesPerFrame = Stats(allocations);
        string prefix = Path.Combine(outputDirectory, "buffer-" + buffer + "-" + name);
        using (var writer = new StreamWriter(prefix + "-frames.csv"))
        {
            writer.WriteLine("sample,streaming_ms,frame_interval_ms,gc_allocated_in_frame_bytes,active,pooled,owned,pending,generated");
            for (int i = 0; i < count; i++) writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2:F6},{3},{4},{5},{6},{7},{8}", i, samples[i].streamingMs, samples[i].frameMs, samples[i].allocated, samples[i].active, samples[i].pooled, samples[i].owned, samples[i].pending, samples[i].generated));
        }
        using (var writer = new StreamWriter(prefix + "-memory.csv"))
        {
            writer.WriteLine("elapsed_seconds,unity_used_bytes,unity_reserved_bytes,mono_used_bytes,active,pooled,owned");
            for (int i = 0; i < memoryCount; i++) writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1},{2},{3},{4},{5},{6}", memories[i].seconds, memories[i].unityUsed, memories[i].unityReserved, memories[i].monoUsed, memories[i].active, memories[i].pooled, memories[i].owned));
        }
        scenarios.Add(result);
        UnityEngine.Debug.Log("Measured buffer " + buffer + " / " + name + " / " + count + " frames");
    }
    MemorySample Memory(double seconds) => new MemorySample {
        seconds = seconds, unityUsed = Profiler.GetTotalAllocatedMemoryLong(), unityReserved = Profiler.GetTotalReservedMemoryLong(),
        monoUsed = Profiler.GetMonoUsedSizeLong(), active = controller.ActiveChunkCount, pooled = pool.PooledChunkCount, owned = pool.OwnedChunkCount
    };
    static double Seconds(long ticks) => (double)ticks / Stopwatch.Frequency;
    static Distribution Stats(double[] values)
    {
        if (values.Length == 0) return new Distribution();
        Array.Sort(values);
        double sum = 0; foreach (double value in values) sum += value;
        return new Distribution { samples = values.Length, mean = sum / values.Length, p50 = Percentile(values, 0.5), p95 = Percentile(values, 0.95), p99 = Percentile(values, 0.99), max = values[values.Length - 1] };
    }
    static double Percentile(double[] sorted, double p) => sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1)];
    void OnLog(string message, string trace, LogType type)
    {
        if (complete || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        File.WriteAllText(Path.Combine(outputDirectory, "failure.txt"), message + "\n" + trace);
        Application.Quit(1);
    }
    void OnDestroy()
    {
        gcAllocatedRecorder.Dispose();
        if (activeRecorders != null)
            for (int i = 0; i < activeRecorders.Length; i++) activeRecorders[i].Dispose();
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
#endif
