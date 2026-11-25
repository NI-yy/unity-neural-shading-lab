param(
    [string]$Suffix='20261107',
    [ValidateSet('Measure','Capture','Export')][string]$Mode='Measure'
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$cases=@('initial/reference','shadow-stress/reference')
$pcf=Join-Path $repo 'Builds/PCF-Baseline-20261107/NeuralShadingLab.exe'
$pcfInfo=Join-Path $repo 'Benchmarks/Candidates/pcf-baseline-20261107/build-info.txt'
if($Mode -eq 'Measure'){
    for($round=1;$round -le 3;$round++){
        $order=if($round -eq 2){@('PCSS','PCF')}else{@('PCF','PCSS')}
        foreach($algorithm in $order){
            $options=@{Phase=$algorithm.ToLower()+"-comparison-$Suffix";Cases=$cases;RunIndex=$round;UnityOnly=$true;NoExport=$true}
            if($algorithm -eq 'PCF'){$options.PlayerPath=$pcf;$options.BuildInfoPath=$pcfInfo}
            & "$PSScriptRoot/Run-Candidates.ps1" @options
        }
    }
}else{
    foreach($algorithm in @('PCF','PCSS')){
        $options=@{Phase=$algorithm.ToLower()+"-comparison-$Suffix";Cases=$cases}
        if($algorithm -eq 'PCF'){$options.PlayerPath=$pcf;$options.BuildInfoPath=$pcfInfo}
        if($Mode -eq 'Capture'){$options.CaptureOnly=$true;$options.CaptureSynchronous=$true}
        else{$options.ExportOnly=$true;$options.Repeats=3}
        & "$PSScriptRoot/Run-Candidates.ps1" @options
    }
}
