#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Login', 'M1', 'V3')][string]$Milestone = 'V3')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DevelopmentChecks.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = [datetime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$results = Join-Path $root "TestResults/NetEase$Milestone/$runId"
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
    $coverage = $null
    if ($Milestone -in @('M1','V3')) {
        $coverage = Assert-M1TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'm1-test-map.json')
        Assert-M1Artifacts $results
    }
    $uiCoverage = $null
    if ($Milestone -eq 'V3') {
        $uiCoverage = Assert-V3TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'v3-test-map.json')
        Assert-V3Artifacts $results
    }
    Assert-MarkdownLinks $root
    Invoke-CheckedProcess 'git' @('diff', '--check') $root
    $revision = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法记录 Git revision。' }
    $dirty = [bool](& git -C $root status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw '无法记录工作树状态。' }
    @{ runId = $runId; revision = $revision; workingTreeDirty = $dirty; testsPassed = $passed;
        startedUtc = $started.ToString('o'); completedUtc = [datetimeoffset]::UtcNow.ToString('o');
        machine = [Runtime.InteropServices.RuntimeInformation]::OSDescription;
        milestone = $Milestone; scenarioCoverage = $coverage; uiScenarioCoverage = $uiCoverage;
        realAccountVerified = $false; audibleOutputVerified = $false; hostVerified = $false; releaseGateExecuted = $false } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $results 'verification.json') -Encoding utf8
    Write-Host "本地开发门禁通过：$passed 项测试；证据目录 $results"
}
finally { $env:NETEASE_TEST_ARTIFACTS = $previousArtifacts }
