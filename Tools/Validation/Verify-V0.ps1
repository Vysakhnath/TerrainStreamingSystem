param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.3.12f1/Editor/Unity.exe'
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

