param(
    [Parameter(Mandatory=$true)][ValidateSet('reference','shadow4096')][string]$Condition,
    [Parameter(Mandatory=$true)][string]$PresentMonPath
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$player=Join-Path $repo 'Builds\WindowsDevelopment\NeuralShadingLab.exe'
$base=Join-Path $repo "Benchmarks\Issue3\$Condition"
if (-not (Test-Path -LiteralPath $PresentMonPath)) { throw "Missing PresentMon: $PresentMonPath" }
if (-not (Test-Path -LiteralPath $player)) { throw "Missing Development Player: $player" }
if (Get-Process NeuralShadingLab,Unity -ErrorAction SilentlyContinue) { throw 'Close other Unity processes first.' }
for ($n=1; $n -le 6; $n++) {
    $mode=if ($n % 2) { 'off' } else { 'on' }
    $repeat=[int][Math]::Ceiling($n/2)
    $out=Join-Path $base "$mode-$repeat"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $pmArgs=@('--process_name','NeuralShadingLab.exe','--output_file',(Join-Path $out 'presentmon.csv'),'--v2_metrics','--qpc_time_ms','--set_circular_buffer_size','32768','--timed','45','--terminate_after_timed','--terminate_on_proc_exit','--no_console_stats')
    $pm=Start-Process -FilePath $PresentMonPath -ArgumentList $pmArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $out 'presentmon.stdout.txt') -RedirectStandardError (Join-Path $out 'presentmon.stderr.txt')
    Start-Sleep -Seconds 2
    $args=@('-neuralLabBenchmark','-neuralLabOutput',(Join-Path $out 'unity'),'-force-d3d12','-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',(Join-Path $out 'player.log'))
    if ($mode -eq 'on') { $args += '-neuralLabGpuProfile' }
    $p=Start-Process -FilePath $player -ArgumentList $args -PassThru -Wait
    $pm.WaitForExit(50000) | Out-Null
    if (-not $pm.HasExited -or $pm.ExitCode -ne 0) { throw "PresentMon failed in $out" }
    # Unity exit code 2 denotes missing FrameTimingManager values; raw files remain valid.
    if ($p.ExitCode -notin @(0,2)) { throw "Player failed in $out : $($p.ExitCode)" }
    Write-Host "$Condition $mode $repeat Player=$($p.ExitCode) PresentMon=$($pm.ExitCode)"
}
