# 门禁自测使用外层已校验的临时目录；正反例不能成为产品功能证据。
$v8Directory=$v7Directory
$v8MapPath=Join-Path $v8Directory 'v8-map.json'
$v8Map=@{schemaVersion=1;methods=@{'Fixture.Pass'=1};scenarios=@{}}
foreach ($scenario in (Get-V8RequiredScenarios)) { $v8Map.scenarios[$scenario]=@('Fixture.Pass') }
function Save-V8Map { $v8Map | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $v8MapPath }
Save-V8Map
if ((Assert-V8TestMap $path $v8MapPath $v7MapPath $v6MapPath).scenarios -ne 50) { throw '正确 V8 映射被拒绝。' }; $checks++
foreach ($scenario in (Get-V8RequiredScenarios)) {
    $v8Map.scenarios.Remove($scenario); Save-V8Map
    Expect-Rejection { Assert-V8TestMap $path $v8MapPath $v7MapPath $v6MapPath }; $checks++
    $v8Map.scenarios[$scenario]=@('Fixture.Pass')
}
Save-V8Map
$v8Map.methods['Fixture.Pass']=2; Save-V8Map; Expect-Rejection { Assert-V8TestMap $path $v8MapPath $v7MapPath $v6MapPath }; $checks++; $v8Map.methods['Fixture.Pass']=1; Save-V8Map
$v8Fixtures=@{
 'v8-discovery-navigation.json'=@{schemaVersion=1;realHost=$false;reads=2;readMediaOpens=0;complete=$true;loadedIds=@(7);queuedIds=@(7)}
 'v8-fm-session.json'=@{schemaVersion=1;realHost=$false;maximumConcurrentReads=1;nextWrites=0;feedbackWrites=1;pending=3;targetId=11;actualTargetId=11;currentId=12;targetEntry=[guid]::NewGuid().ToString();sessionId=[guid]::NewGuid().ToString()}
 'v8-fm-budget.json'=@{schemaVersion=1;realHost=$false;requests=3;elapsedMs=2000;delays=@(500,1500)}
 'v8-fm-race.json'=@{schemaVersion=1;realHost=$false;reads=2;maximumConcurrentReads=1;opens=2;currentId=20;feedbackWrites=0}
 'v8-fm-failures.json'=@{schemaVersion=1;realHost=$false;candidates=20;batches=7;mediaOpens=0}
 'v8-discovery-account.json'=@{schemaVersion=1;realHost=$false;oldRows=0;newRows=1;busy=$false;queueEntries=0}
 'v8-fm-lifetime.json'=@{schemaVersion=1;realHost=$false;rounds=20;peakSubscriptions=1;retainedSubscriptions=0;activeReads=0;currentMedia=$false;fmActive=$false}
 'v8-discovery-restore.json'=@{schemaVersion=1;realHost=$false;restoredEntries=3;fmActive=$false;fmReads=0;feedbackWrites=0;mediaOpens=0;localSavesFromRemoteRead=0;recentBefore=1;recentAfter=1}
 'v8-remote-history.json'=@{schemaVersion=1;realHost=$false;records=2;queueEntriesAfterRead=0;mediaOpensAfterRead=0;unknownFieldPreserved=$true}
 'v8-discovery-lifetime.json'=@{schemaVersion=1;realHost=$false;rounds=20;queuedCallbacks=0;mediaOpens=0;retainedRows=0}
 'v8-discovery-layout.json'=@{schemaVersion=1;realHost=$false;screenshots=16;combinations=@(
  foreach ($page in @('Daily','ArtistSongs','Album','Fm','Recent')) { foreach ($size in @(@(1200,720),@(800,600),@(520,420))) { foreach ($dark in @($false,$true)) { foreach ($density in @($false,$true)) {
   @{page=$page;width=$size[0];height=$size[1];dark=$dark;comfortable=$density;horizontalOverflow=0;buttons=5}
  } } } })}
}
function Save-V8Evidence([string]$Name,[hashtable]$Data) { $Data.provenance=$evidenceContext.Clone(); $Data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $v8Directory $Name) }
foreach ($name in $v8Fixtures.Keys) { Save-V8Evidence $name $v8Fixtures[$name] }
foreach ($shot in (Get-V8Screenshots)) {
 $bitmap=[Drawing.Bitmap]::new($shot.width,$shot.height); $graphics=[Drawing.Graphics]::FromImage($bitmap)
 try { $graphics.Clear([Drawing.Color]::White); $graphics.DrawLine([Drawing.Pens]::Blue,0,0,$shot.width-1,$shot.height-1); $bitmap.Save((Join-Path $v8Directory $shot.name),[Drawing.Imaging.ImageFormat]::Png) }
 finally { $graphics.Dispose(); $bitmap.Dispose() }
 Save-V8Evidence ($shot.name+'.json') @{schemaVersion=1;name=$shot.name;width=$shot.width;height=$shot.height;realHost=$false;sha256=(Get-FileHash -LiteralPath (Join-Path $v8Directory $shot.name)).Hash}
}
Assert-V8Artifacts $v8Directory $evidenceContext $started; $checks++
foreach ($name in $v8Fixtures.Keys) {
 $data=$v8Fixtures[$name]
 foreach ($field in @($data.Keys | Where-Object { $_ -ne 'provenance' })) {
  $value=$data[$field]; $data.Remove($field); Save-V8Evidence $name $data
  Expect-Rejection { Assert-V8OwnArtifacts $v8Directory $evidenceContext $started }; $checks++
  $data[$field]=$value; Save-V8Evidence $name $data
 }
}
foreach ($case in @(@('v8-fm-session.json','maximumConcurrentReads',2),@('v8-fm-session.json','nextWrites',1),@('v8-fm-session.json','feedbackWrites',2),
 @('v8-fm-race.json','opens',3),@('v8-fm-race.json','currentId',21),@('v8-fm-failures.json','candidates',21),@('v8-fm-failures.json','batches',1),
 @('v8-discovery-account.json','oldRows',1),@('v8-fm-lifetime.json','retainedSubscriptions',1),@('v8-fm-lifetime.json','activeReads',1),@('v8-fm-lifetime.json','currentMedia',$true),
 @('v8-discovery-restore.json','fmActive',$true),@('v8-discovery-restore.json','fmReads',1),@('v8-discovery-restore.json','feedbackWrites',1),@('v8-discovery-restore.json','mediaOpens',1),@('v8-discovery-restore.json','localSavesFromRemoteRead',1),@('v8-discovery-restore.json','recentAfter',2),
 @('v8-fm-session.json','pending',11),@('v8-fm-session.json','actualTargetId',99),@('v8-fm-session.json','currentId',11),@('v8-fm-session.json','sessionId','bad'),
 @('v8-fm-budget.json','requests',4),@('v8-fm-budget.json','elapsedMs',20001),@('v8-fm-budget.json','delays',@(0,0)),
 @('v8-remote-history.json','queueEntriesAfterRead',1),@('v8-remote-history.json','unknownFieldPreserved','true'),@('v8-discovery-lifetime.json','queuedCallbacks',1),
 @('v8-discovery-navigation.json','readMediaOpens',1),@('v8-discovery-navigation.json','queuedIds',@(9)),@('v8-discovery-layout.json','screenshots',15))) {
 $data=$v8Fixtures[$case[0]]; $old=$data[$case[1]]; $data[$case[1]]=$case[2]; Save-V8Evidence $case[0] $data
 Expect-Rejection { Assert-V8OwnArtifacts $v8Directory $evidenceContext $started }; $checks++
 $data[$case[1]]=$old; Save-V8Evidence $case[0] $data
}
$layout=$v8Fixtures['v8-discovery-layout.json']; $rows=$layout.combinations
$layout.combinations=@($rows | Select-Object -Skip 1); Save-V8Evidence 'v8-discovery-layout.json' $layout
Expect-Rejection { Assert-V8OwnArtifacts $v8Directory $evidenceContext $started }; $checks++; $layout.combinations=$rows; Save-V8Evidence 'v8-discovery-layout.json' $layout
$proofName='v8-discovery-light-520.png.json'; $proof=Get-Content -LiteralPath (Join-Path $v8Directory $proofName) -Raw | ConvertFrom-Json -AsHashtable
foreach ($case in @(@('width',521),@('sha256','bad'),@('realHost',$true),@('schemaVersion','1'))) {
 $old=$proof[$case[0]]; $proof[$case[0]]=$case[1]; Save-V8Evidence $proofName $proof
 Expect-Rejection { Assert-V8OwnArtifacts $v8Directory $evidenceContext $started }; $checks++; $proof[$case[0]]=$old; Save-V8Evidence $proofName $proof
}
$file=Join-Path $v8Directory 'v8-discovery-light-520.png'; $bytes=[IO.File]::ReadAllBytes($file); [IO.File]::WriteAllBytes($file,[byte[]]::new(10))
Expect-Rejection { Assert-V8OwnArtifacts $v8Directory $evidenceContext $started }; $checks++; [IO.File]::WriteAllBytes($file,$bytes)
$stale=Get-Item -LiteralPath $file; $stale.LastWriteTimeUtc=$started.UtcDateTime.AddMinutes(-5)
Expect-Rejection { Assert-V8OwnArtifacts $v8Directory $evidenceContext $started }; $checks++; $stale.LastWriteTimeUtc=[datetime]::UtcNow
# 通过真实 V8 包装入口删除 V7 专有产物与前置场景，验证继承链，恢复原始字节后再验正例。
$previousFile=Join-Path $v8Directory 'v7-library-lifetime.json'; $bytes=[IO.File]::ReadAllBytes($previousFile); Remove-Item -LiteralPath $previousFile
Expect-Rejection { Assert-V8Artifacts $v8Directory $evidenceContext $started }; $checks++; [IO.File]::WriteAllBytes($previousFile,$bytes)
$v7Map.scenarios.Remove('A01'); Save-V7Map
Expect-Rejection { Assert-V8TestMap $path $v8MapPath $v7MapPath $v6MapPath }; $checks++; $v7Map.scenarios.A01=@('Fixture.Pass'); Save-V7Map
Assert-V8Artifacts $v8Directory $evidenceContext $started; $checks++
$entry=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verify-development.ps1') -Raw
foreach ($match in [regex]::Matches($entry, '\$Milestone -in @\(([^)]*)\)')) { if (!$match.Groups[1].Value.Contains("'V8'")) { throw 'V8 遗漏前置分支。' }; $checks++ }
