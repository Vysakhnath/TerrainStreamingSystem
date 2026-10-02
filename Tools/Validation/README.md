# Terrain validation (V0 stabilization and V1 neighborhood streaming)

From the project root, run:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/Validation/Verify-V0.ps1
```

Use `-UnityPath` if Unity 6000.3.12f1 is installed elsewhere. Windows build support must be installed. The V0 runner name is retained for compatibility.

The runner copies Assets, Packages, and ProjectSettings into a unique ignored Temp directory. It builds a Windows development player and checks MainScene in batch Play Mode. Tools/Validation is outside Assets and does not compile into the original project or player.

Checks cover startup, unchanged mesh topology and border heights, negative coordinates, diagonal movement, teleports, generation failure/recovery, pool reset and mesh cleanup, duplicate returns, scene reload, stationary configuration changes, buffer radii 0/1/2/3, and 24 movement/teleport steps with object-count bounds.

Camera checks cover an unscaled Drone root, preserved visual/collider dimensions, the elevated starting pose, and the camera's inherited translation offset. MainScene starts the Drone at Y=35, with its camera at local (0, 8, -12), pitched downward 60 degrees. Drone Visual holds the original nonuniform scale; changing that child scale does not change the camera rig. This is a fixed starting pose, not terrain collision avoidance or automatic altitude control.

Mesh-buffer reuse checks also cover regenerated vertex positions, topology changes on an existing mesh, restored indices/winding, and independence of already published meshes after scratch-buffer overwrite. For allocation and timing comparisons, run with `-Benchmark`; see [profiling instructions](../Profiling/README.md).

Shared-normal checks compare exact vector components across both axes and corners at startup and after a distant negative-Z teleport, require upward unit normals, verify the central height-gradient direction, and compare edges after a resolution change with noninteger chunk size. The default grid's original noise heights are checked separately. Normals use a reusable one-sample height border and do not require neighboring chunk objects.

## Neighborhood settings

Select ChunkController in the scene Inspector:

- **Chunk Buffer Count**: requested square radius. MainScene uses 5, creating 121 tiles (11 by 11), for the saved camera at Drone Y=35–45 and aspect ratios up to 16:9. A newly added controller still starts with the source field default of 2 (25 tiles).
- **Chunk Retention Margin**: extra square rings that retain previously generated tiles. Default 1 permits at most 49 active tiles (7 by 7) with buffer 2. The margin does not eagerly generate extra tiles.

A tile is released when its maximum X/Z coordinate offset exceeds buffer plus margin. Requested corners are always inside retention. Settings changes update streaming while stationary in Play Mode. Negative values are clamped to zero by Inspector validation.

Outgoing tiles return to the pool before missing tiles are acquired. Teleports can therefore reuse released objects within the same update. The pool retains its historical high-water count; it is not automatically trimmed when the buffer shrinks.

## Generation queue

`Max Chunks Per Frame` defaults to 4. `Generation Budget Milliseconds` defaults to 2; 0 disables the time limit while retaining the chunk-count cap. The budget includes selection/release work and is checked after each completed chunk. At least one chunk is attempted when work is pending. Mesh generation and selection cannot be interrupted, so this is a soft time budget, not a hard frame-time ceiling.

Missing coordinates are queued once, sorted by squared distance from the Drone's current tile (coordinate order breaks ties). Each neighborhood/settings change replaces the pending list with currently missing required tiles. Pending requests own no GameObjects or meshes. Previously active tiles within retention remain active. Stationary Update calls drain the queue; terrain coverage fills gradually after startup and teleports. Start and Update share a frame guard so they cannot each consume a separate generation allowance in the same Unity frame.

`ActiveChunkCount`, `PendingChunkCount`, `GeneratedChunksThisFrame`, and `GenerationPaused` expose current status. A generation failure returns the acquired object, keeps the request queued, and pauses further generation to prevent repeated error logs. Moving to another tile or changing the buffer/retention settings rebuilds requests and resumes generation; restoring the generator's configuration alone does not retry automatically.

MainScene's development overlay also displays current-frame streaming time and StreamingErrorCount/LastStreamingError. Error history includes dependency/configuration failures and persists through recovery until scene reload. F3 toggles the overlay; Editor gizmos show requested/retained boundaries. `Verify-V0.ps1 -RenderedCheck` builds and runs the visible graphics route described in the [profiling instructions](../Profiling/README.md).

The correctness harness lets scene startup finish, verifies radius 5 and camera ground bounds, then normalizes to radius 2 and clears only available pool objects for the established lifecycle checks. Scene reload verifies radius 5 again before normalization. Existing reference checks use a large count cap and no time limit; separate budgeted checks use one chunk per frame, rapid teleports, a shrinking buffer, and a tiny time limit. Budgeted profiling scenarios report time/frames to full coverage as well as per-frame CPU costs.

`camera-coverage.csv` measures saved-camera corner rays intersecting ground planes Y=-1/0/20 at Drone heights 35/45/60, aspect ratios 4:3/16:9/21:9, and nine intra-tile positions at negative world coordinates. Radius 5 covers the chosen Y=35–45, up-to-16:9 envelope once the queue drains. Higher altitude, wider aspect, changed FOV/pitch, rapid movement and teleports can expose unloaded ground. The radius is fixed and configurable; automatic camera-based selection and prefetch are not implemented.

The expected failure test sets resolution to zero and accepts only the generator's matching ArgumentOutOfRangeException. It verifies recovery at the next chunk boundary; automatic stationary retry is not implemented.

The harness records one exact Unity Editor Search startup exception separately in editor-search-warning.txt when present. This editor indexing error is excluded by message and stack signature. Other errors fail validation.

Logs, reports, and the player remain in the printed Temp directory. The first run can take several minutes for imports and shader compilation. The runner has a 20-minute overall timeout. These are correctness checks, not visual validation or frame-time measurements.
