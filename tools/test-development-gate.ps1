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
    Write-Host "门禁自测通过：$checks 项判定。"
}
finally {
    # 只递归清理本测试创建且校验过的 GUID 子目录，不能把仓库或系统临时根交给递归删除。
    $resolved = [IO.Path]::GetFullPath($directory)
    $prefix = [IO.Path]::GetFullPath($ownedParent) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw '自测清理路径越界。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
