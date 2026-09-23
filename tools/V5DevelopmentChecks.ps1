# V5 只扩展本地开发证据，沿用统一的进程、TRX、源码身份和文档校验。
function Get-V5RequiredScenarios {
    foreach ($group in @(@('Q',8),@('N',6),@('L',3),@('P',3),@('R',7),@('U',3))) {
        foreach ($number in 1..$group[1]) { '{0}{1:00}' -f $group[0],$number }
    }
}
function Assert-V5TestMap {
    param([string]$TrxPath, [string]$MapPath)
    $map = Get-Content -LiteralPath $MapPath -Raw | ConvertFrom-Json -AsHashtable
    if ($map.scriptScenarios.U04 -cne 'Assert-V5Assets') { throw 'U04 必须由实际静态资产检查覆盖。' }
    return Assert-DevelopmentTestMap $TrxPath $MapPath @(Get-V5RequiredScenarios)
}
function Get-V5Screenshots {
    foreach ($theme in @('light','dark')) {
        foreach ($size in @(@(1200,720),@(800,600),@(520,420))) { @{ name="v5-detail-$theme-$($size[0]).png"; width=$size[0]; height=$size[1] } }
        foreach ($state in @('settings','failure')) { @{ name="v5-$state-$theme.png"; width=800; height=600 } }
    }
}
function Assert-V5Assets {
    param([string]$Root)
    $paths = & git -C $Root ls-files --cached --others --exclude-standard -- src/MusicNetEasePlugin.Plugin
    if ($LASTEXITCODE -ne 0) { throw '不能枚举 V5 产品资产。' }
    $static = @($paths | Where-Object { $_ -match '/Styles/.*\.axaml$' } | Sort-Object -Unique)
    $bytes = ($static | ForEach-Object { (Get-Item -LiteralPath (Join-Path $Root $_)).Length } | Measure-Object -Sum).Sum
    if (!$static.Count -or $bytes -gt 102400) { throw '矢量和主题静态资产超过 100 KiB 或缺失。' }
    $bitmaps = @($paths | Where-Object { $_ -match '\.(png|jpg|jpeg|webp|gif|mp4|woff2?|ttf|otf)$' })
    if ($bitmaps.Count) { throw '产品目录出现未批准的内置位图、视频或字体资源。' }
    return @{ scenario='U04'; staticAssetBytes=$bytes; staticAssetFiles=$static.Count; builtInBitmapFontVideoFiles=$bitmaps.Count }
}
function Assert-V5Artifacts {
    param([string]$RunDirectory, [hashtable]$Context, [datetimeoffset]$StartedAt)
    foreach ($name in @('v5-artwork-budget.json','v5-image-transfer.json','v5-image-compressed.json','ui-resource-observation.json') + @((Get-V5Screenshots) | ForEach-Object name)) {
        $file = Get-Item -LiteralPath (Join-Path $RunDirectory $name)
        if ($file.LastWriteTimeUtc -lt $StartedAt.UtcDateTime.AddSeconds(-2)) { throw "V5 证据不是本轮生成：$name" }
    }
    foreach ($shot in (Get-V5Screenshots)) {
        $path = Join-Path $RunDirectory $shot.name
        Assert-RenderedPng $path $shot.width $shot.height
        $proof = Read-RunEvidence ($path+'.json') $Context
        if ($proof.name -cne $shot.name -or $proof.width -ne $shot.width -or $proof.height -ne $shot.height -or
            $proof.realHost -ne $false -or $proof.sha256 -cne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw 'V5 截图身份、尺寸或摘要不符。' }
    }
    $decoded = Read-RunEvidence (Join-Path $RunDirectory 'v5-artwork-budget.json') $Context
    foreach ($field in @('observedEntries','observedBytes','retainedEntriesAfterRelease','sharedLease','oversizeRejected')) {
        if (!$decoded.ContainsKey($field)) { throw "V5 解码证据缺少 $field" }
    }
    if ($decoded.observedEntries -ne 24 -or $decoded.observedBytes -le 0 -or $decoded.observedBytes -gt 16777216 -or
        $decoded.retainedEntriesAfterRelease -ne 0 -or $decoded.sharedLease -ne $true -or $decoded.oversizeRejected -ne $true -or $decoded.realHost -ne $false) { throw 'V5 解码预算或引用释放证据失败。' }
    $network = Read-RunEvidence (Join-Path $RunDirectory 'v5-image-transfer.json') $Context
    foreach ($field in @('observedPeakRequests','pendingAfterCompletion','sharedDownloadCalls','consumerCancellationIsolated')) {
        if (!$network.ContainsKey($field)) { throw "V5 下载证据缺少 $field" }
    }
    if ($network.observedPeakRequests -ne 2 -or $network.pendingAfterCompletion -ne 0 -or $network.sharedDownloadCalls -ne 1 -or
        $network.consumerCancellationIsolated -ne $true -or $network.realHost -ne $false) { throw 'V5 下载并发、合并或取消证据失败。' }
    $compressed = Read-RunEvidence (Join-Path $RunDirectory 'v5-image-compressed.json') $Context
    if (!$compressed.ContainsKey('observedCachedBytes') -or $compressed.observedCachedBytes -ne 8388608 -or
        $compressed.oldestWasFetchedAgain -ne $true -or $compressed.oversizeRejected -ne $true -or $compressed.realHost -ne $false) { throw 'V5 压缩缓存未覆盖上限或未按预期淘汰。' }
    $observation = Read-RunEvidence (Join-Path $RunDirectory 'ui-resource-observation.json') $Context
    if ($observation.realHost -ne $false -or !$observation.environment -or $observation.rounds -lt 1 -or $observation.seconds -lt 1 -or
        $observation.queueEntries -ne 10000 -or $observation.lyricLines -ne 1000 -or $observation.observations.Count -ne 3 * $observation.rounds) { throw 'V5 资源采样条件或边界不符。' }
    foreach ($round in 0..($observation.rounds-1)) {
        foreach ($scenario in @('paused-visible','lyrics-visible','playing-hidden')) {
            $samples = @($observation.observations | Where-Object { $_.round -eq $round -and $_.scenario -ceq $scenario })
            if ($samples.Count -ne 1) { throw 'V5 资源采样缺场景或重复。' }
            $sample = $samples[0]
            foreach ($field in @('elapsedMs','privateBytes','peakPrivateBytes','allocatedBytes','cpuOneCorePercent','visibleRows','navigationSamples','navigationPairP50Ms','navigationPairP95Ms')) {
                if (!$sample.ContainsKey($field) -or $null -eq $sample[$field] -or $sample[$field] -lt 0) { throw "V5 资源采样缺少或非法字段 $field" }
            }
            if ($sample.elapsedMs -lt $observation.seconds * 950 -or $sample.privateBytes -le 0 -or $sample.navigationSamples -ne 100 -or
                $sample.visibleRows -gt 60 -or ($scenario -eq 'playing-hidden' -and $sample.visibleRows -ne 0)) { throw 'V5 采样时长、样本数或虚拟化边界错误。' }
        }
    }
}
