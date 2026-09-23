# 合成输入只验证门禁拒绝能力；此处生成的文件绝不能当成生产 UI 证据归档。
$v5Directory = Join-Path $directory 'v5'
[void][IO.Directory]::CreateDirectory($v5Directory)
$v5MapPath = Join-Path $v5Directory 'map.json'
$v5Map = @{ schemaVersion=1; methods=@{ 'Fixture.Pass'=1 }; scenarios=@{}; scriptScenarios=@{U04='Assert-V5Assets'} }
foreach ($scenario in (Get-V5RequiredScenarios)) { $v5Map.scenarios[$scenario]=@('Fixture.Pass') }
function Save-V5Map { $v5Map | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $v5MapPath }
$m1Trx | Set-Content -LiteralPath $path; Save-V5Map
if ((Assert-V5TestMap $path $v5MapPath).scenarios -ne 30) { throw '正确 V5 映射被拒绝。' }; $checks++
foreach ($scenario in (Get-V5RequiredScenarios)) {
    $v5Map.scenarios.Remove($scenario); Save-V5Map
    Expect-Rejection { Assert-V5TestMap $path $v5MapPath }; $checks++
    $v5Map.scenarios[$scenario]=@('Fixture.Pass')
}
$v5Map.scriptScenarios.Clear(); Save-V5Map; Expect-Rejection { Assert-V5TestMap $path $v5MapPath }; $checks++
$v5Fixtures = @{
    'v5-artwork-budget.json' = @{ schemaVersion=1; realHost=$false; observedEntries=24; observedBytes=3145728; retainedEntriesAfterRelease=0; sharedLease=$true; oversizeRejected=$true }
    'v5-image-transfer.json' = @{ schemaVersion=1; realHost=$false; observedPeakRequests=2; pendingAfterCompletion=0; sharedDownloadCalls=1; consumerCancellationIsolated=$true }
    'v5-image-compressed.json' = @{ schemaVersion=1; realHost=$false; observedCachedBytes=8388608; oldestWasFetchedAgain=$true; oversizeRejected=$true }
    'ui-resource-observation.json' = @{ schemaVersion=1; realHost=$false; environment='fixture'; seconds=1; rounds=1; queueEntries=10000; lyricLines=1000;
        observations=@(foreach ($scenario in @('paused-visible','lyrics-visible','playing-hidden')) { @{round=0; scenario=$scenario; elapsedMs=1000; privateBytes=1000000; peakPrivateBytes=1000000; allocatedBytes=10; cpuOneCorePercent=1; visibleRows=0; navigationSamples=100; navigationPairP50Ms=10; navigationPairP95Ms=15} }) }
}
function Save-V5Evidence([string]$Name, [hashtable]$Data) {
    $Data.provenance=$evidenceContext.Clone()
    $Data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $v5Directory $Name)
}
foreach ($name in $v5Fixtures.Keys) { Save-V5Evidence $name $v5Fixtures[$name] }
foreach ($shot in (Get-V5Screenshots)) {
    $bitmap = [Drawing.Bitmap]::new($shot.width,$shot.height); $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.Clear([Drawing.Color]::White); $graphics.DrawLine([Drawing.Pens]::Blue,0,0,$shot.width-1,$shot.height-1); $bitmap.Save((Join-Path $v5Directory $shot.name),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    Save-V5Evidence ($shot.name+'.json') @{ schemaVersion=1; name=$shot.name; width=$shot.width; height=$shot.height; realHost=$false; sha256=(Get-FileHash -LiteralPath (Join-Path $v5Directory $shot.name)).Hash }
}
Assert-V5Artifacts $v5Directory $evidenceContext $started; $checks++
foreach ($name in $v5Fixtures.Keys) {
    $data = $v5Fixtures[$name]
    foreach ($field in @($data.Keys | Where-Object { $_ -ne 'provenance' })) {
        $value = $data[$field]; $data.Remove($field); Save-V5Evidence $name $data
        Expect-Rejection { Assert-V5Artifacts $v5Directory $evidenceContext $started }; $checks++
        $data[$field]=$value; Save-V5Evidence $name $data
    }
}
foreach ($case in @(@('v5-artwork-budget.json','observedEntries',25),@('v5-artwork-budget.json','observedBytes',16777217),
    @('v5-artwork-budget.json','retainedEntriesAfterRelease',1),@('v5-image-transfer.json','observedPeakRequests',3),
    @('v5-image-transfer.json','pendingAfterCompletion',1),@('v5-image-transfer.json','sharedDownloadCalls',2),
    @('v5-image-compressed.json','observedCachedBytes',8388609))) {
    $data=$v5Fixtures[$case[0]]; $old=$data[$case[1]]; $data[$case[1]]=$case[2]; Save-V5Evidence $case[0] $data
    Expect-Rejection { Assert-V5Artifacts $v5Directory $evidenceContext $started }; $checks++
    $data[$case[1]]=$old; Save-V5Evidence $case[0] $data
}
foreach ($field in @('runId','revision','sourceSha256')) {
    $context=$evidenceContext.Clone(); $context[$field]='old'
    Expect-Rejection { Assert-V5Artifacts $v5Directory $context $started }; $checks++
}
foreach ($name in @($v5Fixtures.Keys) + @((Get-V5Screenshots) | ForEach-Object name)) {
    $file=Join-Path $v5Directory $name; $content=[IO.File]::ReadAllBytes($file)
    Remove-Item -LiteralPath $file; Expect-Rejection { Assert-V5Artifacts $v5Directory $evidenceContext $started }; $checks++
    [IO.File]::WriteAllBytes($file,$content)
}
