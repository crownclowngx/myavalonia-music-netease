# 合成证据只用于门禁失败注入；临时目录结束后清理，不会进入产品验收产物。
$v6Directory = Join-Path $directory 'v6'
[void][IO.Directory]::CreateDirectory($v6Directory)
$v6MapPath = Join-Path $v6Directory 'map.json'
$v6Map = @{ schemaVersion=1; methods=@{ 'Fixture.Pass'=1 }; scenarios=@{} }
foreach ($scenario in (Get-V6RequiredScenarios)) { $v6Map.scenarios[$scenario]=@('Fixture.Pass') }
function Save-V6Map { $v6Map | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $v6MapPath }
$m1Trx | Set-Content -LiteralPath $path; Save-V6Map
if ((Assert-V6TestMap $path $v6MapPath).scenarios -ne 15) { throw '正确 V6 映射被拒绝。' }; $checks++
foreach ($scenario in (Get-V6RequiredScenarios)) {
    $v6Map.scenarios.Remove($scenario); Save-V6Map
    Expect-Rejection { Assert-V6TestMap $path $v6MapPath }; $checks++
    $v6Map.scenarios[$scenario]=@('Fixture.Pass')
}
$v6Map.scenarios.N01=@('Fixture.NeverExecuted'); Save-V6Map; Expect-Rejection { Assert-V6TestMap $path $v6MapPath }; $checks++
$v6Fixtures = @{
    'v6-interaction-ui.json' = @{ schemaVersion=1; realHost=$false; CenterDeviation=0; CompactHeight=120; AudioOpened=1; ManualFollowRestored=$true }
    'v6-queue-gesture.json' = @{ schemaVersion=1; realHost=$false; Items=1001; CommitsOnRelease=1; EdgeScrollOffset=36; TimerStopped=$true; Reattachments=20; AudioOpened=0 }
}
function Save-V6Evidence([string]$Name, [hashtable]$Data) {
    $Data.provenance=$evidenceContext.Clone()
    $Data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $v6Directory $Name)
}
foreach ($name in $v6Fixtures.Keys) { Save-V6Evidence $name $v6Fixtures[$name] }
foreach ($shot in (Get-V6Screenshots)) {
    $bitmap=[Drawing.Bitmap]::new($shot.width,$shot.height); $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.Clear([Drawing.Color]::White); $graphics.DrawLine([Drawing.Pens]::Blue,0,0,$shot.width-1,$shot.height-1); $bitmap.Save((Join-Path $v6Directory $shot.name),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    Save-V6Evidence ($shot.name+'.json') @{ schemaVersion=1; name=$shot.name; width=$shot.width; height=$shot.height; realHost=$false; sha256=(Get-FileHash -LiteralPath (Join-Path $v6Directory $shot.name)).Hash }
}
Assert-V6Artifacts $v6Directory $evidenceContext $started; $checks++
foreach ($name in $v6Fixtures.Keys) {
    $data=$v6Fixtures[$name]
    foreach ($field in @($data.Keys | Where-Object { $_ -ne 'provenance' })) {
        $value=$data[$field]; $data.Remove($field); Save-V6Evidence $name $data
        Expect-Rejection { Assert-V6Artifacts $v6Directory $evidenceContext $started }; $checks++
        $data[$field]=$value; Save-V6Evidence $name $data
    }
}
foreach ($case in @(@('v6-interaction-ui.json','CenterDeviation',6),@('v6-interaction-ui.json','CompactHeight',141),
    @('v6-interaction-ui.json','AudioOpened',2),@('v6-interaction-ui.json','ManualFollowRestored',$false),
    @('v6-queue-gesture.json','CommitsOnRelease',2),@('v6-queue-gesture.json','EdgeScrollOffset',0),
    @('v6-queue-gesture.json','TimerStopped',$false),@('v6-queue-gesture.json','Reattachments',19),@('v6-queue-gesture.json','AudioOpened',1))) {
    $data=$v6Fixtures[$case[0]]; $old=$data[$case[1]]; $data[$case[1]]=$case[2]; Save-V6Evidence $case[0] $data
    Expect-Rejection { Assert-V6Artifacts $v6Directory $evidenceContext $started }; $checks++
    $data[$case[1]]=$old; Save-V6Evidence $case[0] $data
}
foreach ($field in @('runId','revision','sourceSha256')) {
    $context=$evidenceContext.Clone(); $context[$field]='old'
    Expect-Rejection { Assert-V6Artifacts $v6Directory $context $started }; $checks++
}
foreach ($name in @($v6Fixtures.Keys) + @((Get-V6Screenshots) | ForEach-Object name)) {
    $file=Join-Path $v6Directory $name; $content=[IO.File]::ReadAllBytes($file)
    Remove-Item -LiteralPath $file; Expect-Rejection { Assert-V6Artifacts $v6Directory $evidenceContext $started }; $checks++
    [IO.File]::WriteAllBytes($file,$content)
}
$staleFile=Get-Item -LiteralPath (Join-Path $v6Directory 'v6-queue-gesture.json'); $staleFile.LastWriteTimeUtc=$started.UtcDateTime.AddMinutes(-5)
Expect-Rejection { Assert-V6Artifacts $v6Directory $evidenceContext $started }; $checks++
