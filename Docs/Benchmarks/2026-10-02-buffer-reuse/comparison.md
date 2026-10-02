# Mesh-buffer and scratch-list reuse

This change reuses one vertex array per TerrainGenerator, caches triangle topology per resolution, and reuses ChunkController's selection/removal lists. Mesh data is still copied into each chunk's owned Unity Mesh, and generation remains synchronous on the main thread. A resolution change rebuilds the buffers and clears an incompatible existing mesh before applying the new topology. Lists retain their high-water capacity until their controller is destroyed.

Base revision: `834b7a0`, with the changes fingerprinted in [source-hashes.json](source-hashes.json). Comparison source: [previous baseline](../2026-10-02/baseline.md). The previous measurements were recorded before commit 834b7a0; that commit packaged the baseline instrumentation and results.

## Method

Same Unity 6000.3.12f1 Windows Mono development player, i5-11300H, headless Null graphics device, 60 Hz target, retention margin 1, profiling enabled, and workload definitions as the previous run. Unity Editor remained open. The build used the cached isolated validation project. No Deep Profiling or allocation call stacks were enabled. This is a single before/after pair, not a controlled statistical study or rendering benchmark.

The harness's allocation-calibration floor changed from one vertex/index payload per chunk to one 3,852-byte payload per fresh generator, because subsequent chunks reuse that buffer pair. Allocation collection, sample-window selection, marker scopes, and workloads are otherwise unchanged. Whole-frame allocation counts include the engine/harness and are not terrain-only measurements. The timed soak follows elapsed time, so sample/update counts differ slightly between runs.

## Allocation results

Mean whole-frame managed allocation in KiB:

| Buffer | Scenario | Before | After | Reduction |
|---:|---|---:|---:|---:|
| 2 | Startup | 106.98 | 15.12 | 85.9% |
| 2 | Boundary every frame | 21.18 | 0.78 | 96.3% |
| 2 | Teleport | 101.35 | 4.50 | 95.6% |
| 3 | Startup | 210.63 | 26.98 | 87.2% |
| 3 | Boundary every frame | 29.90 | 1.10 | 96.3% |
| 3 | Teleport | 198.51 | 8.84 | 95.5% |
| 5 | Startup | 516.88 | 57.90 | 88.8% |
| 5 | Boundary every frame | 47.47 | 1.74 | 96.3% |
| 5 | Teleport | 488.97 | 21.92 | 95.5% |
| 5 | 180-second traversal | 4.37 | 0.16 | 96.3% |

Total soak allocations decrease from **45.86 MiB to 1.68 MiB**. Generation-0 collection-counter increments decrease from 211 to 8; these are not measured full-GC pause counts. The soak generates 11,135 chunks across 995 neighborhood updates (previously 11,166 across 999). The small workload-count difference does not explain the allocation reduction.

This is not allocation-free streaming: chunk naming still creates strings, first-use collection growth still allocates, and Unity/harness activity can allocate. The vertex/index payload is now allocated once per generator/resolution change rather than once per generated chunk.

## CPU timing and limits

Selected streaming Stopwatch measurements in milliseconds:

| Scenario | Before mean / p95 / max | After mean / p95 / max |
|---|---|---|
| Buffer-2 continuous | 0.66 / 1.74 / 2.69 | 0.42 / 0.81 / 1.07 |
| Buffer-3 continuous | 0.58 / 1.38 / 2.32 | 0.66 / 1.20 / 2.01 |
| Buffer-5 continuous | 1.22 / 2.40 / 4.37 | 1.00 / 1.55 / 3.62 |
| Buffer-3 teleport | 3.21 / 5.24 / 5.65 | 4.18 / 5.37 / 6.33 |
| Buffer-5 teleport | 10.58 / 15.90 / 20.18 | 4.77 / 8.89 / 16.53 |
| Buffer-5 soak, all frames | 0.13 / 1.16 / 7.07 | 0.09 / 0.70 / 18.42 |

Startup has only one sample per buffer. Buffer-5 startup increases from 7.33 to 16.12 ms despite much lower allocation. The traversal maximum also worsens; its mesh-data marker reaches 14.87 ms in one frame. The cause of these outliers has not been isolated, and profiler overhead or scheduling can contribute. No universal speedup or frame-time guarantee is claimed.

Among soak frames with actual neighborhood updates, streaming-marker p95 decreases from 2.85 to 1.59 ms. Total mesh-data scope time decreases from 570.24 to 267.89 ms. Scopes are inclusive and elapsed-time based; differences cannot be attributed solely to allocation removal from this one pair of runs.

## Memory and correctness

The soak starts and ends with 143 owned chunks; every one-second sample reports 143. Unity allocated memory starts at 97,066,017 bytes and ends at 97,068,712 bytes (+2,695 bytes). Reserved memory rises by 4 MiB, as in the prior run. Stable ownership and allocated memory in this route do not establish leak freedom for every scenario.

Windows player build and all **169 checks passed**, including the previous streaming coverage, negative coordinates, seams, teleport reuse, failure recovery, reset, and scene reload checks. Four added checks verify regenerated vertex positions, topology rebuilding when resolution changes, restored triangle winding/indices, and independence of previously published meshes after scratch-buffer overwrite.

The standalone benchmark passed startup allocation calibration, expected generation counts, ownership/retention bounds, and runtime-error checks. Full data: [summary.json](summary.json), adjacent raw frame/memory CSV files, [runtime-checks.txt](runtime-checks.txt), and [build-checks.txt](build-checks.txt). The binary Profiler capture remains ignored at `Temp/BufferReuseBaseline/cpu-capture.raw`.

## Next step

Add a bounded generation queue with a per-frame work budget and cancellation of stale requests after rapid movement. Allocation reuse reduces recurring GC pressure; synchronous bursts and outliers remain. A rendered-player profile is still needed before choosing GPU/rendering optimizations or claiming a 60 FPS target is met. Jobs/Burst remain a separate, measured decision.
