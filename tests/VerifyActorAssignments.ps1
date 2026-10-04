param(
    [Parameter(Mandatory)][string]$DataFolder,
    [Parameter(Mandatory)][string]$LoadOrder
)
$ErrorActionPreference = 'Stop'
$folder = Join-Path "$PSScriptRoot/../artifacts/actor-assignment-checks" ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $folder -Force | Out-Null
@{ IncludedPlugins = @('DLCFrostcrag.esp'); EnableRecordDiscovery = $false } | ConvertTo-Json | Set-Content "$folder/settings.json"
'{"Includes":["groups.json","overrides.json"]}' | Set-Content "$folder/actor-classification.json"
'{"Rules":[{"Id":"Fallback test","Priority":"Fallback","Evidence":"Always","Values":{"PowerTier":1}},{"Id":"Daedra test","Priority":"RaceCreature","Evidence":"CreatureType","Match":"Daedra","Values":{"PowerTier":4,"Handling":"Generic","ActorNature":"Daedric"},"Reason":"Creature classification rule"}]}' | Set-Content "$folder/groups.json"
'{"FormKeyOverrides":{"000D26:DLCFrostcrag.esp":{"PowerTier":null,"Reason":"Research proposal","Description":"Test description"}}}' | Set-Content "$folder/overrides.json"
$patcher = "$PSScriptRoot/../BaldursGateStyleOblivion/bin/Debug/net10.0/BaldursGateStyleOblivion.exe"
function Run-Assignment {
    & $patcher run-patcher -g Oblivion -d $DataFolder -l $LoadOrder -e $folder -o "$folder/Test.esp" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Patcher failed' }
    $bytes = [IO.File]::ReadAllBytes("$folder/Test.esp")
    if ($bytes.Length -ne 38 -or [BitConverter]::ToUInt32($bytes, 30) -ne 0) { throw 'Assignment workflow modified gameplay' }
    return (Get-Content "$folder/Reports/Test.actor-classifications.json" -Raw | ConvertFrom-Json).Actors
}
$rows = Run-Assignment
$actor = $rows | Where-Object FormKey -eq '000D26:DLCFrostcrag.esp'
if ($actor.Profile.Dimensions.PowerTier.Selected.Value -ne 4) { throw 'Empty override affected group assignment' }
'{"FormKeyOverrides":{"000D26:DLCFrostcrag.esp":{"PowerTier":6,"Reason":"My selected tier","Description":"Test description"}}}' | Set-Content "$folder/overrides.json"
$rows = Run-Assignment
$actor = $rows | Where-Object FormKey -eq '000D26:DLCFrostcrag.esp'
if ($actor.Profile.Dimensions.PowerTier.Selected.Value -ne 6 -or $actor.Profile.Dimensions.PowerTier.Selected.Reason -ne 'My selected tier') { throw 'Direct override failed' }
if ($actor.Profile.Dimensions.ActorNature.Selected.Value -ne 'Daedric' -or $actor.Profile.Dimensions.Handling.Selected.Value -ne 'Generic') { throw 'Partial override lost other fields' }
if (!($rows | Where-Object { $_.Profile.Dimensions.PowerTier.Selected.Value -eq 1 })) { throw 'Fallback tier missing' }
Write-Output 'Live actor assignments passed: grouped rules, fallback, empty override, direct edit, explanations, and empty ESP.'
