param(
    [string]$Phase='pcf-baseline-20261107',
    [ValidateSet('All','Measure','Capture','Export')][string]$Mode='All'
)
$ErrorActionPreference='Stop'
if($Phase -notmatch '^[a-zA-Z0-9_-]+$'){throw 'Phase must be a simple directory name'}
$repo=Split-Path -Parent $PSScriptRoot
$data=Join-Path $repo "Benchmarks\Candidates\$Phase"
$asset=Join-Path $repo 'UnityProject\Assets\NeuralShadingLab\LocalSample\BaselineDesktop.asset'
$embedded=Join-Path $repo 'UnityProject\Packages\com.unity.render-pipelines.universal'
$urp=if(Test-Path $embedded){Get-Item -LiteralPath $embedded}else{Get-ChildItem (Join-Path $repo 'UnityProject\Library\PackageCache') -Directory -Filter 'com.unity.render-pipelines.universal@*' | Select-Object -First 1}
$shader=Join-Path $urp.FullName 'ShaderLibrary\Shadows.hlsl'
$expected='CAA3E10C26476CD44CBAF7EADCA308A2417BD42CC18A8A4C1466466E2AF9173D'
$shaderHash=(Get-FileHash $shader -Algorithm SHA256).Hash
if($shaderHash -ne $expected){throw 'URP Shadows.hlsl differs from the verified PCF baseline; use a separate comparison phase'}
$settings=Get-Content $asset -Raw
foreach($setting in @('m_SoftShadowsSupported:\s+1','m_SoftShadowQuality:\s+3','m_MainLightShadowmapResolution:\s+8192','m_ShadowCascadeCount:\s+4')){if($settings -notmatch $setting){throw "PCF baseline setting differs: $setting"}}
$assembly=Join-Path $repo 'Builds\WindowsDevelopment\NeuralShadingLab_Data\Managed\Assembly-CSharp.dll'
$assemblyHash=(Get-FileHash $assembly -Algorithm SHA256).Hash
New-Item -ItemType Directory -Force -Path $data|Out-Null
$manifestPath=Join-Path $data 'filter-manifest.json'
if(Test-Path $manifestPath){$previous=Get-Content $manifestPath -Raw|ConvertFrom-Json;if($previous.assemblySha256 -ne $assemblyHash){throw 'Existing phase uses a different Player build; choose a new Phase'}}
$manifest=[ordered]@{
    algorithm='URP PCF';quality='High';filter='7x7 tent';hardwareComparisonSamples=16
    mainLightShadowResolution=8192;cascadeCount=4;width=1920;height=1080;renderScale=1
    graphicsApi='Direct3D12';buildType='Development Mono x64';gpuTiming='Player-side GPU Usage binary log; Editor disconnected'
    independentTiming=$false;warmupFrames=300;samplesPerRun=1000;repeats=3;simulationRate=60
    captureMethod='CaptureScreenshotAsTexture + EncodeToPNG at end-of-frame, separate Player run'
    shaderPath=$shader;shaderSha256=$shaderHash;assemblySha256=$assemblyHash
    pipelineSha256=(Get-FileHash $asset -Algorithm SHA256).Hash
    buildInfo=(Get-Content (Join-Path $repo 'UnityProject\Assets\NeuralShadingLab\Resources\NeuralLabBuildInfo.txt') -Raw)
    views=@('initial','shadow-stress')
}
$manifest|ConvertTo-Json -Depth 4|Set-Content $manifestPath
$backup=Join-Path $repo 'LocalData\ShadowComparison\PCF'
New-Item -ItemType Directory -Force -Path $backup|Out-Null
if(-not(Test-Path (Join-Path $backup 'Shadows.original.hlsl'))){Copy-Item $shader (Join-Path $backup 'Shadows.original.hlsl')}
$cases=@('initial/reference','shadow-stress/reference')
if($Mode -in @('All','Measure')){
    for($round=1;$round -le 3;$round++){
        $order=@($cases);if($round -eq 2){[Array]::Reverse($order)}
        foreach($case in $order){& (Join-Path $PSScriptRoot 'Run-Candidates.ps1') -Phase $Phase -UnityOnly -NoExport -Cases $case -RunIndex $round}
    }
}
if($Mode -in @('All','Measure','Export')){
    & (Join-Path $PSScriptRoot 'Run-Candidates.ps1') -Phase $Phase -ExportOnly -Repeats 3 -Cases $cases
    & python (Join-Path $PSScriptRoot 'Summarize-Candidates.py') --phase $Phase
    if($LASTEXITCODE -ne 0){throw 'Summary failed'}
}
if($Mode -in @('All','Capture')){
    $failed=@()
    foreach($case in $cases){try{& (Join-Path $PSScriptRoot 'Run-Candidates.ps1') -Phase $Phase -CaptureOnly -CaptureSynchronous -Cases $case}catch{$failed+=$case;Write-Output "capture_failed=$case"}}
    if($failed.Count -gt 0){throw "Capture failed: $($failed -join ',')"}
}
Write-Output "pcf_baseline=$data mode=$Mode"
