param([Parameter(Mandatory)][string]$DataFolder,[Parameter(Mandatory)][string]$LoadOrder)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path 'artifacts/combat-native-checks' ([Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory $root -Force | Out-Null
$runtime=Join-Path $root 'bin'
& dotnet build tests/Combat/Combat.Tests.csproj --no-restore -p:UseAppHost=false "-p:OutputPath=$runtime/"
if($LASTEXITCODE -ne 0){throw 'Combat test build failed'}
$config=Get-Content BaldursGateStyleOblivion/combat.json -Raw | ConvertFrom-Json
$config.Gameplay.NormalizeEquipment=$false; $config.Gameplay.BalanceEnchantedPhysicalStats=$false; $config.Gameplay.BalanceActors=$false; $config.Gameplay.BalanceGameSettings=$false
$config.Materials.Iron.Damage=1.5; $config.Materials.Iron.Armor=1.15
$config | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $root 'combat.json')
$settings=@{EnableActorClassification=$false;EnableActorDeleveling=$false;EnableCreatureListDeleveling=$false;EnableGeographicDiscovery=$false;EnableEquipmentDistribution=$false;EnableWorldLoot=$false;EnableMerchantStock=$false;EnableQuestRewards=$false;EnableDiagnostics=$false;EnableRecordDiscovery=$false;EnablePhysicalCombatAnalysis=$true;EnablePhysicalCombatBalance=$true;CombatConfigurationFile=(Join-Path $root 'combat.json');ReportOnly=$true}
foreach($case in @('report-only','apply')) {
    $folder=Join-Path $root $case;New-Item -ItemType Directory $folder -Force | Out-Null
    $settings.ReportOnly=$case -eq 'report-only';$settings | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $folder 'settings.json')
    & dotnet (Join-Path $runtime 'BaldursGateStyleOblivion.dll') run-patcher -g Oblivion -d $DataFolder -l $LoadOrder -e $folder -o (Join-Path $folder 'Combat.esp') > (Join-Path $folder 'run.log')
    if($LASTEXITCODE -ne 0){throw "$case patcher failed"}
    & dotnet (Join-Path $runtime 'Combat.Tests.dll') (Join-Path $folder 'Reports/Combat.physical-combat.json') (Join-Path $folder 'Combat.esp')
    if($LASTEXITCODE -ne 0){throw "$case candidate validation failed"}
    if($case -eq 'report-only' -and (Get-Item (Join-Path $folder 'Combat.esp')).Length -ne 38){throw 'Report-only patch is not empty'}
}
Write-Output 'Physical combat report-only and native application checks passed.'
