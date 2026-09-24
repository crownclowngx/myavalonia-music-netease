# 仅合成门禁自测夹具；沿用外层已验证的临时目录，绝不写产品运行产物。
$v7Directory = $v6Directory
$v6Map.scenarios.N01=@('Fixture.Pass'); Save-V6Map
foreach ($file in (Get-ChildItem -LiteralPath $v7Directory -File)) { $file.LastWriteTimeUtc=[datetime]::UtcNow }
$v7MapPath = Join-Path $v7Directory 'v7-map.json'
$v7Map = @{ schemaVersion=1; methods=@{ 'Fixture.Pass'=1 }; scenarios=@{} }
foreach ($scenario in (Get-V7RequiredScenarios)) { $v7Map.scenarios[$scenario]=@('Fixture.Pass') }
function Save-V7Map { $v7Map | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $v7MapPath }
Save-V7Map
if ((Assert-V7TestMap $path $v7MapPath $v6MapPath).scenarios -ne 46) { throw '正确 V7 映射被拒绝。' }; $checks++
foreach ($scenario in (Get-V7RequiredScenarios)) {
    $v7Map.scenarios.Remove($scenario); Save-V7Map
    Expect-Rejection { Assert-V7TestMap $path $v7MapPath $v6MapPath }; $checks++
    $v7Map.scenarios[$scenario]=@('Fixture.Pass')
}
$v7Map.scenarios.A01=@('Fixture.NeverExecuted'); Save-V7Map; Expect-Rejection { Assert-V7TestMap $path $v7MapPath $v6MapPath }; $checks++
$v7Map.scenarios.A01=@('Fixture.Pass'); $v7Map.methods['Fixture.Pass']=2; Save-V7Map
Expect-Rejection { Assert-V7TestMap $path $v7MapPath $v6MapPath }; $checks++; $v7Map.methods['Fixture.Pass']=1; Save-V7Map
$v6Map.scenarios.Remove('N01'); Save-V6Map
Expect-Rejection { Assert-V7TestMap $path $v7MapPath $v6MapPath }; $checks++; $v6Map.scenarios.N01=@('Fixture.Pass'); Save-V6Map
$v7Fixtures = @{
    'v7-library-consistency.json' = @{ schemaVersion=1; realHost=$false; maximumConcurrentWrites=1; duplicateWrites=0; oldAccountApplied=0; lateOverwrites=0;
        operations=@(@{ operationId=[guid]::NewGuid().ToString(); targetId=7; writes=1; recheckRounds=3; elapsedMs=2000; inputUnique=3;
            items=@(@{trackId=1;state='AlreadyPresent'},@{trackId=3;state='Added'},@{trackId=4;state='NeedsRecheck'}) }) }
    'v7-library-lifetime.json' = @{ schemaVersion=1; realHost=$false; rounds=20; subscribers=0; pendingTasks=0; extraMediaOpens=0; replayWrites=0; shutdownDrained=$true }
    'v7-library-layout.json' = @{ schemaVersion=1; realHost=$false; screenshots=16; combinations=@(
        foreach ($size in @(@(1200,720),@(800,600),@(520,420))) { foreach ($dark in @($false,$true)) { foreach ($density in @($false,$true)) {
            @{width=$size[0];height=$size[1];dark=$dark;comfortable=$density;submitReachable=$true;focusReturned=$true;horizontalOverflow=0}
        } } }) }
}
function Save-V7Evidence([string]$Name, [hashtable]$Data) {
    $Data.provenance=$evidenceContext.Clone()
    $Data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $v7Directory $Name)
}
foreach ($name in $v7Fixtures.Keys) { Save-V7Evidence $name $v7Fixtures[$name] }
foreach ($shot in (Get-V7Screenshots)) {
    $bitmap=[Drawing.Bitmap]::new($shot.width,$shot.height); $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.Clear([Drawing.Color]::White); $graphics.DrawLine([Drawing.Pens]::Blue,0,0,$shot.width-1,$shot.height-1); $bitmap.Save((Join-Path $v7Directory $shot.name),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    Save-V7Evidence ($shot.name+'.json') @{schemaVersion=1;name=$shot.name;width=$shot.width;height=$shot.height;realHost=$false;sha256=(Get-FileHash -LiteralPath (Join-Path $v7Directory $shot.name)).Hash}
}
Assert-V7Artifacts $v7Directory $evidenceContext $started; $checks++
foreach ($name in $v7Fixtures.Keys) {
    $data=$v7Fixtures[$name]
    foreach ($field in @($data.Keys | Where-Object { $_ -ne 'provenance' })) {
        $value=$data[$field]; $data.Remove($field); Save-V7Evidence $name $data
        Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++
        $data[$field]=$value; Save-V7Evidence $name $data
    }
}
foreach ($case in @(@('v7-library-consistency.json','maximumConcurrentWrites',2),@('v7-library-consistency.json','duplicateWrites',1),
    @('v7-library-consistency.json','oldAccountApplied',1),@('v7-library-consistency.json','lateOverwrites',1),@('v7-library-consistency.json','maximumConcurrentWrites','1'),
    @('v7-library-lifetime.json','rounds',19),@('v7-library-lifetime.json','subscribers',1),@('v7-library-lifetime.json','pendingTasks',1),
    @('v7-library-lifetime.json','extraMediaOpens',1),@('v7-library-lifetime.json','replayWrites',1),@('v7-library-lifetime.json','shutdownDrained','true'))) {
    $data=$v7Fixtures[$case[0]]; $old=$data[$case[1]]; $data[$case[1]]=$case[2]; Save-V7Evidence $case[0] $data
    Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++
    $data[$case[1]]=$old; Save-V7Evidence $case[0] $data
}
$business=$v7Fixtures['v7-library-consistency.json']; $op=$business.operations[0]
foreach ($case in @(@('writes',2),@('recheckRounds',4),@('elapsedMs',20001),@('inputUnique',4),@('operationId','bad'),@('elapsedMs','0'))) {
    $old=$op[$case[0]]; $op[$case[0]]=$case[1]; Save-V7Evidence 'v7-library-consistency.json' $business
    Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++
    $op[$case[0]]=$old; Save-V7Evidence 'v7-library-consistency.json' $business
}
foreach ($case in @(@('trackId',1),@('state','Success'))) {
    $old=$op.items[1][$case[0]]; $op.items[1][$case[0]]=$case[1]; Save-V7Evidence 'v7-library-consistency.json' $business
    Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++
    $op.items[1][$case[0]]=$old; Save-V7Evidence 'v7-library-consistency.json' $business
}
$layout=$v7Fixtures['v7-library-layout.json']; $all=$layout.combinations; $layout.combinations=@($all | Select-Object -Skip 1); Save-V7Evidence 'v7-library-layout.json' $layout
Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++; $layout.combinations=$all; Save-V7Evidence 'v7-library-layout.json' $layout
foreach ($case in @(@('width',521),@('dark','false'),@('submitReachable',$false),@('focusReturned',$false),@('horizontalOverflow',1))) {
    $old=$all[0][$case[0]]; $all[0][$case[0]]=$case[1]; Save-V7Evidence 'v7-library-layout.json' $layout
    Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++; $all[0][$case[0]]=$old; Save-V7Evidence 'v7-library-layout.json' $layout
}
foreach ($field in @('runId','revision','sourceSha256')) {
    $context=$evidenceContext.Clone(); $context[$field]='old'; Expect-Rejection { Assert-V7Artifacts $v7Directory $context $started }; $checks++
}
foreach ($flag in @('realHost','realAccountVerified','hostVerified','deployed','releaseGateExecuted')) {
    $business[$flag]=$true; Save-V7Evidence 'v7-library-consistency.json' $business
    Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++; $business[$flag]=$false; Save-V7Evidence 'v7-library-consistency.json' $business
}
$allFiles=@($v7Fixtures.Keys) + @((Get-V7Screenshots) | ForEach-Object { $_.name; $_.name+'.json' }) + @('v6-queue-gesture.json')
foreach ($name in $allFiles) {
    $file=Join-Path $v7Directory $name; $bytes=[IO.File]::ReadAllBytes($file)
    Remove-Item -LiteralPath $file; Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++
    [IO.File]::WriteAllBytes($file,$bytes)
}
$file=Join-Path $v7Directory 'v7-library-light-520.png'; $bytes=[IO.File]::ReadAllBytes($file)
[IO.File]::WriteAllBytes($file,[byte[]]::new(10)); Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++; [IO.File]::WriteAllBytes($file,$bytes)
$proofPath=$file+'.json'; $proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json -AsHashtable
foreach ($case in @(@('width',521),@('sha256','bad'),@('schemaVersion','1'))) {
    $old=$proof[$case[0]]; $proof[$case[0]]=$case[1]; Save-V7Evidence ([IO.Path]::GetFileName($proofPath)) $proof
    Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++; $proof[$case[0]]=$old; Save-V7Evidence ([IO.Path]::GetFileName($proofPath)) $proof
}
$stale=Get-Item -LiteralPath $proofPath; $stale.LastWriteTimeUtc=$started.UtcDateTime.AddMinutes(-5)
Expect-Rejection { Assert-V7Artifacts $v7Directory $evidenceContext $started }; $checks++; $stale.LastWriteTimeUtc=[datetime]::UtcNow
Assert-V7Artifacts $v7Directory $evidenceContext $started; $checks++

# G05：每个已有前置分支都必须包含 V7；正文门禁仍运行源码摘要和链接检查。
$entry=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verify-development.ps1') -Raw
foreach ($match in [regex]::Matches($entry, '\$Milestone -in @\(([^)]*)\)')) {
    if (!$match.Groups[1].Value.Contains("'V7'")) { throw 'V7 遗漏前置检查分支。' }; $checks++
}
foreach ($required in @('Assert-MarkdownLinks $root','Get-DevelopmentSourceStamp $root','Assert-DevelopmentSourceUnchanged $source $after','Assert-V7TestMap','Assert-V7Artifacts')) {
    if (!$entry.Contains($required)) { throw "V7 入口丢失检查：$required" }; $checks++
}
$sourceBefore=@{revision='commit';sourceSha256='source'}
Assert-DevelopmentSourceUnchanged $sourceBefore $sourceBefore.Clone(); $checks++
foreach ($field in @('revision','sourceSha256')) {
    $sourceAfter=$sourceBefore.Clone(); $sourceAfter[$field]='changed'
    Expect-Rejection { Assert-DevelopmentSourceUnchanged $sourceBefore $sourceAfter }; $checks++
}
