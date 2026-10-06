param([int]$Port = 5079)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path 'artifacts/combat-editor-checks' ([Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory $root -Force | Out-Null
Copy-Item BaldursGateStyleOblivion/*.json -Destination $root
$config = Join-Path $root 'combat.json'
$actorConfig = Join-Path $root 'actor-classification.json'
$process = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('tools/ActorResearch/bin/Debug/net10.0/ActorResearch.dll','edit','--no-open','--port',$Port,'--config',$actorConfig) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'editor.log') -RedirectStandardError (Join-Path $root 'error.log')
try {
    $url="http://127.0.0.1:$Port"
    for($i=0;$i -lt 50;$i++) { try {$page=Invoke-WebRequest "$url/combat";break} catch {Start-Sleep -Milliseconds 200} }
    if(-not $page -or $page.Content -notmatch 'const token=("[A-F0-9]+")') { throw 'Combat page/token missing.' }
    $token=$Matches[1] | ConvertFrom-Json
    if($page.Content -notmatch 'aria-current="page">Combat workbench') { throw 'Navigation does not select combat.' }
    $headers=@{Origin=$url;'X-Actor-Editor-Token'=$token}
    function Post($route,$body) { Invoke-RestMethod "$url/api/combat/$route" -Method Post -ContentType 'application/json' -Headers $headers -Body ($body | ConvertTo-Json -Depth 15) }
    $data=Invoke-RestMethod "$url/api/combat"
    if($data.Catalog.Items.Count -lt 2000 -or $data.Catalog.Actors.Count -lt 3000) {throw 'Combat catalog coverage missing.'}
    $fighter=@{Weapon='000C0C:Oblivion.esm';Armor=@();Style='Aggressive Fighter';Health=140;MaxFatigue=200;Tier=3;StartingFatiguePercent=100;ConditionPercent=100;Strength=50;WeaponSkill=50;LightArmorSkill=50;HeavyArmorSkill=50;BlockSkill=50;Luck=50;FatigueRegen=5;DamageMultiplier=1}
    $scenario=@{Player=$fighter;Enemy=$fighter;EnemyCount=1;Seconds=60}
    $baseline=Post preview @{Settings=$data.Settings;Scenario=$scenario}
    if($baseline.Proposed.PlayerHitFactors.WeaponMultiplier -ne 3 -or $baseline.Proposed.PlayerHitFactors.BenchmarkMultiplier -ne 1) {throw 'Playable engine settings missing from preview.'}
    $data.Settings.Materials.Iron.Damage=1.5
    $changed=Post preview @{Settings=$data.Settings;Scenario=$scenario}
    if($changed.Proposed.PlayerCleanHit -le $changed.Current.PlayerCleanHit) {throw 'Live material preview failed.'}
    $saved=Post config @{Json=($data.Settings | ConvertTo-Json -Depth 15);Revision=$data.Revision}
    if((Get-Content $config -Raw | ConvertFrom-Json).Materials.Iron.Damage -ne 1.5) {throw 'Save failed.'}
    try {Post config @{Json=($data.Settings | ConvertTo-Json -Depth 15);Revision=$data.Revision} | Out-Null;throw 'Stale revision accepted.'}
    catch {if($_.Exception.Response.StatusCode.value__ -ne 409){throw}}
    $hash=(Get-FileHash $config).Hash
    try {Post config @{Json='{"Styles":null}';Revision=$saved.Revision} | Out-Null;throw 'Invalid config accepted.'}
    catch {if($_.Exception.Response.StatusCode.value__ -ne 400){throw}}
    if((Get-FileHash $config).Hash -ne $hash){throw 'Invalid save modified config.'}
    $scenario.EnemyCount=0
    try {Post preview @{Settings=$data.Settings;Scenario=$scenario} | Out-Null;throw 'Invalid scenario accepted.'}
    catch {if($_.Exception.Response.StatusCode.value__ -ne 400){throw}}
    try {Invoke-RestMethod "$url/api/combat/config" -Method Post -ContentType 'application/json' -Body '{}' | Out-Null;throw 'Unauthenticated write accepted.'}
    catch {if($_.Exception.Response.StatusCode.value__ -ne 403){throw}}
    Write-Output 'Combat editor catalog, preview, save, stale revisions, invalid input, and write protection passed.'
}
finally {if(-not $process.HasExited){Stop-Process -Id $process.Id}}
