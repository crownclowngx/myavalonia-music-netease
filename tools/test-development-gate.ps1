#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DevelopmentChecks.ps1')
$ownedParent = Join-Path ([IO.Path]::GetTempPath()) 'MusicNetEasePlugin.GateTests'
$directory = Join-Path $ownedParent ([guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
$path = Join-Path $directory 'fixture.trx'
$started = [datetimeoffset]::UtcNow
$time = $started.ToString('o')
$valid = "<TestRun><Times start='$time' finish='$time'/><Results><UnitTestResult outcome='Passed'/></Results><ResultSummary outcome='Completed'><Counters total='1' executed='1' passed='1' failed='0' notExecuted='0'/></ResultSummary></TestRun>"
$checks = 0
function Expect-Rejection([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (!$rejected) { throw '门禁错误接受了失败输入。' }
}
try {
    $valid | Set-Content $path
    if ((Assert-DevelopmentTrx $path $started $directory) -ne 1) { throw '正确 TRX 被拒绝。' }
    $checks++
    $cases = @(
        $valid.Replace("total='1' executed='1' passed='1'", "total='0' executed='0' passed='0'"),
        $valid.Replace("passed='1' failed='0'", "passed='0' failed='1'"),
        $valid.Replace("executed='1' passed='1'", "executed='0' passed='0'").Replace("notExecuted='0'", "notExecuted='1'"),
        $valid.Replace("outcome='Completed'", "outcome='Aborted'"),
        $valid.Replace($time, $started.AddHours(-1).ToString('o')),
        $valid.Replace("<UnitTestResult outcome='Passed'/>", ''),
        '<TestRun><ResultSummary'
    )
    foreach ($fixture in $cases) {
        $fixture | Set-Content $path
        Expect-Rejection { Assert-DevelopmentTrx $path $started $directory }
        $checks++
    }
    Expect-Rejection { Assert-DevelopmentTrx (Join-Path $directory 'missing.trx') $started $directory }; $checks++
    $valid | Set-Content $path
    Expect-Rejection { Assert-DevelopmentTrx $path $started (Join-Path $directory 'other-run') }; $checks++
    Invoke-CheckedProcess 'pwsh' @('-NoProfile', '-Command', 'exit 0') $directory 10
    Expect-Rejection { Invoke-CheckedProcess 'pwsh' @('-NoProfile', '-Command', 'exit 7') $directory 10 }; $checks++
    Expect-Rejection { Invoke-CheckedProcess 'pwsh' @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30') $directory 1 }; $checks++
    [void][IO.Directory]::CreateDirectory((Join-Path $directory 'docs'))
    '# Notice' | Set-Content (Join-Path $directory 'THIRD-PARTY-NOTICES.md')
    "# 使用入口`n[细节](docs/detail.md#测试说明)" | Set-Content (Join-Path $directory 'README.md')
    '# 测试说明' | Set-Content (Join-Path $directory 'docs/detail.md')
    Assert-MarkdownLinks $directory
    '[错误](docs/detail.md#缺失)' | Set-Content (Join-Path $directory 'README.md')
    Expect-Rejection { Assert-MarkdownLinks $directory }; $checks++
    '[错误](missing.md)' | Set-Content (Join-Path $directory 'README.md')
    Expect-Rejection { Assert-MarkdownLinks $directory }; $checks++
    # 用完整场景表和真正 TRX 节点验证映射，不把“含 M1 标签”当作已执行。
    $m1Trx = "<TestRun><TestDefinitions><UnitTest id='fixture'><TestMethod className='Fixture' name='Pass'/></UnitTest></TestDefinitions><Results><UnitTestResult testId='fixture' outcome='Passed'/></Results></TestRun>"
    $m1MapPath = Join-Path $directory 'm1.json'
    $m1Map = @{ schemaVersion=1; methods=@{ 'Fixture.Pass'=1 }; scenarios=@{} }
    foreach ($group in @(@('P',6),@('A',6),@('C',6),@('B',7),@('L',9),@('E',7),@('T',9),@('U',8))) {
        foreach ($number in 1..$group[1]) { $m1Map.scenarios[('{0}{1:00}' -f $group[0],$number)] = @('Fixture.Pass') }
    }
    function Save-M1Map { $m1Map | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $m1MapPath }
    $m1Trx | Set-Content -LiteralPath $path; Save-M1Map
    if ((Assert-M1TestMap $path $m1MapPath).scenarios -ne 58) { throw '正确 M1 映射被拒绝。' }; $checks++
    foreach ($missing in @('P01', 'T09', 'U07', 'U08')) {
        $m1Map.scenarios.Remove($missing); Save-M1Map
        Expect-Rejection { Assert-M1TestMap $path $m1MapPath }; $checks++
        $m1Map.scenarios[$missing] = @('Fixture.Pass')
    }
    $m1Map.methods['Fixture.Pass']=2; Save-M1Map
    Expect-Rejection { Assert-M1TestMap $path $m1MapPath }; $checks++
    $m1Map.methods['Fixture.Pass']=1; $m1Map.scenarios['P01']=@('Fixture.Renamed'); Save-M1Map
    Expect-Rejection { Assert-M1TestMap $path $m1MapPath }; $checks++
    $m1Map.scenarios['P01']=@(); Save-M1Map
    Expect-Rejection { Assert-M1TestMap $path $m1MapPath }; $checks++
    $m1Map.scenarios['P01']=@('Fixture.Pass'); Save-M1Map
    $m1Trx.Replace("outcome='Passed'", "outcome='NotExecuted'") | Set-Content -LiteralPath $path
    Expect-Rejection { Assert-M1TestMap $path $m1MapPath }; $checks++
    $m1Trx.Replace("testId='fixture'", "testId='unmatched'") | Set-Content -LiteralPath $path
    Expect-Rejection { Assert-M1TestMap $path $m1MapPath }; $checks++
    $m1Map.methods.Clear(); Save-M1Map; $m1Trx | Set-Content -LiteralPath $path
    Expect-Rejection { Assert-M1TestMap $path $m1MapPath }; $checks++
    $nativePath = Join-Path $directory 'libvlc-offline.json'
    $native = @{ actualDeviceOutput=$false; ActiveVersion='fixture'; ActiveDirectory='fixture'; sample=@{sha256=('a'*64)};
        measurements=@(1..12 | ForEach-Object { @{decodedFrames=19200} }) }
    $native | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $nativePath
    $png = [byte[]]::new(1024); [Convert]::FromHexString('89504E470D0A1A0A').CopyTo($png,0)
    foreach ($name in @('m1-music-and-settings.png','m1-compact.png')) { [IO.File]::WriteAllBytes((Join-Path $directory $name),$png) }
    Assert-M1Artifacts $directory; $checks++
    $native.actualDeviceOutput=$true; $native | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $nativePath
    Expect-Rejection { Assert-M1Artifacts $directory }; $checks++
    $native.actualDeviceOutput=$false; $native.measurements[0].decodedFrames=0; $native | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $nativePath
    Expect-Rejection { Assert-M1Artifacts $directory }; $checks++
    $native.measurements[0].decodedFrames=19200; $native | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $nativePath
    [IO.File]::WriteAllBytes((Join-Path $directory 'm1-compact.png'),[byte[]]::new(0))
    Expect-Rejection { Assert-M1Artifacts $directory }; $checks++
    # V3 必测范围和证据缺项同样必须使门禁失败，避免界面测试提前退出却留下绿色 TRX。
    $v3MapPath = Join-Path $directory 'v3.json'
    $v3Map = @{ schemaVersion=1; methods=@{ 'Fixture.Pass'=1 }; scenarios=@{} }
    $v3Scenarios = @('THEME-AUTO','DOCUMENT-AUTO','TOOL-AUTO','LOGIN-AUTO','MOTION-AUTO','PREFERENCES-AUTO','RESOURCE-AUTO')
    foreach ($scenario in $v3Scenarios) { $v3Map.scenarios[$scenario] = @('Fixture.Pass') }
    function Save-V3Map { $v3Map | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $v3MapPath }
    $m1Trx | Set-Content -LiteralPath $path; Save-V3Map
    if ((Assert-V3TestMap $path $v3MapPath).scenarios -ne 7) { throw '正确 V3 映射被拒绝。' }; $checks++
    foreach ($scenario in $v3Scenarios) {
        $v3Map.scenarios.Remove($scenario); Save-V3Map
        Expect-Rejection { Assert-V3TestMap $path $v3MapPath }; $checks++
        $v3Map.scenarios[$scenario] = @('Fixture.Pass')
    }
    $v3Map.methods['Fixture.Pass']=2; Save-V3Map
    Expect-Rejection { Assert-V3TestMap $path $v3MapPath }; $checks++
    foreach ($name in (Get-V3ScreenshotNames)) { [IO.File]::WriteAllBytes((Join-Path $directory $name),$png) }
    $motionPath = Join-Path $directory 'v3-motion-observation.json'
    $motion = @{ schemaVersion=1; environment='fixture'; realHost=$false; frameTimeMeasured=$false; detachedViews=40; retainedViews=0;
        samples=@(foreach ($reduced in @($false,$true)) { foreach ($scenario in @('idle','playing','hidden')) {
            @{reducedMotion=$reduced; scenario=$scenario; wallMilliseconds=500; cpuMilliseconds=0; allocatedBytes=0; activeFadeAfter=$false}
        } }) }
    function Save-Motion { $motion | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $motionPath }
    Save-Motion; Assert-V3Artifacts $directory; $checks++
    foreach ($name in @('realHost','frameTimeMeasured')) {
        $motion[$name]=$true; Save-Motion; Expect-Rejection { Assert-V3Artifacts $directory }; $checks++; $motion[$name]=$false
    }
    $motion.retainedViews=1; Save-Motion; Expect-Rejection { Assert-V3Artifacts $directory }; $checks++; $motion.retainedViews=0
    $motion.samples[0].activeFadeAfter=$true; Save-Motion; Expect-Rejection { Assert-V3Artifacts $directory }; $checks++; $motion.samples[0].activeFadeAfter=$false
    $motion.samples[0].wallMilliseconds=1; Save-Motion; Expect-Rejection { Assert-V3Artifacts $directory }; $checks++; $motion.samples[0].wallMilliseconds=500
    $motion.samples[0].scenario='missing'; Save-Motion; Expect-Rejection { Assert-V3Artifacts $directory }; $checks++; $motion.samples[0].scenario='idle'; Save-Motion
    foreach ($name in (Get-V3ScreenshotNames)) {
        [IO.File]::WriteAllBytes((Join-Path $directory $name),[byte[]]::new(0))
        Expect-Rejection { Assert-V3Artifacts $directory }; $checks++
        [IO.File]::WriteAllBytes((Join-Path $directory $name),$png)
    }
    Remove-Item -LiteralPath $motionPath
    Expect-Rejection { Assert-V3Artifacts $directory }; $checks++
    . (Join-Path $PSScriptRoot 'TestM2DevelopmentGate.ps1')
    . (Join-Path $PSScriptRoot 'TestV5DevelopmentGate.ps1')
    . (Join-Path $PSScriptRoot 'TestV6DevelopmentGate.ps1')
    . (Join-Path $PSScriptRoot 'TestV7DevelopmentGate.ps1')
    Write-Host "门禁自测通过：$checks 项判定。"
}
finally {
    # 只递归清理本测试创建且校验过的 GUID 子目录，不能把仓库或系统临时根交给递归删除。
    $resolved = [IO.Path]::GetFullPath($directory)
    $prefix = [IO.Path]::GetFullPath($ownedParent) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw '自测清理路径越界。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
