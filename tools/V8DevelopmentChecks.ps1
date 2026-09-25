# M4 本地开发证据：场景映射、业务观察、真实渲染和 V7 前置同时成立才通过。
function Get-V8RequiredScenarios {
    foreach ($group in @(@('A',6),@('D',6),@('W',6),@('P',4),@('F',10),@('H',4),@('C',6),@('U',5),@('R',3))) {
        1..$group[1] | ForEach-Object { $group[0] + $_.ToString('00') }
    }
}
function Assert-V8TestMap {
    param([string]$TrxPath, [string]$MapPath, [string]$PreviousMapPath = (Join-Path $PSScriptRoot 'v7-test-map.json'), [string]$V6MapPath = (Join-Path $PSScriptRoot 'v6-test-map.json'))
    $null = Assert-V7TestMap $TrxPath $PreviousMapPath $V6MapPath
    Assert-DevelopmentTestMap $TrxPath $MapPath @(Get-V8RequiredScenarios)
}
function Get-V8Screenshots {
    foreach ($theme in @('light','dark')) {
        foreach ($size in @(@(1200,720),@(800,600),@(520,420))) { @{ name="v8-discovery-$theme-$($size[0]).png"; width=$size[0]; height=$size[1] } }
        foreach ($panel in @('artist','album','fm','fm-uncertain')) { @{ name="v8-$panel-$theme.png"; width=800; height=600 } }
        @{ name="v8-remote-history-$theme.png"; width=520; height=420 }
    }
}
function Assert-V8Artifacts {
    param([string]$RunDirectory, [hashtable]$Context, [datetimeoffset]$StartedAt)
    Assert-V7Artifacts $RunDirectory $Context $StartedAt
    Assert-V8OwnArtifacts $RunDirectory $Context $StartedAt
}
function Assert-V8OwnArtifacts {
    param([string]$RunDirectory, [hashtable]$Context, [datetimeoffset]$StartedAt)
    $names = @('v8-discovery-navigation.json','v8-fm-session.json','v8-fm-budget.json','v8-fm-race.json','v8-fm-failures.json','v8-fm-lifetime.json','v8-discovery-account.json','v8-discovery-restore.json','v8-remote-history.json','v8-discovery-lifetime.json','v8-discovery-layout.json')
    foreach ($shot in (Get-V8Screenshots)) { $names += @($shot.name, ($shot.name+'.json')) }
    foreach ($name in $names) {
        $file=Get-Item -LiteralPath (Join-Path $RunDirectory $name)
        if ($file.LastWriteTimeUtc -lt $StartedAt.UtcDateTime.AddSeconds(-2) -or $file.LastWriteTimeUtc -gt [datetime]::UtcNow.AddSeconds(2)) { throw "V8 证据时间不属于本轮：$name" }
    }
    foreach ($shot in (Get-V8Screenshots)) {
        $path=Join-Path $RunDirectory $shot.name
        Assert-RenderedPng $path $shot.width $shot.height
        $proof=Read-V7Evidence ($path+'.json') $Context
        Assert-V7Number $proof 'width' $shot.width $shot.width; Assert-V7Number $proof 'height' $shot.height $shot.height
        if ($proof.name -cne $shot.name -or $proof.sha256 -cne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw 'V8 截图身份或摘要错误。' }
    }
    $navigation=Read-V7Evidence (Join-Path $RunDirectory 'v8-discovery-navigation.json') $Context
    Assert-V7Number $navigation 'reads' 2 2; Assert-V7Number $navigation 'readMediaOpens' 0 0; Assert-V7Flag $navigation 'complete' $true
    if ($navigation.loadedIds -isnot [array] -or $navigation.queuedIds -isnot [array] -or $navigation.loadedIds.Count -lt 1 -or
        ($navigation.loadedIds -join ',') -cne ($navigation.queuedIds -join ',')) { throw 'V8 播放范围与已加载身份不同。' }
    foreach ($id in $navigation.loadedIds) { Assert-V7Number @{id=$id} 'id' 1 ([long]::MaxValue) }
    $fm=Read-V7Evidence (Join-Path $RunDirectory 'v8-fm-session.json') $Context
    Assert-V7Number $fm 'maximumConcurrentReads' 1 1; Assert-V7Number $fm 'nextWrites' 0 0; Assert-V7Number $fm 'feedbackWrites' 1 1
    Assert-V7Number $fm 'pending' 0 10
    foreach ($field in @('targetId','actualTargetId','currentId')) { Assert-V7Number $fm $field 1 ([long]::MaxValue) }
    if ($fm.targetId -ne $fm.actualTargetId -or $fm.currentId -eq $fm.targetId) { throw 'V8 反馈目标或成功推进错误。' }
    foreach ($field in @('targetEntry','sessionId')) { if ([guid]::Parse($fm[$field]) -eq [guid]::Empty) { throw 'V8 FM 身份为空。' } }
    $budget=Read-V7Evidence (Join-Path $RunDirectory 'v8-fm-budget.json') $Context
    Assert-V7Number $budget 'requests' 3 3; Assert-V7Number $budget 'elapsedMs' 2000 20000 $false
    if ($budget.delays -isnot [array] -or $budget.delays.Count -ne 2) { throw 'V8 缺少补取等待观察。' }
    Assert-V7Number @{value=$budget.delays[0]} 'value' 500 500; Assert-V7Number @{value=$budget.delays[1]} 'value' 1500 1500
    $race=Read-V7Evidence (Join-Path $RunDirectory 'v8-fm-race.json') $Context
    Assert-V7Number $race 'reads' 2 2; Assert-V7Number $race 'maximumConcurrentReads' 1 1; Assert-V7Number $race 'opens' 2 2
    Assert-V7Number $race 'currentId' 20 20; Assert-V7Number $race 'feedbackWrites' 0 0
    $failures=Read-V7Evidence (Join-Path $RunDirectory 'v8-fm-failures.json') $Context
    Assert-V7Number $failures 'candidates' 20 20; Assert-V7Number $failures 'batches' 2 20; Assert-V7Number $failures 'mediaOpens' 0 0
    $restore=Read-V7Evidence (Join-Path $RunDirectory 'v8-discovery-restore.json') $Context
    Assert-V7Number $restore 'restoredEntries' 1 110; Assert-V7Flag $restore 'fmActive' $false
    foreach ($field in @('fmReads','feedbackWrites','mediaOpens','localSavesFromRemoteRead')) { Assert-V7Number $restore $field 0 0 }
    Assert-V7Number $restore 'recentBefore' 1 1; Assert-V7Number $restore 'recentAfter' 1 1
    $account=Read-V7Evidence (Join-Path $RunDirectory 'v8-discovery-account.json') $Context
    Assert-V7Number $account 'oldRows' 0 0; Assert-V7Number $account 'newRows' 1 1; Assert-V7Flag $account 'busy' $false; Assert-V7Number $account 'queueEntries' 0 0
    $fmLife=Read-V7Evidence (Join-Path $RunDirectory 'v8-fm-lifetime.json') $Context
    Assert-V7Number $fmLife 'rounds' 20 20; Assert-V7Number $fmLife 'peakSubscriptions' 1 1
    Assert-V7Number $fmLife 'retainedSubscriptions' 0 0; Assert-V7Number $fmLife 'activeReads' 0 0
    Assert-V7Flag $fmLife 'currentMedia' $false; Assert-V7Flag $fmLife 'fmActive' $false
    $history=Read-V7Evidence (Join-Path $RunDirectory 'v8-remote-history.json') $Context
    Assert-V7Number $history 'records' 2 2; Assert-V7Number $history 'queueEntriesAfterRead' 0 0; Assert-V7Number $history 'mediaOpensAfterRead' 0 0; Assert-V7Flag $history 'unknownFieldPreserved' $true
    $life=Read-V7Evidence (Join-Path $RunDirectory 'v8-discovery-lifetime.json') $Context
    Assert-V7Number $life 'rounds' 20 20
    foreach ($field in @('queuedCallbacks','mediaOpens','retainedRows')) { Assert-V7Number $life $field 0 0 }
    $layout=Read-V7Evidence (Join-Path $RunDirectory 'v8-discovery-layout.json') $Context
    Assert-V7Number $layout 'screenshots' 16 16
    if ($layout.combinations -isnot [array] -or $layout.combinations.Count -ne 60) { throw 'V8 缺少 60 个布局组合。' }
    $seen=@{}
    foreach ($row in $layout.combinations) {
        Assert-V7Number $row 'horizontalOverflow' 0 0; Assert-V7Number $row 'buttons' 1 100
        if ($row.dark -isnot [bool] -or $row.comfortable -isnot [bool]) { throw 'V8 布局布尔类型错误。' }
        $key="$($row.page)/$($row.width)/$($row.height)/$($row.dark)/$($row.comfortable)"
        if ($seen.ContainsKey($key)) { throw 'V8 布局组合重复。' }; $seen[$key]=$true
    }
    foreach ($page in @('Daily','ArtistSongs','Album','Fm','Recent')) { foreach ($size in @(@(1200,720),@(800,600),@(520,420))) { foreach ($dark in @($false,$true)) { foreach ($density in @($false,$true)) {
        if (!$seen.ContainsKey("$page/$($size[0])/$($size[1])/$dark/$density")) { throw 'V8 布局维度缺失。' }
    } } } }
}
