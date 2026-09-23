# 由 test-development-gate.ps1 点入，共享受控临时目录和拒绝断言。此处只有合成失败输入，不是业务验证证据。
$m2Directory = Join-Path $directory 'm2'
[void][IO.Directory]::CreateDirectory($m2Directory)
$m2MapPath = Join-Path $m2Directory 'map.json'
$m2Map = @{ schemaVersion=1; methods=@{ 'Fixture.Pass'=1 }; scenarios=@{} }
foreach ($scenario in (Get-M2RequiredScenarios)) { $m2Map.scenarios[$scenario]=@('Fixture.Pass') }
function Save-M2Map { $m2Map | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $m2MapPath }
$m1Trx | Set-Content -LiteralPath $path; Save-M2Map
if ((Assert-M2TestMap $path $m2MapPath).scenarios -ne 58) { throw '正确 M2 映射被拒绝。' }; $checks++
foreach ($scenario in (Get-M2RequiredScenarios)) {
    $m2Map.scenarios.Remove($scenario); Save-M2Map
    Expect-Rejection { Assert-M2TestMap $path $m2MapPath }; $checks++
    $m2Map.scenarios[$scenario]=@('Fixture.Pass')
}
$m2Map.methods['Fixture.Pass']=2; Save-M2Map; Expect-Rejection { Assert-M2TestMap $path $m2MapPath }; $checks++
$m2Map.methods['Fixture.Pass']=1; $m2Map.scenarios['H08']=@(); Save-M2Map; Expect-Rejection { Assert-M2TestMap $path $m2MapPath }; $checks++
$m2Map.scenarios['H08']=@('Fixture.Missing'); Save-M2Map; Expect-Rejection { Assert-M2TestMap $path $m2MapPath }; $checks++
$m2Map.methods.Clear(); Save-M2Map; Expect-Rejection { Assert-M2TestMap $path $m2MapPath }; $checks++

