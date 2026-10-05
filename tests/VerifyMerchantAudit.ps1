$ErrorActionPreference = 'Stop'
$fixture = Join-Path 'artifacts/merchant-audit-test' ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
function Report($name, $rows) { @{ Records = @($rows) } | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $fixture "BaldursGateStyleOblivion.$name.json") -Encoding UTF8 }
function Identity($key, $id, $type) { @{ FormKey = $key; EditorID = $id; RecordType = $type; SourcePlugin = 'Fixture.esm' } }
$npc = '000001:Fixture.esm'; $service = '000002:Fixture.esm'; $container = '000003:Fixture.esm'; $root = '000004:Fixture.esm'; $child = '000005:Fixture.esm'; $item = '000006:Fixture.esm'
$placement = @{ PlacedNPC = (Identity '000007:Fixture.esm' 'ShopRef' 'PlacedNpc'); ContainerBase = $container; MerchantContainerReference = '000008:Fixture.esm'; LinkResolvedToContainer = $true }
Report merchants @(@{ Record = (Identity $npc 'Trader' 'Npc'); Original = @{ Services = 'Weapons, Armor, Repair'; Inventory = @(); Placements = @($placement) } }, @{ Record = (Identity $service 'Repairer' 'Npc'); Original = @{ Services = 'Repair'; Inventory = @(); Placements = @() } })
Report npcs @(@{ FormKey = $npc; EditorID = 'Trader'; Name = 'Trader'; Original = @{} }, @{ FormKey = $service; EditorID = 'Repairer'; Name = 'Repairer'; Original = @{} })
Report containers @(@{ FormKey = $container; EditorID = 'ShopStock'; Original = @{ Items = @(@{ Reference = $root; Count = 1 }) } })
Report leveled-items @(@{ FormKey = $root; EditorID = 'Root'; Original = @{ Entries = @(@{ Reference = $child; Level = 10; Count = 1 }) } }, @{ FormKey = $child; EditorID = 'Child'; Original = @{ Entries = @(@{ Reference = $root; Level = 1; Count = 1 }, @{ Reference = $item; Level = 1; Count = 1 }) } })
Report weapons @(@{ FormKey = $item; EditorID = 'DaedricSword'; RecordType = 'Weapon' })
foreach ($name in @('armor', 'classes', 'factions', 'reference-index', 'actor-inventories')) { Report $name @() }
@{ Artifacts = @{ $item = @{ Unique = $true } } } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixture 'rewards.json') -Encoding UTF8
& tools/ActorResearch/AuditMerchants.ps1 -Reports $fixture -Output (Join-Path $fixture 'output') -RewardConfig (Join-Path $fixture 'rewards.json')
$result = Get-Content (Join-Path $fixture 'output/merchants.json') -Raw | ConvertFrom-Json
$trader = $result.Merchants | Where-Object FormKey -eq $npc
$repairer = $result.Merchants | Where-Object FormKey -eq $service
if ($result.ModifiedRecords -ne 0 -or $result.Merchants.Count -ne 2) { throw 'Unexpected audit scope.' }
if ($trader.ProposedProfile -ne 'Professional Blacksmith' -or $repairer.ProposedProfile -ne 'Service only') { throw 'Service/profile classification failed.' }
if ($trader.LevelGatedLists -ne 1 -or $trader.Warnings -notcontains "List cycle: $root") { throw 'Nested gating/cycle detection failed.' }
if ($trader.ProtectedItems -notcontains $item -or -not $trader.ReachableRareEquipment.Daedric.PSObject.Properties[$item]) { throw 'Artifact/material tracing failed.' }
$stock = $trader.Branches | Where-Object Kind -eq 'Linked merchant container'
if ($stock.LevelGatedLists -notcontains $root -or $stock.ProtectedItems -notcontains $item) { throw 'Stock/personal inventory separation failed.' }
Write-Output 'Merchant audit fixture checks passed.'
