# Budgeted chunk generation

Based on dev revision `79bf03e`, with the queue changes fingerprinted in [source-hashes.json](source-hashes.json). The existing synchronous generator and reusable mesh buffers remain in use. Generation now drains pending coordinates across frames, with defaults of four chunks per frame and a 2 ms soft streaming budget.

## Behavior

On startup or a change to the Drone's tile/buffer/retention settings, ChunkController releases outgoing tiles and replaces pending coordinates with currently missing required tiles. It sorts requests by squared tile distance from the current Drone tile, with coordinate tie-breaking. Pending requests acquire no objects until generation starts. Each successful tile becomes active after mesh generation and placement; stationary Update calls continue draining requests.

The coordinate list contains each missing tile once, and active coordinates are excluded when rebuilding it. Rapid movement discards obsolete pending requests before more objects are acquired. Retention still applies to previously active tiles, not queued requests. Start and Update share a Unity-frame guard to avoid consuming the allowance twice in one frame.

The time budget includes selection/release work and is checked after each complete chunk. At least one pending chunk is attempted, so an indivisible chunk, selection/release work, or thread scheduling can exceed the time limit. The count cap is the hard limit. Setting the time budget to zero removes only the time limit. Coverage is gradual; fast movement can outrun generation even though pending work remains bounded by the requested square.

A generation error returns the acquired object and pauses generation, retaining the failed request. A subsequent tile/buffer/retention change rebuilds requests and resumes work. Automatic stationary retry is not provided. Status is exposed through ActiveChunkCount, PendingChunkCount, GeneratedChunksThisFrame, and GenerationPaused.

This is main-thread scheduling across frames, without background loading, Tasks, Jobs, or Burst.

## Method

Unity 6000.3.12f1 Windows Mono development player on the same i5-11300H machine, headless Null graphics, target 60 Hz, VSync off, CPU/Memory profiling enabled. Deep Profiling and allocation call stacks are disabled. Unity Editor remained open. The cached isolated project built successfully and passed correctness checks before measurement.

Original control workloads explicitly set a 1,000-chunk cap and disable the time limit. They still execute the new queue code. Budgeted workloads use fresh scenes, four chunks per frame, and 2 ms. Startup holds position until all tiles are active; a single teleport then holds position until coverage completes. The rapid case moves between 24 disjoint neighborhoods in consecutive frames, then drains the final view.

Coverage latency includes frame pacing and is measured from the first controller invocation to the invocation that completes coverage. Rapid-case latency includes the entire 24-frame movement sequence. CPU timings bracket controller calls; profiler overhead and scheduling affect results. One run does not establish a universal frame-time bound, and this is not a rendered FPS/GPU benchmark.

## Budgeted results

| Buffer / tiles | Scenario | Frames | Coverage ms | Streaming p95 ms | Streaming max ms |
|---|---|---:|---:|---:|---:|
| 2 / 25 | Startup | 7 | 100.66 | 0.80 | 0.80 |
| 2 / 25 | Single teleport | 7 | 100.26 | 0.39 | 0.39 |
| 2 / 25 | Rapid teleports + drain | 30 | 484.05 | 0.78 | 1.02 |
| 3 / 49 | Startup | 13 | 201.60 | 0.77 | 0.77 |
| 3 / 49 | Single teleport | 13 | 204.14 | 1.23 | 1.23 |
| 3 / 49 | Rapid teleports + drain | 36 | 588.21 | 0.46 | 0.66 |
| 5 / 121 | Startup | 31 | 500.94 | 0.83 | 1.11 |
| 5 / 121 | Single teleport | 31 | 499.75 | 0.73 | 0.78 |
| 5 / 121 | Rapid teleports + drain | 54 | 885.40 | 0.52 | 0.79 |

All budgeted raw samples generate at most four tiles per frame. Peak pending counts are 21, 45, and 117 after the first batch. Every budgeted workload ends with zero pending requests and complete requested coverage. In this run the count cap dominates the configured 2 ms time limit; a separate correctness check exercises the time cutoff using a tiny budget.

Buffer-5 rapid movement generates 213 tiles total, including 121 for final coverage and four for each of 23 superseded views. Owned count remains at 121; canceled pending coordinates create no additional objects. Coverage after the final movement takes 31 sampled invocations, including that movement frame.

For context, the same build's unrestricted buffer-5 startup creates all 121 tiles in one 6.65 ms invocation. Budgeted startup spreads that work across 31 invocations, with a 1.11 ms maximum. Total measured startup work is approximately 13.40 ms across those invocations; spreading work does not promise lower total CPU cost. The control's 24 full teleports peak at 13.54 ms; the held-position budgeted teleport is a different workload and should not be treated as a matched statistical speed comparison. Previous buffer-reuse results are [here](../2026-10-02-buffer-reuse/comparison.md).

## Memory and verification

The 180-second control traversal retains the earlier route, with count/time limits effectively disabled to preserve full-generation behavior. Owned chunks start and end at 143. Unity allocated memory rises from 98,510,425 to 98,512,829 bytes (+2,404 bytes); whole-frame managed allocations total approximately 1.68 MiB and the generation-0 collection counter increases seven times. This control soak does not establish long-duration memory behavior with default budgets; budgeted measurements above cover startup, held teleports, and rapid cancellation.

The Windows build and all **181 checks passed**. These cover previous terrain/mesh integrity, coverage, failure recovery, and ownership tests, plus nearest-center activation, one-chunk caps, rapid cancellation, queue shrink, tiny-time-budget deferral, and stationary completion. The benchmark passed runtime-error checks, count/ownership bounds, allocation calibration, generation-count checks, and bounded completion checks.

Full evidence: [summary.json](summary.json), adjacent raw frame/memory CSVs, [runtime-checks.txt](runtime-checks.txt), [build-checks.txt](build-checks.txt). The Unity Profiler capture remains ignored at `Temp/QueueBaseline/cpu-capture.raw`. Rerun instructions and workload definitions are in [Tools/Profiling](../../../Tools/Profiling/README.md).

Next validation should use a rendered player with the default budgets, observing visible coverage during realistic flight and teleports. Queue/coverage counters can support an in-game diagnostic display. Jobs/Burst and rendering changes require separate measurements.