$evidenceContext = @{ runId='current-test-run'; revision=('b'*40); sourceSha256=('c'*64) }
$nativeFixtures = @{
    'm2-native-seek.json' = @{ schemaVersion=1; ActiveVersion='fixture'; sampleSha256=('a'*64); startPositionMs=4000; PositionMs=1000; forwardPositionMs=4500;
        firstOutputMatchesStart=$true; pausedAfterSeek=$true; fileReleased=$true; actualDeviceOutput=$false; realHost=$false }
    'm2-native-queue.json' = @{ schemaVersion=1; tracks=3; decodedFrames=100000; filesReleased=$true; actualDeviceOutput=$false; realHost=$false }
    'm2-lifetime.json' = @{ schemaVersion=1; environment='fixture'; realHost=$false; mountedDocuments=22; detachedViews=20; retainedViews=0; audioOpens=1; stoppedAfterShutdown=$true; noAutomaticResume=$true }
}
foreach ($target in @(4000,12000)) {
    $nativeFixtures["m2-native-restore-$target.json"] = @{ schemaVersion=1; target=$target; reset=($target -eq 12000); ActiveVersion='fixture'; sampleSha256=('a'*64);
        initializedWithoutEngine=$true; firstOutputMatchesStart=$true; decodedFrames=1200; fileReleased=$true; actualDeviceOutput=$false; realHost=$false }
}
function Save-Evidence([string]$Name, [hashtable]$Data) {
    $Data.provenance=$evidenceContext.Clone()
    $Data | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $m2Directory $Name)
}
foreach ($name in $nativeFixtures.Keys) { Save-Evidence $name $nativeFixtures[$name] }
foreach ($shot in (Get-M2Screenshots)) {
    $bitmap = [Drawing.Bitmap]::new($shot.width,$shot.height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.Clear([Drawing.Color]::White); $graphics.DrawLine([Drawing.Pens]::Blue,0,0,$shot.width-1,$shot.height-1); $bitmap.Save((Join-Path $m2Directory $shot.name),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    Save-Evidence ($shot.name+'.json') @{ schemaVersion=1; name=$shot.name; width=$shot.width; height=$shot.height; realHost=$false; sha256=(Get-FileHash -LiteralPath (Join-Path $m2Directory $shot.name)).Hash }
}
Assert-M2Artifacts $m2Directory $evidenceContext $started; $checks++
foreach ($case in @(
    @('m2-native-seek.json','PositionMs',3000), @('m2-native-seek.json','forwardPositionMs',9000), @('m2-native-seek.json','sampleSha256','bad'),
    @('m2-native-seek.json','firstOutputMatchesStart',$false), @('m2-native-seek.json','pausedAfterSeek',$false), @('m2-native-seek.json','fileReleased',$false),
    @('m2-native-restore-4000.json','initializedWithoutEngine',$false), @('m2-native-restore-4000.json','decodedFrames',0), @('m2-native-restore-12000.json','reset',$false),
    @('m2-native-restore-4000.json','firstOutputMatchesStart',$false), @('m2-native-restore-4000.json','actualDeviceOutput',$true),
    @('m2-native-queue.json','tracks',2), @('m2-native-queue.json','decodedFrames',0), @('m2-lifetime.json','retainedViews',1),
    @('m2-lifetime.json','audioOpens',22), @('m2-lifetime.json','stoppedAfterShutdown',$false), @('m2-lifetime.json','realHost',$true))) {
    $data = $nativeFixtures[$case[0]]; $original=$data[$case[1]]; $data[$case[1]]=$case[2]; Save-Evidence $case[0] $data
    Expect-Rejection { Assert-M2Artifacts $m2Directory $evidenceContext $started }; $checks++
    $data[$case[1]]=$original; Save-Evidence $case[0] $data
}
foreach ($field in @('runId','revision','sourceSha256')) {
    $oldContext=$evidenceContext.Clone(); $oldContext[$field]='old'
    Expect-Rejection { Assert-M2Artifacts $m2Directory $oldContext $started }; $checks++
}
foreach ($name in @($nativeFixtures.Keys) + @((Get-M2Screenshots) | ForEach-Object name)) {
    $file=Join-Path $m2Directory $name; $bytes=[IO.File]::ReadAllBytes($file)
    Remove-Item -LiteralPath $file; Expect-Rejection { Assert-M2Artifacts $m2Directory $evidenceContext $started }; $checks++
    [IO.File]::WriteAllBytes($file,$bytes)
}
$shotPath=Join-Path $m2Directory 'm2-document-light-520.png'; $realPng=[IO.File]::ReadAllBytes($shotPath)
[IO.File]::WriteAllBytes($shotPath,$png); Expect-Rejection { Assert-M2Artifacts $m2Directory $evidenceContext $started }; $checks++
[IO.File]::WriteAllBytes($shotPath,$realPng)
Expect-Rejection { Assert-RenderedPng $shotPath 800 600 }; $checks++
[IO.File]::SetLastWriteTimeUtc($shotPath,$started.UtcDateTime.AddHours(-1)); Expect-Rejection { Assert-M2Artifacts $m2Directory $evidenceContext $started }; $checks++
[IO.File]::SetLastWriteTimeUtc($shotPath,[datetime]::UtcNow)
$realPng[100] = $realPng[100] -bxor 1; [IO.File]::WriteAllBytes($shotPath,$realPng)
Expect-Rejection { Assert-M2Artifacts $m2Directory $evidenceContext $started }; $checks++
foreach ($invalidTrx in @($valid.Replace("<Times start='$time' finish='$time'/>",''), '<!DOCTYPE TestRun [<!ENTITY x SYSTEM "file:///missing">]><TestRun>&x;</TestRun>')) {
    $invalidTrx | Set-Content -LiteralPath $path; Expect-Rejection { Assert-DevelopmentTrx $path $started $directory }; $checks++
}
