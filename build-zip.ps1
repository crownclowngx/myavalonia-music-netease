#requires -Version 7.0
<#
.SYNOPSIS
在项目根目录生成不包含 libVLC 原生运行库的插件 ZIP。
.EXAMPLE
pwsh -NoProfile -File ./build-zip.ps1
.EXAMPLE
pwsh -NoProfile -File ./build-zip.ps1 -Configuration Release -OutputDirectory ./artifacts/packages
.NOTES
默认 Debug，仅构建本地安装包，不调用测试 / 发布门禁，也不部署到 Host。
使用 Build 包的资产筛选、目录布局和摘要清单，不直接压缩普通 bin 目录。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # 相对路径以脚本所在的项目根目录为准；每次生成独立子目录，避免覆盖上次结果。
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory = './artifacts/zip'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj'
if (!(Test-Path -LiteralPath $project -PathType Leaf)) { throw "未找到插件项目：$project" }
$null = Get-Command dotnet -CommandType Application -ErrorAction Stop
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$packageDirectory = Join-Path $outputRoot "$Configuration-no-libvlc-$runId"

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet 执行失败，退出码 $LASTEXITCODE；未完成打包。" }
}

# BuildManagedPluginPackage 会再启动 pwsh 和 dotnet。只给外层传 -p 参数不会传入子构建，
# 因此同时设置进程环境变量。项目中 IncludeLibVlcRuntime 的条件默认值会保留 false。
# finally 恢复调用者原值，避免随后正常构建也意外禁用原生库。
$previousIncludeRuntime = [Environment]::GetEnvironmentVariable('IncludeLibVlcRuntime', 'Process')
Push-Location -LiteralPath $projectRoot
try {
    $env:IncludeLibVlcRuntime = 'false'
    # 首次运行也先还原，保证 NuGet 的打包 Target 已导入；依赖版本保持锁定。
    Invoke-DotNet @('restore', $project, '--locked-mode', '--nologo', '-p:IncludeLibVlcRuntime=false', '-p:SkipPluginDeploy=true')
    Invoke-DotNet @('msbuild', $project, '-nologo', '-t:BuildManagedPluginPackage',
        "-p:Configuration=$Configuration", '-p:IncludeLibVlcRuntime=false',
        "-p:ManagedPluginPackageOutput=$packageDirectory")

    $packages = @(Get-ChildItem -LiteralPath $packageDirectory -File -Filter '*.zip')
    if ($packages.Count -ne 1) { throw "预期生成一个 ZIP，请检查：$packageDirectory" }
    $zipPath = $packages[0].FullName
    $manifestPath = [IO.Path]::ChangeExtension($zipPath, '.manifest.json')
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.archive.sha256 -ne (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash) {
        throw 'ZIP 与外置清单的 SHA-256 不一致。'
    }

    # 校验最终 ZIP，而不是只相信构建开关。LibVLCSharp.dll 是必需的托管适配器，需要保留；
    # 排除的是 libvlc.dll、libvlccore.dll 以及原生 libvlc 目录及其解码插件。
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $native = @($names | Where-Object { $_ -match '(?i)(^|/)(libvlc/|libvlc\.dll$|libvlccore\.dll$)' })
        if ($native.Count -gt 0) { throw "ZIP 意外包含 libVLC 原生库：$($native -join ', ')" }
        foreach ($file in @('plugin.manifest.json', 'MusicNetEasePlugin.Plugin.dll', 'LibVLCSharp.dll', 'BouncyCastle.Cryptography.dll')) {
            if ("Controls/MusicNetEasePlugin/$file" -notin $names) { throw "ZIP 缺少必需文件：$file" }
        }
    }
    finally { $archive.Dispose() }

    Write-Host "已生成 $Configuration 插件 ZIP（不含 libVLC 原生库）："
    Write-Host $zipPath
    Write-Host "配套摘要清单：$manifestPath"
}
finally {
    [Environment]::SetEnvironmentVariable('IncludeLibVlcRuntime', $previousIncludeRuntime, 'Process')
    Pop-Location
}
