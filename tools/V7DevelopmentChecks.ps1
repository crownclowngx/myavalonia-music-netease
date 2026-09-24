# M3 本地门禁：业务数值、截图来源与前置 V6 证据同时成立才允许通过。
function Get-V7RequiredScenarios {
    foreach ($group in @(@('A',6),@('L',5),@('S',5),@('E',6),@('T',5),@('D',4),@('C',7),@('U',5),@('R',3))) {
        1..$group[1] | ForEach-Object { $group[0] + $_.ToString('00') }
    }
}
function Assert-V7TestMap {
    param([string]$TrxPath, [string]$MapPath, [string]$PreviousMapPath = (Join-Path $PSScriptRoot 'v6-test-map.json'))
    $null = Assert-V6TestMap $TrxPath $PreviousMapPath
    Assert-DevelopmentTestMap $TrxPath $MapPath @(Get-V7RequiredScenarios)
}
function Get-V7Screenshots {
    foreach ($theme in @('light','dark')) {
        foreach ($size in @(@(1200,720),@(800,600),@(520,420))) {
            foreach ($panel in @('library','editor')) { @{ name="v7-$panel-$theme-$($size[0]).png"; width=$size[0]; height=$size[1] } }
        }
        foreach ($panel in @('delete','recheck')) { @{ name="v7-$panel-$theme.png"; width=800; height=600 } }
    }
}
function Assert-V7Number {
    param([System.Collections.IDictionary]$Data, [string]$Field, [double]$Min, [double]$Max, [bool]$Integer = $true)
    $value = $Data[$Field]
    if ($null -eq $value -or $value -is [bool] -or $value -is [string] -or $value -isnot [ValueType] -or
        [double]::IsNaN([double]$value) -or [double]::IsInfinity([double]$value) -or $value -lt $Min -or $value -gt $Max -or
        ($Integer -and [math]::Truncate([double]$value) -ne $value)) { throw "V7 数值无效：$Field" }
}
function Assert-V7Flag {
    param([System.Collections.IDictionary]$Data, [string]$Field, [bool]$Expected)
    if ($Data[$Field] -isnot [bool] -or $Data[$Field] -ne $Expected) { throw "V7 布尔事实无效：$Field" }
}
function Read-V7Evidence {
    param([string]$Path, [hashtable]$Context)
    $data = Read-RunEvidence $Path $Context
    Assert-V7Number $data 'schemaVersion' 1 1
    Assert-V7Flag $data 'realHost' $false
    foreach ($flag in @('realAccountVerified','hostVerified','deployed','releaseGateExecuted')) {
        if ($data.ContainsKey($flag)) { Assert-V7Flag $data $flag $false }
    }
    return $data
}
function Assert-V7Artifacts {
    param([string]$RunDirectory, [hashtable]$Context, [datetimeoffset]$StartedAt)
    # 前置检查属于 V7 的实际路径；自测会删除 V6 专属产物验证此依赖，不能只检查脚本文本。
    Assert-V6Artifacts $RunDirectory $Context $StartedAt
    $names = @('v7-library-consistency.json','v7-library-lifetime.json','v7-library-layout.json')
    foreach ($shot in (Get-V7Screenshots)) { $names += @($shot.name, ($shot.name + '.json')) }
    foreach ($name in $names) {
        $file = Get-Item -LiteralPath (Join-Path $RunDirectory $name)
        if ($file.LastWriteTimeUtc -lt $StartedAt.UtcDateTime.AddSeconds(-2) -or $file.LastWriteTimeUtc -gt [datetime]::UtcNow.AddSeconds(2)) { throw "V7 证据不在本轮时间窗口：$name" }
    }
    foreach ($shot in (Get-V7Screenshots)) {
        $path = Join-Path $RunDirectory $shot.name
        Assert-RenderedPng $path $shot.width $shot.height
        $proof = Read-V7Evidence ($path + '.json') $Context
        Assert-V7Number $proof 'width' $shot.width $shot.width; Assert-V7Number $proof 'height' $shot.height $shot.height
        if ($proof.name -cne $shot.name -or $proof.sha256 -cne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw 'V7 截图名称或摘要错误。' }
    }
    $business = Read-V7Evidence (Join-Path $RunDirectory 'v7-library-consistency.json') $Context
    Assert-V7Number $business 'maximumConcurrentWrites' 1 1
    foreach ($field in @('duplicateWrites','oldAccountApplied','lateOverwrites')) { Assert-V7Number $business $field 0 0 }
    if ($business.operations -isnot [array] -or $business.operations.Count -lt 1) { throw '缺少 V7 操作关联证据。' }
    $operationIds = @{}
    foreach ($operation in $business.operations) {
        $guid = [guid]::Parse($operation.operationId)
        if ($guid -eq [guid]::Empty -or $operationIds.ContainsKey($guid)) { throw 'V7 操作身份为空或重复。' }; $operationIds[$guid]=$true
        Assert-V7Number $operation 'targetId' 1 ([long]::MaxValue)
        Assert-V7Number $operation 'writes' 1 1; Assert-V7Number $operation 'recheckRounds' 1 3
        Assert-V7Number $operation 'elapsedMs' 0 20000 $false; Assert-V7Number $operation 'inputUnique' 1 50
        if ($operation.items -isnot [array] -or $operation.items.Count -ne $operation.inputUnique) { throw 'V7 部分结果计数不守恒。' }
        $ids = @{}
        foreach ($item in $operation.items) {
            Assert-V7Number $item 'trackId' 1 ([long]::MaxValue)
            if ($ids.ContainsKey($item.trackId) -or $item.state -cnotin @('Added','AlreadyPresent','Removed','AlreadyAbsent','Rejected','NeedsRecheck')) { throw 'V7 逐项结果重复或分类无效。' }
            $ids[$item.trackId]=$true
        }
    }
    $life = Read-V7Evidence (Join-Path $RunDirectory 'v7-library-lifetime.json') $Context
    Assert-V7Number $life 'rounds' 20 20
    foreach ($field in @('subscribers','pendingTasks','extraMediaOpens','replayWrites')) { Assert-V7Number $life $field 0 0 }
    Assert-V7Flag $life 'shutdownDrained' $true
    $layout = Read-V7Evidence (Join-Path $RunDirectory 'v7-library-layout.json') $Context
    Assert-V7Number $layout 'screenshots' 16 16
    if ($layout.combinations -isnot [array] -or $layout.combinations.Count -ne 12) { throw 'V7 缺少三尺寸双色两密度组合。' }
    $combinations = @{}
    foreach ($row in $layout.combinations) {
        Assert-V7Number $row 'width' 520 1200; Assert-V7Number $row 'height' 420 720
        if ($row.dark -isnot [bool] -or $row.comfortable -isnot [bool]) { throw 'V7 布局维度不是布尔值。' }
        $key = "$($row.width)/$($row.height)/$($row.dark)/$($row.comfortable)"
        if ($combinations.ContainsKey($key)) { throw 'V7 布局组合重复。' }; $combinations[$key]=$true
        Assert-V7Flag $row 'submitReachable' $true; Assert-V7Flag $row 'focusReturned' $true; Assert-V7Number $row 'horizontalOverflow' 0 0
    }
    foreach ($size in @(@(1200,720),@(800,600),@(520,420))) { foreach ($dark in @($false,$true)) { foreach ($density in @($false,$true)) {
        if (!$combinations.ContainsKey("$($size[0])/$($size[1])/$dark/$density")) { throw 'V7 布局维度缺失。' }
    } } }
}
