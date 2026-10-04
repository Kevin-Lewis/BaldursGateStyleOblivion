param(
    [string]$ReportFolder = "$PSScriptRoot\..\artifacts\phase0-data\Reports",
    [string]$PatcherPath = "$PSScriptRoot\..\BaldursGateStyleOblivion\bin\Debug\net10.0\BaldursGateStyleOblivion.dll"
)
$ErrorActionPreference = 'Stop'
$ReportFolder = (Resolve-Path $ReportFolder).Path
$stem = 'BaldursGateStyleOblivion'
$summary = Get-Content "$ReportFolder\$stem.discovery-summary.json" -Raw | ConvertFrom-Json
foreach ($group in $summary.RecordCounts.PSObject.Properties) {
    $rows = @((Get-Content "$ReportFolder\$stem.$($group.Name).json" -Raw | ConvertFrom-Json).Records)
    if ($rows.Count -ne $group.Value) { throw "Incorrect count: $($group.Name)" }
    $keys = @($rows | ForEach-Object {
        if ($group.Name -eq 'script-candidates') { $_.Record.FormKey + '/' + $_.Original.Context }
        else { $_.Record.FormKey }
    })
    if (@($keys | Sort-Object -Unique).Count -ne $rows.Count) { throw "Duplicate subjects: $($group.Name)" }
}
$inventory = (Get-Content "$ReportFolder\$stem.actor-inventories.json" -Raw | ConvertFrom-Json).Records
$actors = Get-Content "$ReportFolder\$stem.actors.json" -Raw | ConvertFrom-Json
if ($inventory.Count -ne $actors.NPCs.Count + $actors.Creatures.Count) { throw 'Incomplete inventories' }
$audit = (Get-Content "$ReportFolder\$stem.scaling-audit.json" -Raw | ConvertFrom-Json).Records
$scaled = Get-Content "$ReportFolder\$stem.scaled-actors.json" -Raw | ConvertFrom-Json
if (@($audit | Where-Object { $_.Original.LevelMode -eq 'PlayerLevelOffset' }).Count -ne $scaled.NPCs.Count + $scaled.Creatures.Count) { throw 'Actor scaling count mismatch' }
$index = (Get-Content "$ReportFolder\$stem.reference-index.json" -Raw | ConvertFrom-Json).Records
$chest = $index | Where-Object { $_.Record.FormKey -eq '00124C:DLCFrostcrag.esp' }
if ('00124D:DLCFrostcrag.esp' -notin $chest.Original.UsedBy.FormKey) { throw 'Merchant chest usage missing' }
$merchant = (Get-Content "$ReportFolder\$stem.merchants.json" -Raw | ConvertFrom-Json).Records |
    Where-Object { $_.Record.EditorID -eq 'Aurelinwae' }
if ($merchant.Original.Placements.ContainerBase -notcontains $chest.Record.FormKey) { throw 'Merchant link incorrect' }

$dllFolder = Split-Path (Resolve-Path $PatcherPath).Path
[AppDomain]::CurrentDomain.add_AssemblyResolve({ param($sender, $eventArgs)
    $file = Join-Path $dllFolder (($eventArgs.Name.Split(',')[0]) + '.dll')
    if (Test-Path $file) { [Reflection.Assembly]::LoadFrom($file) }
})
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $PatcherPath).Path)
$scanner = $assembly.GetType('BaldursGateStyleOblivion.Discovery.ScriptDiscovery').GetMethod('Scan')
$source = @('; player.GetLevel', 'Message "player.GetLevel; AddItem"',
    'if Player.GetLevel >= 5 ; other.GetLevel', 'other.GetLevel',
    'player.AddItem Gold001 10', 'player.AddItemNS SomeItem 1', 'SetStage SomeQuest 10') -join "`n"
$signals = $scanner.Invoke($null, [object[]]@($source))
if ($signals.Count -ne 5 -or $signals[0].Line -ne 3 -or $signals[0].Kind -ne 'PlayerLevelRead' -or $signals[1].Kind -ne 'ActorLevelRead') { throw 'Source scanner failed' }
$cycles = $assembly.GetType('BaldursGateStyleOblivion.Discovery.RecordDiscovery').GetMethod('FindCycles', [Reflection.BindingFlags]'NonPublic,Static')
$graph = [Activator]::CreateInstance($cycles.GetParameters()[0].ParameterType)
$a = [Mutagen.Bethesda.Plugins.FormKey]::Factory('000001:Test.esp')
$b = [Mutagen.Bethesda.Plugins.FormKey]::Factory('000002:Test.esp')
$c = [Mutagen.Bethesda.Plugins.FormKey]::Factory('000003:Test.esp')
$graph.Add($a, [Mutagen.Bethesda.Plugins.FormKey[]]@($b, $c))
$graph.Add($b, [Mutagen.Bethesda.Plugins.FormKey[]]@($c))
$graph.Add($c, [Mutagen.Bethesda.Plugins.FormKey[]]@())
if ($cycles.Invoke($null, [object[]]@($graph)).Length -ne 0) { throw 'Acyclic diamond reported as cycle' }
$graph[$c] = [Mutagen.Bethesda.Plugins.FormKey[]]@($a)
if ($cycles.Invoke($null, [object[]]@($graph)).Length -eq 0) { throw 'Cycle not detected' }
Write-Output 'Discovery checks passed: coverage, merchant/reference links, source scanning, and cycle detection.'
