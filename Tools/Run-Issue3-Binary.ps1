param(
    [Parameter(Mandatory=$true)][ValidateSet('reference','shadow4096')][string]$Condition,
    [string]$PresentMonPath,
    [string]$EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$base=Join-Path $repo "Benchmarks\Issue3\binary\$Condition"
$raw=Join-Path $repo "LocalData\Issue3Binary\$Condition"
$player=Join-Path $repo 'Builds\WindowsDevelopment\NeuralShadingLab.exe'
$pm=if($PresentMonPath){$PresentMonPath}else{Join-Path $repo 'LocalData\Tools\PresentMon-2.6.0-x64.exe'}
$editor=$EditorPath
New-Item -ItemType Directory -Force -Path $base,$raw | Out-Null
$build=Get-Content (Join-Path $repo 'UnityProject\Assets\NeuralShadingLab\Resources\NeuralLabBuildInfo.txt') -Raw
$expected=if ($Condition -eq 'reference') {'89d118024ad1b7b67baa1c4e88e53ad7'} else {'8f85983f8df686e99e506509fde1d48e'}
if ($build -notmatch "pipeline_dependency_hash=$expected") { throw "Wrong build for $Condition" }
for($run=1;$run -le 3;$run++) {
    $out=Join-Path $base "run-$run"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $pmArgs=@('--process_name','NeuralShadingLab.exe','--output_file',(Join-Path $out 'presentmon.csv'),'--v2_metrics','--qpc_time_ms','--set_circular_buffer_size','32768','--timed','45','--terminate_after_timed','--terminate_on_proc_exit','--no_console_stats')
    $pmProc=Start-Process -FilePath $pm -ArgumentList $pmArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $out 'presentmon.stdout.txt') -RedirectStandardError (Join-Path $out 'presentmon.stderr.txt')
    Start-Sleep -Seconds 2
    $args=@('-neuralLabBenchmark','-neuralLabGpuProfile','-neuralLabBinaryProfile',(Join-Path $raw "attached-repeat-$run"),'-neuralLabOutput',(Join-Path $out 'unity'),'-force-d3d12','-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',(Join-Path $out 'player.log'))
    $p=Start-Process -FilePath $player -ArgumentList $args -PassThru -Wait
    $pmProc.WaitForExit(50000) | Out-Null
    if (-not $pmProc.HasExited -or $pmProc.ExitCode -ne 0) { throw "PresentMon failed run $run" }
    if ($p.ExitCode -notin @(0,2)) { throw "Player failed run $run exit $($p.ExitCode)" }
    if (-not (Test-Path (Join-Path $raw "attached-repeat-$run.raw"))) { throw "Profiler raw missing run $run" }
    "run=$run player_exit=$($p.ExitCode) presentmon_exit=$($pmProc.ExitCode)"
}
$exportArgs='-batchmode -quit -projectPath "{0}" -executeMethod GpuRepeatExport.Export -neuralLabIssue3Input "{1}" -neuralLabIssue3AllFrames -neuralLabIssue3Output "{2}" -logFile "{3}"' -f (Join-Path $repo 'UnityProject'),$raw,$base,(Join-Path $base 'editor-export.log')
$ed=Start-Process -FilePath $editor -ArgumentList $exportArgs -PassThru -Wait -WindowStyle Hidden
if ($ed.ExitCode -ne 0) { throw "Profiler export failed exit $($ed.ExitCode)" }
"exported=$Condition"
