#requires -Version 7.0
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# 仅编排本地开发检查，不调用 Host 的部署、CI 或发布工具。独立函数方便注入失败报告自测。
function Invoke-CheckedProcess {
    param([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory, [int]$TimeoutSeconds = 300)
    $start = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw "无法启动 $FilePath" }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "开发检查超时：$FilePath"
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($output) { Write-Host $output.TrimEnd() }
        if ($errorText) { Write-Host $errorText.TrimEnd() }
        if ($process.ExitCode -ne 0) { throw "开发检查失败：$FilePath，退出码 $($process.ExitCode)" }
    }
    finally { $process.Dispose() }
}

function Assert-DevelopmentTrx {
    param([string]$Path, [datetimeoffset]$StartedAt, [string]$RunDirectory)
    $actual = [IO.Path]::GetFullPath($Path)
    $expected = [IO.Path]::GetFullPath($RunDirectory)
    if ([IO.Path]::GetDirectoryName($actual) -ne $expected -or !(Test-Path -LiteralPath $actual -PathType Leaf)) {
        throw '缺少本轮独立目录中的 TRX。'
    }
    if ((Get-Item -LiteralPath $actual).LastWriteTimeUtc -lt $StartedAt.UtcDateTime.AddSeconds(-2)) { throw 'TRX 文件不是本轮生成。' }
    # 禁止 DTD/外部实体；即使报告异常，也不允许校验器读取外部资源。
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $reader = [Xml.XmlReader]::Create($actual, $settings)
    try { $xml = [Xml.XmlDocument]::new(); $xml.Load($reader) }
    finally { $reader.Dispose() }
    $run = $xml.SelectSingleNode('/*[local-name()="TestRun"]')
    if ($null -eq $run) { throw 'TRX 没有 TestRun。' }
    $times = $run.SelectSingleNode('*[local-name()="Times"]')
    if ($null -eq $times) { throw 'TRX 缺少运行时间。' }
    $begin = [datetimeoffset]::Parse($times.GetAttribute('start'))
    $end = [datetimeoffset]::Parse($times.GetAttribute('finish'))
    if ($begin -lt $StartedAt.AddSeconds(-2) -or $end -lt $begin -or $end -gt [datetimeoffset]::UtcNow.AddSeconds(2)) { throw 'TRX 时间不属于本轮完整运行。' }
    $summary = $run.SelectSingleNode('*[local-name()="ResultSummary"]')
    if ($null -eq $summary -or $summary.GetAttribute('outcome') -notin @('Completed', 'Passed')) { throw '测试未正常完成。' }
    $counts = $summary.SelectSingleNode('*[local-name()="Counters"]')
    if ($null -eq $counts) { throw 'TRX 缺少统计。' }
    $total = [int]$counts.GetAttribute('total')
    if ($total -le 0 -or [int]$counts.GetAttribute('executed') -ne $total -or [int]$counts.GetAttribute('passed') -ne $total) { throw '测试为空、失败或存在跳过。' }
    foreach ($attribute in $counts.Attributes) {
        if ($attribute.Name -notin @('total', 'executed', 'passed') -and [int]$attribute.Value -ne 0) { throw "测试存在未通过状态：$($attribute.Name)" }
    }
    $results = @($run.SelectNodes('*[local-name()="Results"]/*[local-name()="UnitTestResult"]'))
    if ($results.Count -ne $total -or @($results | Where-Object { $_.GetAttribute('outcome') -ne 'Passed' }).Count -gt 0) { throw 'TRX 结果与统计不一致。' }
    return $total
}

function Get-MarkdownAnchors {
    param([string]$Text)
    $seen = @{}
    foreach ($match in [regex]::Matches($Text, '(?m)^#{1,6}\s+(.+?)\s*#*\s*$')) {
        $slug = $match.Groups[1].Value.Trim().ToLowerInvariant() -replace '[^\p{L}\p{M}\p{N}\p{Pc}\-\s]', '' -replace '\s', '-'
        if ($seen.ContainsKey($slug)) { $seen[$slug]++; "$slug-$($seen[$slug])" }
        else { $seen[$slug] = 0; $slug }
    }
}

function Assert-MarkdownLinks {
    param([string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root)
    $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    $files = @((Join-Path $Root 'README.md'), (Join-Path $Root 'THIRD-PARTY-NOTICES.md')) + @(Get-ChildItem (Join-Path $Root 'docs') -File -Recurse -Filter '*.md' | ForEach-Object FullName)
    $optional = [Collections.Generic.HashSet[string]]::new()
    foreach ($file in $files) {
        $text = [IO.File]::ReadAllText($file) -replace '(?ms)^```.*?^```\s*$', ''
        $links = @([regex]::Matches($text, '\]\(([^\s)]+)(?:\s+"[^"]*")?\)') | ForEach-Object { $_.Groups[1].Value }) + @([regex]::Matches($text, '(?m)^\s*\[[^\]]+\]:\s*(\S+)') | ForEach-Object { $_.Groups[1].Value })
        foreach ($link in $links) {
            if ($link -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|//)') { continue }
            $parts = $link.Trim('<', '>').Split('#', 2)
            $relative = [Uri]::UnescapeDataString($parts[0])
            $target = if (!$relative) { $file } else { [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($file)) $relative)) }
            if (!$target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { [void]$optional.Add($link); continue }
            if (!(Test-Path -LiteralPath $target)) { throw "文档链接不存在：$file -> $link" }
            if ($parts.Length -eq 2 -and $parts[1] -and [IO.Path]::GetExtension($target) -eq '.md') {
                $anchor = [Uri]::UnescapeDataString($parts[1])
                $anchors = @(Get-MarkdownAnchors ([IO.File]::ReadAllText($target)))
                if ($anchor -notin $anchors) { throw "文档锚点不存在：$file -> $link" }
            }
        }
    }
    Write-Host "文档检查通过：$($files.Count) 个文件；$($optional.Count) 个邻仓参考单列，不要求邻仓存在。"
}
