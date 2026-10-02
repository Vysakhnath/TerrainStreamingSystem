# Streaming diagnostics and rendered validation

Base revision `e2561a4`, with the diagnostic changes fingerprinted in [source-hashes.json](source-hashes.json). MainScene now references an optional StreamingDebugOverlay alongside its ChunkController.

## Delivered diagnostics

- F3 toggles the development overlay. It displays current Drone tile, active/queued/pooled/owned counts, last sampled frame's generated tiles and streaming CPU time, interval peaks, status, and retained error history.
- Text refreshes at 4 Hz, with immediate refresh on tile and queue/paused-state transitions. Strings are cached between refreshes. This IMGUI tool has rendering/allocation overhead; it is not allocation-free production UI.
- Editor gizmos draw current tile white, requested boundary cyan, and retention boundary amber, at a configurable height. They require Editor Gizmos to be enabled and are not player-rendered lines.
- Controller telemetry retains streaming errors through recovery and sums streaming work performed in the current Unity frame. It reports zero time on frames without streaming work. Configuration/dependency failures also appear in error history.
- Overlay Update/LateUpdate/OnGUI code is excluded from release players and disabled in headless/control benchmarks. No streaming scheduling behavior is changed by the overlay.

## Rendered method

Windows Unity 6000.3.12f1 development player, NVIDIA GeForce GTX 1650, Direct3D12, actual resolution 1280x720. Visible window, 60 Hz target, VSync off, run-in-background enabled, overlay visible, CPU/Rendering/GPU profiler enabled, no Deep Profiling or allocation call stacks. Unity Editor remained open on the same machine.

The route uses the real controller Update and default four-chunk/2 ms soft budget. It waits for initial coverage and captures the saved camera view, then raises the Drone to Y=35 and changes the child camera's local viewing pose for inspection. This preserves the saved scene pose. It flies for five seconds at the scene's saved 5 units/second speed, five seconds at 300 units/second, then teleports to (-5000, 35, 5000) and holds for eight seconds.

Frame intervals include pacing, scheduling, overlay rendering, profiling, shader startup, and screenshot overhead. Recorder rendering/GPU values describe the previous completed frame. GPU samples are used only when the counter supplies positive nanosecond values; missing values are -1. Counter meanings and availability are documented by [Unity's profiler counter reference](https://docs.unity3d.com/6000.3/Documentation/Manual/profiler-counters-reference.html); platform/API support is described in [GPU Usage Profiler](https://docs.unity3d.com/6000.3/Documentation/Manual/ProfilerGPU.html).

An initial hidden-window run produced black screenshots and zero draw counters. It is excluded. The final harness rejects runs without positive draw/triangle samples, and the runner opens a visible window for this check. These are single-machine smoke measurements, not release FPS guarantees.

## Results

| Route | Samples | Frame interval p95 / max ms | Streaming p95 / max ms | GPU p95 / max ms | Mean / max draws |
|---|---:|---|---|---|---|
| Normal flight | 297 | 16.82 / 72.87 | 0.00 / 0.61 | 3.65 / 66.73 | 112.36 / 122 |
| Fast flight | 297 | 16.79 / 54.24 | 0.31 / 2.20 | 3.83 / 48.89 | 117.98 / 125 |
| Teleport + hold | 479 | 16.73 / 43.98 | 0.00 / 0.44 | 3.76 / 34.57 | 107.76 / 117 |

All-frame streaming percentiles include idle frames, so zero p95 in the slow-flight/hold phases does not mean generation costs zero. Draw counts include the full render pipeline and diagnostic UI, rather than one draw per terrain object. These figures do not establish a draw-call or GPU bottleneck. Each phase's largest frame/GPU outliers occur shortly after its screenshot capture (near 4, 9, and 10 seconds). This association does not isolate the cause; attribution requires the profiler capture, and screenshot recording and other instrumentation are included in this run.

Final coverage is complete: 25 active tiles, zero pending requests, 30 owned objects, and zero streaming errors. Fast flight's peak pending count is four; the teleport begins with 21 queued tiles after its first generated batch. The screenshots show both filling and completed coverage. The short rendered route is not a long-duration memory/leak test.

## Visual findings and next priorities

1. **Initial viewing pose needs correction.** The original camera has world Y=0.87 because it inherits the Drone's nonuniform scale. Even with all 25 tiles active, [original-camera.png](original-camera.png) shows the Drone/sky and terrain above the view. Start the Drone/camera above the height field and separate camera placement from the scaled visual body before a portfolio demo.
2. **Shared-edge shading needs correction.** [normal-flight.png](normal-flight.png) and [complete-coverage.png](complete-coverage.png) show tile shading seams. A shared edge in the final neighborhood has up to **16.62 degrees** of normal disagreement between coincident border vertices. Existing height continuity checks pass; each chunk independently calls RecalculateNormals, so shared heights do not guarantee shared normals. Use world-consistent normals or a border neighborhood when computing normals, then verify edge-normal continuity.
3. **Coverage delay is visible and bounded in this route.** [teleport-filling.png](teleport-filling.png) shows four active tiles and 21 queued; [complete-coverage.png](complete-coverage.png) shows the completed square. Tune budgets against rendered coverage latency after the visual issues above are fixed. Jobs/Burst or draw-call optimizations are not justified solely by these smoke results.

No viewing-pose, normal-generation, material, or rendering optimization is implemented in this diagnostics pass.

## Verification and reproduction

Windows player build and all **184 checks passed**, including scene diagnostic references, error retention/recovery, previous mesh integrity, queue behavior, coverage, and ownership checks. The rendered route verifies an active populated overlay, positive rendered samples, final coverage, and no streaming errors. Screenshots were visually inspected for legibility and terrain visibility. Editor gizmo code compiled; no automated visual check of the Scene view gizmos was performed. F3 is wired through the same legacy Input API used by the Drone; its physical key interaction was not automated.

Run `powershell -ExecutionPolicy Bypass -File Tools/Validation/Verify-V0.ps1 -RenderedCheck`. Keep the player window visible. The runner builds an isolated project and closes the player after its route. Details: [profiling README](../../../Tools/Profiling/README.md).

Raw evidence: [rendered-check.json](rendered-check.json), [runtime-checks.txt](runtime-checks.txt), [build-checks.txt](build-checks.txt), and the adjacent screenshots. The potentially large binary capture remains ignored at `Temp/RenderedDiagnosticsFinal/rendered-capture.raw`.
