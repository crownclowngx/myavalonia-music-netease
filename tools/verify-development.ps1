#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Login', 'M1', 'V3', 'V4', 'V5', 'V6', 'V7')][string]$Milestone = 'V7')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DevelopmentChecks.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = [datetime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$results = Join-Path $root "TestResults/NetEase$Milestone/$runId"
[void][IO.Directory]::CreateDirectory($results)
$previousArtifacts = $env:NETEASE_TEST_ARTIFACTS
$previousRunId = $env:NETEASE_TEST_RUN_ID
$previousRevision = $env:NETEASE_TEST_REVISION
$previousSource = $env:NETEASE_TEST_SOURCE_SHA256
try {
    $source = Get-DevelopmentSourceStamp $root
    $context = @{ runId=$runId; revision=$source.revision; sourceSha256=$source.sourceSha256 }
    Invoke-CheckedProcess 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-development-gate.ps1')) $root 60
    Invoke-CheckedProcess 'dotnet' @('restore', 'MusicNetEasePlugin.slnx', '--locked-mode') $root
    Invoke-CheckedProcess 'dotnet' @('build', 'MusicNetEasePlugin.slnx', '-c', 'Debug', '--no-restore', '-warnaserror') $root
    $started = [datetimeoffset]::UtcNow
    $env:NETEASE_TEST_ARTIFACTS = $results
    $env:NETEASE_TEST_RUN_ID = $runId
    $env:NETEASE_TEST_REVISION = $source.revision
    $env:NETEASE_TEST_SOURCE_SHA256 = $source.sourceSha256
    Invoke-CheckedProcess 'dotnet' @('test', 'tests/MusicNetEasePlugin.Tests/MusicNetEasePlugin.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=netease-login-development.trx', '--results-directory', $results) $root 180
    $passed = Assert-DevelopmentTrx (Join-Path $results 'netease-login-development.trx') $started $results
    $coverage = $null
    if ($Milestone -in @('M1','V3','V4','V5','V6','V7')) {
        $coverage = Assert-M1TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'm1-test-map.json')
        Assert-M1Artifacts $results
    }
    $uiCoverage = $null
    if ($Milestone -in @('V3','V4','V5','V6','V7')) {
        $uiCoverage = Assert-V3TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'v3-test-map.json')
        Assert-V3Artifacts $results
    }
    $m2Coverage = $null
    if ($Milestone -in @('V4','V5','V6','V7')) {
        $m2Coverage = Assert-M2TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'm2-test-map.json')
        Assert-M2Artifacts $results $context $started
    }
    $v5Coverage = $null; $v5Assets = $null
    if ($Milestone -in @('V5','V6','V7')) {
        $v5Coverage = Assert-V5TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'v5-test-map.json')
        Assert-V5Artifacts $results $context $started
        $v5Assets = Assert-V5Assets $root
    }
    $v6Coverage = $null
    if ($Milestone -in @('V6','V7')) {
        $v6Coverage = Assert-V6TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'v6-test-map.json')
        Assert-V6Artifacts $results $context $started
    }
    $v7Coverage = $null
    if ($Milestone -eq 'V7') {
        $v7Coverage = Assert-V7TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'v7-test-map.json')
        Assert-V7Artifacts $results $context $started
    }
    Assert-MarkdownLinks $root
    Invoke-CheckedProcess 'git' @('diff', '--check') $root
    $after = Get-DevelopmentSourceStamp $root
    Assert-DevelopmentSourceUnchanged $source $after
    $artifacts = @(Get-ChildItem -LiteralPath $results -File | Sort-Object Name | ForEach-Object { @{ name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    $native = if ($Milestone -in @('M1','V3','V4','V5','V6','V7')) { Get-Content -LiteralPath (Join-Path $results 'libvlc-offline.json') -Raw | ConvertFrom-Json } else { $null }
    @{ runId = $runId; revision = $source.revision; workingTreeDirty = $source.workingTreeDirty; sourceSha256=$source.sourceSha256; testsPassed = $passed;
        startedUtc = $started.ToString('o'); completedUtc = [datetimeoffset]::UtcNow.ToString('o');
        machine = [Runtime.InteropServices.RuntimeInformation]::OSDescription;
        milestone = $Milestone; scenarioCoverage = $coverage; uiScenarioCoverage = $uiCoverage; m2ScenarioCoverage=$m2Coverage; v5ScenarioCoverage=$v5Coverage; v6ScenarioCoverage=$v6Coverage; v7ScenarioCoverage=$v7Coverage; v5StaticAssets=$v5Assets; artifacts=$artifacts;
        runtime = $(if ($native) { @{ version=$native.ActiveVersion; directory=$native.ActiveDirectory } } else { $null });
        realAccountVerified = $false; audibleOutputVerified = $false; hostVerified = $false; deployed=$false; releaseGateExecuted = $false } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $results 'verification.json') -Encoding utf8
    Write-Host "本地开发门禁通过：$passed 项测试；证据目录 $results"
}
finally { $env:NETEASE_TEST_ARTIFACTS = $previousArtifacts; $env:NETEASE_TEST_RUN_ID=$previousRunId; $env:NETEASE_TEST_REVISION=$previousRevision; $env:NETEASE_TEST_SOURCE_SHA256=$previousSource }
