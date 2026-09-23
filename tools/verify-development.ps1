#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Login', 'M1', 'V3', 'V4')][string]$Milestone = 'V4')
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
    if ($Milestone -in @('M1','V3','V4')) {
        $coverage = Assert-M1TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'm1-test-map.json')
        Assert-M1Artifacts $results
    }
    $uiCoverage = $null
    if ($Milestone -in @('V3','V4')) {
        $uiCoverage = Assert-V3TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'v3-test-map.json')
        Assert-V3Artifacts $results
    }
    $m2Coverage = $null
    if ($Milestone -eq 'V4') {
        $m2Coverage = Assert-M2TestMap (Join-Path $results 'netease-login-development.trx') (Join-Path $PSScriptRoot 'm2-test-map.json')
        Assert-M2Artifacts $results $context $started
    }
    Assert-MarkdownLinks $root
    Invoke-CheckedProcess 'git' @('diff', '--check') $root
    $after = Get-DevelopmentSourceStamp $root
    if ($after.revision -ne $source.revision -or $after.sourceSha256 -ne $source.sourceSha256) { throw '验证期间源码变化，请对最终代码重新运行。' }
    $artifacts = @(Get-ChildItem -LiteralPath $results -File | Sort-Object Name | ForEach-Object { @{ name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    $native = if ($Milestone -in @('M1','V3','V4')) { Get-Content -LiteralPath (Join-Path $results 'libvlc-offline.json') -Raw | ConvertFrom-Json } else { $null }
    @{ runId = $runId; revision = $source.revision; workingTreeDirty = $source.workingTreeDirty; sourceSha256=$source.sourceSha256; testsPassed = $passed;
        startedUtc = $started.ToString('o'); completedUtc = [datetimeoffset]::UtcNow.ToString('o');
        machine = [Runtime.InteropServices.RuntimeInformation]::OSDescription;
        milestone = $Milestone; scenarioCoverage = $coverage; uiScenarioCoverage = $uiCoverage; m2ScenarioCoverage=$m2Coverage; artifacts=$artifacts;
        runtime = $(if ($native) { @{ version=$native.ActiveVersion; directory=$native.ActiveDirectory } } else { $null });
        realAccountVerified = $false; audibleOutputVerified = $false; hostVerified = $false; deployed=$false; releaseGateExecuted = $false } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $results 'verification.json') -Encoding utf8
    Write-Host "本地开发门禁通过：$passed 项测试；证据目录 $results"
}
finally { $env:NETEASE_TEST_ARTIFACTS = $previousArtifacts; $env:NETEASE_TEST_RUN_ID=$previousRunId; $env:NETEASE_TEST_REVISION=$previousRevision; $env:NETEASE_TEST_SOURCE_SHA256=$previousSource }
