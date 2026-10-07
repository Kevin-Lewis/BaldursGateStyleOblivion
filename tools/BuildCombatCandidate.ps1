param(
    [string]$DataFolder='F:\SteamLibrary\steamapps\common\Oblivion\Data',
    [string]$LoadOrder=(Join-Path $env:LOCALAPPDATA 'Oblivion\Plugins.txt'),
    [switch]$Install,
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$folder=Join-Path $root 'artifacts\combat-first-pass'
$candidate=Join-Path $folder 'BaldursGateStyleOblivion.esp'
$runtime=Join-Path $folder 'build'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$settingsPath=Join-Path $folder 'settings.json'
if(-not (Test-Path -LiteralPath $settingsPath)){
    $settings=@{ReportOnly=$false;EnablePhysicalCombatAnalysis=$true;EnablePhysicalCombatBalance=$true;EnableDiagnostics=$false;EnableRecordDiscovery=$false}
    foreach($entry in @{
        ActorConfigurationFile='actor-classification.json';CreatureListConfigurationFile='creature-lists.json';
        GeographicConfigurationFile='geography.json';DungeonConfigurationFile='dungeons.json';
        EquipmentConfigurationFile='equipment.json';LootConfigurationFile='loot.json';
        MerchantConfigurationFile='merchants.json';RewardConfigurationFile='rewards.json';CombatConfigurationFile='combat.json';MagicConfigurationFile='magic.json';EnchantmentConfigurationFile='enchantments.json';AlchemyConfigurationFile='alchemy.json';CreationConfigurationFile='creation.json'
    }.GetEnumerator()){$settings[$entry.Key]=Join-Path $root ('BaldursGateStyleOblivion\'+$entry.Value)}
    [IO.File]::WriteAllText($settingsPath,($settings | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
}
$candidateSettings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settingsUpdated=$false
foreach($entry in @{EnchantmentConfigurationFile='enchantments.json';AlchemyConfigurationFile='alchemy.json';CreationConfigurationFile='creation.json'}.GetEnumerator()){
    if(-not $candidateSettings.PSObject.Properties[$entry.Key] -or [string]::IsNullOrWhiteSpace($candidateSettings.($entry.Key))){
        $candidateSettings | Add-Member -NotePropertyName $entry.Key -NotePropertyValue (Join-Path $root ('BaldursGateStyleOblivion\'+$entry.Value)) -Force
        $settingsUpdated=$true
    }
}
if($settingsUpdated){[IO.File]::WriteAllText($settingsPath,($candidateSettings | ConvertTo-Json),[Text.UTF8Encoding]::new($false))}
if(-not $SkipBuild){
    & dotnet build BaldursGateStyleOblivion/BaldursGateStyleOblivion.csproj -p:UseAppHost=false "-p:OutputPath=$runtime/"
    if($LASTEXITCODE -ne 0){throw 'Build failed.'}
    & dotnet (Join-Path $runtime 'BaldursGateStyleOblivion.dll') run-patcher -g Oblivion -d $DataFolder -l $LoadOrder -e $folder -o $candidate
    if($LASTEXITCODE -ne 0){throw 'Candidate generation failed.'}
    $editorReports=Join-Path $root 'artifacts\phase0-data\Reports'
    New-Item -ItemType Directory -Path $editorReports -Force | Out-Null
    foreach($suffix in @('character-creation.json','enhancements.json','magic-analysis.json','magic-analysis.md','magic-gameplay.json')){
        $magicReport=Join-Path $folder ('Reports\BaldursGateStyleOblivion.'+$suffix)
        if(Test-Path -LiteralPath $magicReport){Copy-Item -LiteralPath $magicReport -Destination $editorReports -Force}
    }
    Copy-Item -LiteralPath (Join-Path $folder 'Reports\BaldursGateStyleOblivion.physical-combat.json') -Destination (Join-Path $editorReports 'BaldursGateStyleOblivion.physical-combat.json') -Force
}
if(-not (Test-Path -LiteralPath $candidate)){throw 'Candidate file is missing.'}
if($Install){
    if(Get-Process -Name Oblivion -ErrorAction SilentlyContinue){throw 'Close Oblivion before installing the candidate.'}
    $backup=Join-Path $folder ('backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    $installed=Join-Path ([IO.Path]::GetFullPath($DataFolder)) 'BaldursGateStyleOblivion.esp'
    if(Test-Path -LiteralPath $installed){Copy-Item -LiteralPath $installed -Destination $backup}
    Copy-Item -LiteralPath $LoadOrder -Destination (Join-Path $backup 'Plugins.txt')
    Copy-Item -LiteralPath $candidate -Destination $installed -Force
    $plugins=[IO.File]::ReadAllLines($LoadOrder)
    if(-not ($plugins | Where-Object {$_.Trim().TrimStart('*') -eq 'BaldursGateStyleOblivion.esp'})){
        [IO.File]::WriteAllLines($LoadOrder,($plugins+@('BaldursGateStyleOblivion.esp')),[Text.UTF8Encoding]::new($false))
    }
    Write-Output "Installed candidate. Previous plugin/load order backup: $backup"
}
Write-Output "Candidate: $candidate"
