#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DevelopmentChecks.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = [datetime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$results = Join-Path $root "TestResults/NetEaseLogin/$runId"
[void][IO.Directory]::CreateDirectory($results)
$previousArtifacts = $env:NETEASE_TEST_ARTIFACTS
try {
    Invoke-CheckedProcess 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-development-gate.ps1')) $root 60
    Invoke-CheckedProcess 'dotnet' @('restore', 'MusicNetEasePlugin.slnx', '--locked-mode') $root
    Invoke-CheckedProcess 'dotnet' @('build', 'MusicNetEasePlugin.slnx', '-c', 'Debug', '--no-restore', '-warnaserror') $root
    $started = [datetimeoffset]::UtcNow
    $env:NETEASE_TEST_ARTIFACTS = $results
    Invoke-CheckedProcess 'dotnet' @('test', 'tests/MusicNetEasePlugin.Tests/MusicNetEasePlugin.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=netease-login-development.trx', '--results-directory', $results) $root 180
    $passed = Assert-DevelopmentTrx (Join-Path $results 'netease-login-development.trx') $started $results
    Assert-MarkdownLinks $root
    Invoke-CheckedProcess 'git' @('diff', '--check') $root
    $revision = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法记录 Git revision。' }
    $dirty = [bool](& git -C $root status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw '无法记录工作树状态。' }
    @{ runId = $runId; revision = $revision; workingTreeDirty = $dirty; testsPassed = $passed;
        startedUtc = $started.ToString('o'); completedUtc = [datetimeoffset]::UtcNow.ToString('o');
        machine = [Runtime.InteropServices.RuntimeInformation]::OSDescription;
        realAccountVerified = $false; hostVerified = $false; releaseGateExecuted = $false } |
        ConvertTo-Json | Set-Content (Join-Path $results 'verification.json') -Encoding utf8
    Write-Host "本地开发门禁通过：$passed 项测试；证据目录 $results"
}
finally { $env:NETEASE_TEST_ARTIFACTS = $previousArtifacts }
