# V6 只扩展本地开发检查；测试、截图与结构化证据必须属于同一轮源码身份。
function Get-V6RequiredScenarios { @('N01','N02','N03','L01','L02','P01','P02','P03','P04','B01','B02','Q01','Q02','U01','R01') }
function Assert-V6TestMap {
    param([string]$TrxPath, [string]$MapPath)
    Assert-DevelopmentTestMap $TrxPath $MapPath @(Get-V6RequiredScenarios)
}
function Get-V6Screenshots {
    foreach ($theme in @('light','dark')) {
        foreach ($size in @(@(1200,720),@(800,600),@(520,420))) {
            foreach ($panel in @('lyrics','queue')) { @{ name="v6-$panel-$theme-$($size[0]).png"; width=$size[0]; height=$size[1] } }
        }
    }
    @{ name='v6-drawer-lyrics.png'; width=1200; height=720 }
    @{ name='v6-compact-player.png'; width=520; height=420 }
}
function Assert-V6Artifacts {
    param([string]$RunDirectory, [hashtable]$Context, [datetimeoffset]$StartedAt)
    foreach ($name in @('v6-interaction-ui.json','v6-queue-gesture.json') + @((Get-V6Screenshots) | ForEach-Object name)) {
        $file = Get-Item -LiteralPath (Join-Path $RunDirectory $name)
        if ($file.LastWriteTimeUtc -lt $StartedAt.UtcDateTime.AddSeconds(-2)) { throw "V6 证据不是本轮生成：$name" }
    }
    foreach ($shot in (Get-V6Screenshots)) {
        $path = Join-Path $RunDirectory $shot.name
        Assert-RenderedPng $path $shot.width $shot.height
        $proof = Read-RunEvidence ($path+'.json') $Context
        if ($proof.name -cne $shot.name -or $proof.width -ne $shot.width -or $proof.height -ne $shot.height -or
            $proof.realHost -ne $false -or $proof.sha256 -cne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw 'V6 截图身份、尺寸或摘要不符。' }
    }
    $ui = Read-RunEvidence (Join-Path $RunDirectory 'v6-interaction-ui.json') $Context
    foreach ($field in @('realHost','CenterDeviation','CompactHeight','AudioOpened','ManualFollowRestored')) { if (!$ui.ContainsKey($field) -or $null -eq $ui[$field]) { throw "V6 界面证据缺少 $field" } }
    if ($ui.realHost -ne $false -or $ui.CenterDeviation -lt 0 -or $ui.CenterDeviation -gt 5 -or $ui.CompactHeight -le 0 -or
        $ui.CompactHeight -gt 140 -or $ui.AudioOpened -ne 1 -or $ui.ManualFollowRestored -ne $true) { throw 'V6 居中、紧凑布局或播放证据失败。' }
    $queue = Read-RunEvidence (Join-Path $RunDirectory 'v6-queue-gesture.json') $Context
    foreach ($field in @('realHost','Items','CommitsOnRelease','EdgeScrollOffset','TimerStopped','Reattachments','AudioOpened')) { if (!$queue.ContainsKey($field) -or $null -eq $queue[$field]) { throw "V6 手势证据缺少 $field" } }
    if ($queue.realHost -ne $false -or $queue.Items -ne 1001 -or $queue.CommitsOnRelease -ne 1 -or $queue.EdgeScrollOffset -le 0 -or
        $queue.TimerStopped -ne $true -or $queue.Reattachments -ne 20 -or $queue.AudioOpened -ne 0) { throw 'V6 手势或寿命证据失败。' }
}
