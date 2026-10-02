# Streaming CPU and memory baseline — 2026-10-02

This measures the configurable square-neighborhood implementation based on dev commit `155df43ba3a26b8c3747717a71ac0bd6e0389adc`, with profiling markers and the benchmark harness added. It is **not** a measurement of the original V0 commit. Source fingerprints are in [source-hashes.json](source-hashes.json).

## Environment and method

Unity 6000.3.12f1 Windows standalone development player, Mono, Intel Core i5-11300H (8 logical processors), 7,975 MB system RAM. Headless `Null` graphics device, target 60 Hz, VSync off. CPU/Memory profiling and binary capture enabled; Deep Profiling and allocation call stacks disabled. Unity Editor remained open on the same machine. This is one run, not a release-build or GPU benchmark.

The isolated player invokes the existing controller through cached delegates. Setup and CSV writing are outside the streaming Stopwatch bracket. Fresh-terrain startup excludes scene/process load and follows a small call-path warm-up. Retention margin is 1. Whole-frame GC allocations include the engine and harness; they are not terrain-only attribution. Memory includes fixed measurement buffers. Profiler overhead and trace I/O can influence results.

Full procedure and rerun command: [profiling README](../../../Tools/Profiling/README.md). [summary.json](summary.json) contains complete metrics; the adjacent CSV files contain raw samples. The 292 MiB Unity Profiler capture remains in ignored `Temp/StreamingBaselineFinal/cpu-capture.raw` and is not committed.

## Results

Streaming CPU times below cover the controller invocation, not total frame time. GC is mean allocated KiB per sampled whole frame.

| Buffer / requested tiles | Workload | Samples | Mean ms | p95 ms | Max ms | Mean GC KiB |
|---|---|---:|---:|---:|---:|---:|
| 2 / 25 | Startup | 1 | 1.88 | — | 1.88 | 106.98 |
| 2 / 25 | Boundary every frame | 240 | 0.66 | 1.74 | 2.69 | 21.18 |
| 2 / 25 | Disjoint teleport | 24 | 2.06 | 3.21 | 3.87 | 101.35 |
| 3 / 49 | Startup | 1 | 3.65 | — | 3.65 | 210.62 |
| 3 / 49 | Boundary every frame | 240 | 0.58 | 1.38 | 2.32 | 29.90 |
| 3 / 49 | Disjoint teleport | 24 | 3.21 | 5.24 | 5.65 | 198.51 |
| 5 / 121 | Startup | 1 | 7.33 | — | 7.33 | 516.88 |
| 5 / 121 | Boundary every frame | 240 | 1.22 | 2.40 | 4.37 | 47.47 |
| 5 / 121 | Disjoint teleport | 24 | 10.58 | 15.90 | 20.18 | 488.97 |
| 5 / 121 | 180-second traversal | 10,756 | 0.13 | 1.16 | 7.07 | 4.37 |

The soak travels at 100 units/second with sinusoidal Z motion. Only 999 sampled frames perform a neighborhood update; the remaining frames mostly check position. Update-marker p95 among frames with updates is 2.85 ms, so the all-frame average should not be read as the cost of a boundary crossing.

One of 24 buffer-5 teleport samples exceeds the entire 16.67 ms budget associated with 60 Hz, before rendering. Buffers 2 and 3 have no such samples in this run. The small teleport/startup sample counts and instrumented environment limit generalization. Buffer 3's lower continuous mean than buffer 2 illustrates why this single run does not establish a precise scaling curve.

## Ownership and memory

- Owned chunks grow to 34, 62, and 142 during the respective boundary stress scenarios. Released objects are reused through teleports; generation still rebuilds every required mesh.
- After warm-up, the buffer-5 soak starts and ends with **143 owned chunks**, with every one-second memory sample reporting 143. Active counts range from 132 to 143, pooled counts from 0 to 11. The required square contains 121 tiles; retention preserves additional previously active tiles.
- Unity allocated memory starts at 96,781,873 bytes and ends at 96,784,535 bytes (+2,662 bytes). Sampled range is 96,781,827–96,784,580 bytes. This run shows stable allocated memory and ownership, not proof that every path is leak-free.
- Unity reserved memory rises from 181.36 to 185.36 MiB (+4 MiB). Mono used memory ranges from 2.76 to 3.11 MiB. Reserved-memory growth alone does not establish a leak.
- Whole-frame managed allocation totals **45.86 MiB** during the soak. The generation-0 collection counter increases 211 times; this does not measure pause duration or prove 211 full stop-the-world collections.

Memory evidence: [buffer-5-soak-memory.csv](buffer-5-soak-memory.csv).

## Where the measured work goes

The soak generates 11,166 chunks across 999 neighborhood updates. Inclusive marker totals: streaming 1,363.08 ms, generation 840.05 ms, mesh-data construction 570.24 ms, mesh application 242.96 ms, release 195.85 ms. Data construction is approximately 42% of streaming scope time and application 18%; generation includes both. Nested totals must not be added together.

The current generator allocates a 121-element Vector3 array and a 600-element int array for every chunk: 3,852 bytes of payload before array headers. Lists and chunk naming also allocate. These are confirmed allocation sources in code; the whole-frame counter does not independently attribute all 45.86 MiB to them. No GPU, draw-call, rendered frame-time, allocation call-stack, disk-loading, or Jobs/Burst measurements were collected.

## Recommended next change

1. Reuse mesh-data buffers and neighborhood/removal scratch collections; cache the fixed triangle topology. This addresses confirmed recurring allocations without changing streaming behavior. Rerun these workloads and compare allocation totals and CPU distributions.
2. Introduce a bounded generation queue with a per-frame work budget, including cancellation of obsolete requests after teleports. The measured synchronous buffer-5 burst can consume an entire frame budget; a queue would spread that work, with delayed coverage as an explicit tradeoff.
3. Profile a rendered development player on a repeatable camera route before selecting rendering optimizations or Jobs/Burst. This run does not establish a need for either. Higher-resolution mesh generation should be measured separately.

These are recommendations; this profiling pass implements no allocation, scheduling, rendering, or concurrency optimization.

## Verification

The Windows development build passed, followed by all 165 existing runtime checks, including neighborhood radii, boundary/corner movement, teleport ownership bounds, failure recovery, pool reset, and scene reload. Reports: [build-checks.txt](build-checks.txt), [runtime-checks.txt](runtime-checks.txt). The benchmark also passed marker generation-count checks, startup allocation calibration, ownership/retention invariants, and runtime-error checks. The corrected harness discards the unfinished frame's zero-count counter sample. Earlier calibration runs were rejected and are excluded from this baseline.
