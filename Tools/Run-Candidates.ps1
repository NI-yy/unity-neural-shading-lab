param(
    [string]$Phase='screening',
    [string]$PlayerPath='',
    [string]$BuildInfoPath='',
    [string[]]$Cases=@('initial/reference','initial/hard-shadows','initial/low-shadows','initial/no-fog','initial/no-ssao','initial/no-post','alternate/reference','waterfall/reference'),
    [ValidateSet(1,3)][int]$Repeats=1,
    [switch]$CaptureOnly,
    [switch]$CaptureSynchronous,
    [switch]$ExportOnly,
    [switch]$UnityOnly,
    [switch]$NoExport,
    [ValidateRange(1,3)][int]$RunIndex=1
)
$ErrorActionPreference='Stop'
if($RunIndex+$Repeats -gt 4){throw 'RunIndex + Repeats must not exceed 4'}
$repo=Split-Path -Parent $PSScriptRoot
$player=if($PlayerPath){(Resolve-Path -LiteralPath $PlayerPath).Path}else{Join-Path $repo 'Builds\WindowsDevelopment\NeuralShadingLab.exe'}
$buildInfo=if($BuildInfoPath){(Resolve-Path -LiteralPath $BuildInfoPath).Path}else{Join-Path $repo 'UnityProject\Assets\NeuralShadingLab\Resources\NeuralLabBuildInfo.txt'}
$pm=Join-Path $repo 'LocalData\Tools\PresentMon-2.6.0-x64.exe'
$editor='C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Unity.exe'
$base=Join-Path $repo "Benchmarks\Candidates\$Phase"
$raw=Join-Path $repo "LocalData\Candidates\$Phase"
New-Item -ItemType Directory -Force -Path $base,$raw|Out-Null
if(-not $ExportOnly -and -not $CaptureOnly){
    Copy-Item $buildInfo (Join-Path $base 'build-info.txt')
    [ordered]@{utc=[DateTime]::UtcNow.ToString('O');player=$player;playerSha256=(Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash;cases=$Cases;repeats=$Repeats;independentTiming=(-not $UnityOnly);revision=(& git -c "safe.directory=$repo" -C $repo rev-parse HEAD);changes=@(& git -c "safe.directory=$repo" -C $repo status --porcelain)}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $base 'source-state.json')
    [ordered]@{utc=[DateTime]::UtcNow.ToString('O');player=$player;playerSha256=(Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash;cases=$Cases;runIndex=$RunIndex;repeats=$Repeats;independentTiming=(-not $UnityOnly)}|ConvertTo-Json -Compress|Add-Content (Join-Path $base 'run-order.jsonl')
}
foreach($case in $Cases){
    $split=$case.Split('/');if($split.Count -ne 2){throw "Expected view/variant: $case"};$view=$split[0];$variant=$split[1]
    $data=Join-Path $base "$view\$variant";$binary=Join-Path $raw "$view\$variant"
    New-Item -ItemType Directory -Force -Path $data,$binary|Out-Null
    if($CaptureOnly){
        $capture=Join-Path $repo "Benchmarks\Captures\Candidates\$Phase\$view\$variant"
        New-Item -ItemType Directory -Force -Path $capture|Out-Null
        $captureId=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
        $args=@('-neuralLabCapture','-candidate',$variant,'-candidateView',$view,'-neuralLabOutput',$capture,'-force-d3d12','-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',(Join-Path $capture "player-$captureId.log"))
        if($CaptureSynchronous){$args+='-neuralLabCaptureSynchronous'}
        $p=Start-Process -FilePath $player -ArgumentList $args -Wait -PassThru
        [ordered]@{playerExitCode=$p.ExitCode;log="player-$captureId.log";completedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $capture "execution-$captureId.json")
        if($p.ExitCode -ne 0){throw "Capture failed $case exit=$($p.ExitCode)"};Write-Output "captured=$case";continue
    }
    if(-not $ExportOnly){for($run=$RunIndex;$run -lt $RunIndex+$Repeats;$run++){
        $out=Join-Path $data "run-$run"
        if(Test-Path $out){throw "Refusing to overwrite existing run: $out"}
        New-Item -ItemType Directory -Path $out|Out-Null
        $args=@('-neuralLabBenchmark','-candidate',$variant,'-candidateView',$view,'-neuralLabGpuProfile','-neuralLabBinaryProfile',(Join-Path $binary "attached-repeat-$run"),'-neuralLabOutput',(Join-Path $out 'unity'),'-force-d3d12','-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-logFile',(Join-Path $out 'player.log'))
        $p=Start-Process -FilePath $player -ArgumentList $args -PassThru
        if(-not $UnityOnly){
        $session='Candidate-'+[Guid]::NewGuid().ToString('N').Substring(0,12)
        $pmArgs=@('--session_name',$session,'--process_id',$p.Id,'--output_file',(Join-Path $out 'presentmon.csv'),'--v2_metrics','--qpc_time_ms','--set_circular_buffer_size','32768','--timed','45','--terminate_after_timed','--terminate_on_proc_exit','--no_console_stats')
        $pmProc=Start-Process -FilePath $pm -ArgumentList $pmArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'presentmon.stdout.txt') -RedirectStandardError (Join-Path $out 'presentmon.stderr.txt')
        }
        $p.WaitForExit()
        [ordered]@{playerExitCode=$p.ExitCode;processId=$p.Id;independentTiming=(-not $UnityOnly);completedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out 'execution.json')
        if(-not $UnityOnly){
        $pmProc.WaitForExit(50000)|Out-Null
        if(-not $pmProc.HasExited -or $pmProc.ExitCode -ne 0){throw "PresentMon failed: $case run=$run"}
        if(-not(Test-Path (Join-Path $out 'presentmon.csv'))){throw "PresentMon did not record the target PID: $case run=$run"}
        $pmErrors=Get-Content (Join-Path $out 'presentmon.stderr.txt') -Raw
        if($pmErrors -match 'ETW events were lost|overflowed present events'){throw "PresentMon lost events: $case run=$run"}
        }
        if($p.ExitCode -notin @(0,2)){throw "Player failed: $case run=$run exit=$($p.ExitCode)"}
        if(-not(Test-Path (Join-Path $binary "attached-repeat-$run.raw"))){throw "Missing raw: $case run=$run"}
        Write-Output "measured=$case run=$run exit=$($p.ExitCode)"
    }}
}
if($CaptureOnly -or $NoExport){return}
foreach($case in $Cases){
    $split=$case.Split('/');$data=Join-Path $base "$($split[0])\$($split[1])";$binary=Join-Path $raw "$($split[0])\$($split[1])"
    $single=if($Repeats -eq 1){' -neuralLabIssue3SingleFile "'+(Join-Path $binary 'attached-repeat-1.raw')+'"'}else{''}
    $args='-batchmode -quit -projectPath "{0}" -executeMethod GpuRepeatExport.Export -neuralLabIssue3Input "{1}" -neuralLabIssue3AllFrames -neuralLabIssue3Output "{2}" -logFile "{3}"{4}' -f (Join-Path $repo 'UnityProject'),$binary,$data,(Join-Path $data 'editor-export.log'),$single
    $ed=Start-Process -FilePath $editor -ArgumentList $args -WindowStyle Hidden -Wait -PassThru
    if($ed.ExitCode -ne 0){throw "Export failed: $case exit=$($ed.ExitCode)"}
    Write-Output "exported=$case"
}
