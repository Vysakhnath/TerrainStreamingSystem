# Streaming CPU and memory baseline

Run from the project root:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/Validation/Verify-V0.ps1 -Benchmark
```

The default soak is 180 seconds. `-SoakSeconds 0` runs a short smoke test; values up to 600 are supported. `-UnityPath` selects the installed Unity 6000.3.12f1 editor. Windows build support is required.

The runner builds an isolated Windows development player, runs the terrain correctness checks, then starts the instrumented player with `-batchmode -nographics`. The benchmark source is copied into that isolated project's Assets; it is not part of the normal Unity project/player. All generated output stays under the printed ignored Temp directory.

## Workloads

Each buffer (2, 3, 5) starts with a fresh MainScene and retention margin 1:

1. Startup: one measured call creates all 25, 49, or 121 required tiles. This is terrain startup, not process/scene load time. Managed/native call paths are warmed with a one-tile scene before comparisons; each measured buffer then starts with fresh terrain in the same process.
2. Continuous movement: 240 frames, advancing one 20-unit chunk in X per frame. Z alternates by one tile every 30 frames. This is deliberate boundary-crossing stress, not normal flight speed.
3. Teleports: 24 disjoint neighborhoods at distant positive/negative coordinates, one per frame.
4. Buffer-5 soak: 120 pool-warm-up movement frames, 120 settling frames, then 180 seconds of traversal at 100 units/second with sinusoidal Z movement.

The target frame rate is 60 and VSync is disabled. Frame intervals include pacing, engine work, scheduling jitter, and the harness. They are not CPU-only frame time or a rendered FPS benchmark.

The harness explicitly enables CPU/Memory profiling and binary recording, with allocation call stacks disabled and no Deep Profiling. Timings include profiler overhead; trace-file I/O can affect frame intervals. This is an instrumented baseline rather than release-build performance.

## Measurements and validity

- Streaming milliseconds: Stopwatch around a cached delegate invoking the unchanged ChunkController Start/Update method. The controller's automatic Update is disabled to prevent duplicate work. Reflection/delegate setup, position assignment, file writing, and summary construction are outside this timing bracket.
- GC allocated bytes: Unity ProfilerRecorder's GC Allocated In Frame counter across completed sampled frames and all threads, including engine/harness allocations. Arrays and recorder storage are allocated before a settling frame; CSV writing and summaries happen after collection. The unfinished current-frame sample (`Count == 0`) is discarded before selecting the final measured frames. This is not exact allocation attribution to terrain alone and does not include native mesh memory. Known startup array payload provides a nonzero calibration check. The initially tried GC.GetAllocatedBytesForCurrentThread returned zero in the Mono player and was rejected.
- Profiler marker timings: Unity ProfilerRecorder samples on the main thread, summed per frame. Each marker records total calls, total milliseconds, mean per call, and frame percentiles for frames containing calls. Nested markers have inclusive times and must not be added together.
- Active/pooled/owned counts: sampled after each invocation, with ownership and retention-bound assertions.
- Memory: Unity allocated/reserved and Mono used memory, sampled at the start/end and once per second during the soak. Memory includes engine and fixed-size harness/recorder buffers. Across scenarios those buffers differ in size, so compare memory trends within a scenario. No forced GC is used.
- GC collections: generation-0 collection-count change across the measured window (Mono's collector semantics apply).

Marker generation counts are checked against startup and teleport workloads. Invalid markers, runtime errors, ownership failures, and recording-capacity overflow fail the run. Recorder resources are disposed after scenarios and on teardown.

Nearest-rank p50/p95/p99 values are reported. Startup has one sample and teleports have only 24: their percentiles should not be treated as statistically robust estimates. This is a single-machine baseline, not a universal performance claim.

## Output

`BenchmarkResults/summary.json` contains hardware/build metadata and per-scenario distributions. Each scenario has raw `*-frames.csv` and `*-memory.csv` files. `completed.txt` confirms completion; runtime errors produce `failure.txt` and a nonzero exit. The player log is outside the result folder.

`cpu-capture.raw` can be loaded in Unity's Profiler window. Keep this potentially large capture outside Git; the summary and small memory CSV are sufficient for a tracked baseline report.

Profiler markers are also available in ordinary Editor Play Mode/development builds:

- Terrain.StreamingUpdate
- Terrain.SelectChunks
- Terrain.ReleaseChunks
- Terrain.PoolAcquire
- Terrain.GenerateChunk
- Terrain.BuildMeshData
- Terrain.ApplyMesh
- Terrain.RecalculateNormals
- Terrain.RecalculateBounds

Attach Unity's CPU Profiler to a rendered development player to inspect these scopes under a real camera/render workload. This automated baseline provides no GPU, draw-call, texture-upload, or rendered frame-time measurements.

API references: [ProfilerRecorder](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html), [ProfilerRecorderOptions](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.html), [ProfilerMarker.Auto](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerMarker.Auto.html).

Allocation API reference: [Unity profiler counters](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-counters-reference.html). Unity also documents the Mono zero-result issue in [UUM-100690](https://issuetracker.unity.com/issues/5660/crash-on-runtimefieldinforesolvetype-with-il2cpp-and-returns-0-with-mono-when-calling-gcgetallocatedbytesforcurrentthread-method); the rejection here is based on the observed smoke-test result in this editor version.
