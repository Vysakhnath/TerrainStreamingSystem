param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.3.12f1/Editor/Unity.exe',
    [switch]$Benchmark,
    [switch]$RenderedCheck,
    [ValidateRange(0, 600)][int]$SoakSeconds = 180
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
$runRoot = Join-Path $projectRoot ('Temp/V0Validation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $folder) -Destination $runRoot -Recurse
}
$editorRoot = Join-Path $runRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $editorRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'V0Validation.cs') -Destination $editorRoot
if ($Benchmark -or $RenderedCheck) {
    $benchmarkAssets = Join-Path $runRoot 'Assets/Benchmark'
    New-Item -ItemType Directory -Path $benchmarkAssets -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Tools/Profiling/TerrainBenchmark.cs') -Destination $benchmarkAssets
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Tools/Profiling/TerrainRenderedCheck.cs') -Destination $benchmarkAssets
}
$logPath = Join-Path $runRoot 'validation.log'
Write-Output "Validation copy: $runRoot"
$process = Start-Process -FilePath $UnityPath -ArgumentList "-batchmode -nographics -projectPath `"$runRoot`" -executeMethod V0Validation.Run -logFile `"$logPath`"" -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(20)
while (!$process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) {
        Stop-Process -Id $process.Id
        throw "Validation timed out. See $logPath"
    }
}
foreach ($report in @('build-checks.txt', 'runtime-checks.txt', 'validation-failure.txt', 'editor-search-warning.txt')) {
    $reportPath = Join-Path $runRoot $report
    if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath }
}
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $runRoot 'runtime-checks.txt'))) {
    throw "Validation failed. See $logPath"
}
Write-Output "Validation passed. Build and reports: $runRoot"


if ($Benchmark) {
    $benchmarkOutput = Join-Path $runRoot 'BenchmarkResults'
    New-Item -ItemType Directory -Path $benchmarkOutput -Force | Out-Null
    $playerPath = Join-Path $runRoot 'Builds/Validation/DroneTerrainSystem.exe'
    $playerLog = Join-Path $runRoot 'benchmark-player.log'
    $revision = git -C $projectRoot rev-parse --short HEAD
    if (git -C $projectRoot status --porcelain) { $revision += '-dirty' }
    $player = Start-Process -FilePath $playerPath -ArgumentList "-batchmode -nographics -terrainBenchmark -benchmarkOutput `"$benchmarkOutput`" -benchmarkSoakSeconds $SoakSeconds -benchmarkRevision $revision -logFile `"$playerLog`"" -WindowStyle Hidden -PassThru
    $playerDeadline = [DateTime]::UtcNow.AddSeconds($SoakSeconds + 180)
    while (!$player.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $playerDeadline) {
            Stop-Process -Id $player.Id
            throw "Player benchmark timed out. See $playerLog"
        }
    }
    $failurePath = Join-Path $benchmarkOutput 'failure.txt'
    if (Test-Path -LiteralPath $failurePath) { Get-Content -LiteralPath $failurePath }
    if ($player.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $benchmarkOutput 'completed.txt'))) {
        throw "Player benchmark failed. See $playerLog"
    }
    Write-Output "Benchmark results: $benchmarkOutput"
}

if ($RenderedCheck) {
    $renderedOutput = Join-Path $runRoot 'RenderedCheck'
    $renderedLog = Join-Path $runRoot 'rendered-player.log'
    $renderedPlayerPath = Join-Path $runRoot 'Builds/Validation/DroneTerrainSystem.exe'
    $renderedPlayer = Start-Process -FilePath $renderedPlayerPath -ArgumentList "-terrainRenderedCheck -renderedOutput `"$renderedOutput`" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile `"$renderedLog`"" -WindowStyle Normal -PassThru
    $renderedDeadline = [DateTime]::UtcNow.AddMinutes(3)
    while (!$renderedPlayer.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $renderedDeadline) {
            Stop-Process -Id $renderedPlayer.Id
            throw "Rendered check timed out. See $renderedLog"
        }
    }
    if ($renderedPlayer.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $renderedOutput 'completed.txt'))) {
        throw "Rendered check failed. See $renderedLog"
    }
    Write-Output "Rendered check results: $renderedOutput"
}
