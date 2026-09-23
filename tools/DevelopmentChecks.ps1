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

function Assert-M1TestMap {
    param([string]$TrxPath, [string]$MapPath)
    $required = @()
    foreach ($group in @(@('P',6),@('A',6),@('C',6),@('B',7),@('L',9),@('E',7),@('T',9),@('U',8))) {
        foreach ($number in 1..$group[1]) { $required += '{0}{1:00}' -f $group[0],$number }
    }
    return Assert-DevelopmentTestMap $TrxPath $MapPath $required
}

function Assert-V3TestMap {
    param([string]$TrxPath, [string]$MapPath)
    # AUTO 编号仅代表可自动重复的子场景，不冒充系统主题、真实 Dock 或硬件帧时间验收。
    return Assert-DevelopmentTestMap $TrxPath $MapPath @('THEME-AUTO','DOCUMENT-AUTO','TOOL-AUTO','LOGIN-AUTO','MOTION-AUTO','PREFERENCES-AUTO','RESOURCE-AUTO')
}

function Assert-DevelopmentTestMap {
    param([string]$TrxPath, [string]$MapPath, [string[]]$Required)
    # 映射是经审阅的实际方法清单，不在门禁里扫描 Trait 字符串冒充执行证据。
    $map = Get-Content -LiteralPath $MapPath -Raw | ConvertFrom-Json -AsHashtable
    if ($map.schemaVersion -ne 1 -or !$map.methods.Count -or !$map.scenarios.Count) { throw '测试映射为空或版本无效。' }
    $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($TrxPath), $settings)
    try { $xml = [Xml.XmlDocument]::new(); $xml.Load($reader) } finally { $reader.Dispose() }
    $executed = @{}
    foreach ($definition in $xml.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]')) {
        $method = $definition.SelectSingleNode('*[local-name()="TestMethod"]')
        $id = $definition.GetAttribute('id')
        $result = @($xml.SelectNodes('//*[local-name()="Results"]/*[local-name()="UnitTestResult"]') | Where-Object { $_.GetAttribute('testId') -eq $id })
        if ($null -eq $method -or $result.Count -ne 1 -or $result[0].GetAttribute('outcome') -ne 'Passed') { throw '测试映射缺少唯一的通过结果。' }
        $name = $method.GetAttribute('className') + '.' + $method.GetAttribute('name')
        if (!$executed.ContainsKey($name)) { $executed[$name] = 0 }
        $executed[$name]++
    }
    foreach ($name in $map.methods.Keys) {
        if ([int]$map.methods[$name] -le 0 -or !$executed.ContainsKey($name) -or $executed[$name] -lt [int]$map.methods[$name]) {
            throw "必测方法或参数化用例未执行：$name"
        }
    }
    foreach ($scenario in $required) {
        if (!$map.scenarios.ContainsKey($scenario) -or !$map.scenarios[$scenario].Count) { throw "缺少场景映射：$scenario" }
        foreach ($name in $map.scenarios[$scenario]) {
            if (!$map.methods.ContainsKey($name) -or !$executed.ContainsKey($name)) { throw "场景引用未执行的方法：$scenario / $name" }
        }
    }
    return @{ scenarios = $required.Count; methods = $map.methods.Count; executedCases = ($map.methods.Keys | ForEach-Object { $executed[$_] } | Measure-Object -Sum).Sum }
}

function Get-V3ScreenshotNames {
    foreach ($theme in @('light','dark')) {
        foreach ($width in @(1200,800,520)) { "v3-document-$theme-$width.png" }
        foreach ($width in @(280,320,420,640)) { "v3-tool-$theme-$width.png" }
        "v3-login-$theme.png"
    }
    'v3-host-theme-dark-composition.png'
}

function Assert-V3Artifacts {
    param([string]$RunDirectory)
    foreach ($name in (Get-V3ScreenshotNames)) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $RunDirectory $name))
        if ($bytes.Length -lt 1024 -or [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A') { throw "缺少 V3 实际渲染截图：$name" }
    }
    $motion = Get-Content -LiteralPath (Join-Path $RunDirectory 'v3-motion-observation.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($motion.schemaVersion -ne 1 -or !$motion.environment -or $motion.realHost -ne $false -or $motion.frameTimeMeasured -ne $false -or
        $motion.detachedViews -ne 40 -or $motion.retainedViews -ne 0 -or $motion.samples.Count -ne 6) {
        throw 'V3 动效证据缺项、视图残留或把 Headless 误报为真 Host / 帧时间。'
    }
    foreach ($reduced in @($false,$true)) {
        foreach ($scenario in @('idle','playing','hidden')) {
            $sample = @($motion.samples | Where-Object { $_.reducedMotion -eq $reduced -and $_.scenario -eq $scenario })
            if ($sample.Count -ne 1 -or $sample[0].wallMilliseconds -lt 450 -or $sample[0].cpuMilliseconds -lt 0 -or
                $sample[0].allocatedBytes -lt 0 -or $sample[0].activeFadeAfter -ne $false) { throw "V3 动效采样不完整或仍有淡入：$reduced / $scenario" }
        }
    }
}

function Assert-M1Artifacts {
    param([string]$RunDirectory)
    $native = Get-Content -LiteralPath (Join-Path $RunDirectory 'libvlc-offline.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($native.actualDeviceOutput -ne $false -or !$native.ActiveVersion -or !$native.ActiveDirectory -or
        $native.sample.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $native.measurements.Count -lt 12 -or
        @($native.measurements | Where-Object { $_.decodedFrames -le 0 }).Count) { throw '真实离线解码证据缺项或把 PCM 误报为设备输出。' }
    foreach ($name in @('m1-music-and-settings.png', 'm1-compact.png')) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $RunDirectory $name))
        if ($bytes.Length -lt 1024 -or [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A') { throw "缺少有效的生产 View 渲染证据：$name" }
    }
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
