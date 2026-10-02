# Terrain validation (V0 stabilization and V1 neighborhood streaming)

From the project root, run:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/Validation/Verify-V0.ps1
```

Use `-UnityPath` if Unity 6000.3.12f1 is installed elsewhere. Windows build support must be installed. The V0 runner name is retained for compatibility.

The runner copies Assets, Packages, and ProjectSettings into a unique ignored Temp directory. It builds a Windows development player and checks MainScene in batch Play Mode. Tools/Validation is outside Assets and does not compile into the original project or player.

Checks cover startup, unchanged mesh topology and border heights, negative coordinates, diagonal movement, teleports, generation failure/recovery, pool reset and mesh cleanup, duplicate returns, scene reload, stationary configuration changes, buffer radii 0/1/2/3, and 24 movement/teleport steps with object-count bounds.

## Neighborhood settings

Select ChunkController in the scene Inspector:

- **Chunk Buffer Count**: requested square radius. Default 2 creates 25 tiles (5 by 5).
- **Chunk Retention Margin**: extra square rings that retain previously generated tiles. Default 1 permits at most 49 active tiles (7 by 7) with buffer 2. The margin does not eagerly generate extra tiles.

A tile is released when its maximum X/Z coordinate offset exceeds buffer plus margin. Requested corners are always inside retention. Settings changes update streaming while stationary in Play Mode. Negative values are clamped to zero by Inspector validation.

Outgoing tiles return to the pool before missing tiles are acquired. Teleports can therefore reuse released objects within the same update. The pool retains its historical high-water count; it is not automatically trimmed when the buffer shrinks.

The expected failure test sets resolution to zero and accepts only the generator's matching ArgumentOutOfRangeException. It verifies recovery at the next chunk boundary; automatic stationary retry is not implemented.

The harness records one exact Unity Editor Search startup exception separately in editor-search-warning.txt when present. This editor indexing error is excluded by message and stack signature. Other errors fail validation.

Logs, reports, and the player remain in the printed Temp directory. The first run can take several minutes for imports and shader compilation. The runner has a 20-minute overall timeout. These are correctness checks, not visual validation or frame-time measurements.
