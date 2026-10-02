# V0 stabilization validation

From the project root, run:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/Validation/Verify-V0.ps1
```

Use `-UnityPath` if Unity 6000.3.12f1 is installed elsewhere. Windows build support must be installed.

The runner copies Assets, Packages, and ProjectSettings into a unique ignored Temp directory. It builds a Windows development player, then checks the MainScene in batch Play Mode: startup, mesh dimensions, negative coordinates, boundary crossing, border heights, teleport release, injected generation failure and recovery, pool reset, duplicate returns, non-divisible tile sizes, and scene reload/mesh cleanup.

The expected failure test deliberately sets resolution to zero and accepts only its ArgumentOutOfRangeException. Other runtime errors fail validation. Failure recovery is checked at the next chunk boundary; there is no automatic retry while stationary.

The copy contains the temporary Editor harness; Tools/Validation itself is outside Assets and is excluded from Unity compilation and player builds. The original project's scenes and settings are not changed. Logs, reports, and the generated player remain in the printed Temp directory. The first run may take several minutes to import packages and compile shaders. The runner has a 20-minute overall timeout.

These are correctness checks, not frame-time measurements or visual rendering validation.

The harness records one exact Unity Editor Search startup exception separately in editor-search-warning.txt when present. This editor indexing error is excluded by message and stack signature; terrain/runtime exceptions still fail validation.
