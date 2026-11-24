param(
    [string]$EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Unity.exe',
    [switch]$Build,
    [switch]$BuildOnly,
    [switch]$Development,
    [switch]$CaptureOnly,
    [ValidateSet('d3d11','d3d12')][string]$GraphicsApi = 'd3d12',
    [ValidateRange(1, 10)][int]$Repeats = 3
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoPath 'UnityProject'
$logsPath = Join-Path $repoPath 'LocalData\Logs'
$rawPath = Join-Path $repoPath $(if ($CaptureOnly) { 'Benchmarks\Captures' } else { 'Benchmarks\Raw' })
New-Item -ItemType Directory -Force -Path $logsPath,$rawPath | Out-Null
if ($Build) {
    if (-not (Test-Path -LiteralPath $EditorPath)) { throw "Unity Editor not found: $EditorPath" }
    $extra = if ($Development) { ' -neuralLabDevelopment' } else { '' }
    $arguments = '-batchmode -quit -projectPath "{0}" -executeMethod NeuralLabProjectSetup.BuildBaseline -logFile "{1}"{2}' -f $projectPath,(Join-Path $logsPath 'build.log'),$extra
    $buildProcess = Start-Process -FilePath $EditorPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($buildProcess.ExitCode -ne 0) { throw "Build failed. See $logsPath\build.log" }
}
$buildFolder = if ($Development) { 'WindowsDevelopment' } else { 'Windows' }
$playerPath = Join-Path $repoPath "Builds\$buildFolder\NeuralShadingLab.exe"
if ($BuildOnly) {
    if (-not $Build) { throw '-BuildOnly requires -Build.' }
    Write-Host "Built Player: $playerPath"
    return
}
if (-not (Test-Path -LiteralPath $playerPath)) { throw 'Player not built. Close Unity Editor and run this script with -Build.' }
$sourceState = [ordered]@{ recordedUtc = [DateTime]::UtcNow.ToString('O'); revision = (& git -C $repoPath rev-parse HEAD); changes = @(& git -C $repoPath status --porcelain) }
$sourceState | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $rawPath 'source-state.json') -Encoding utf8
Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $rawPath 'display-drivers.json') -Encoding utf8
for ($run = 1; $run -le $Repeats; $run++) {
    Write-Host "Baseline run $run / $Repeats"
    $logPath = Join-Path $logsPath "player-$run.log"
    $mode = if ($CaptureOnly) { '-neuralLabCapture' } else { '-neuralLabBenchmark' }
    $arguments = '{0} -neuralLabOutput "{1}" -force-{2} -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "{3}"' -f $mode,$rawPath,$GraphicsApi,$logPath
    # A visible Player is required for graphics timing. Do not use -nographics or -batchmode.
    $playerProcess = Start-Process -FilePath $playerPath -ArgumentList $arguments -Wait -PassThru
    if ($playerProcess.ExitCode -ne 0) { throw "Run $run failed or collected incomplete GPU timings. See $logPath" }
}
Write-Host "Completed. Raw data and per-run summaries: $rawPath"
