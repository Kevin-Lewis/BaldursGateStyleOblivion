param(
    [string]$Reports = 'artifacts/phase0-data/Reports',
    [string]$Output = 'artifacts/merchant-review',
    [string]$RewardConfig = 'BaldursGateStyleOblivion/rewards.json'
)
$ErrorActionPreference = 'Stop'
$inputs = [System.Collections.Generic.List[object]]::new()
function Read-Report($name) {
    $path = Join-Path $Reports "BaldursGateStyleOblivion.$name.json"
    $inputs.Add([ordered]@{ File = [IO.Path]::GetFullPath($path); LastWriteUtc = (Get-Item -LiteralPath $path).LastWriteTimeUtc })
    (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).Records
}
function Get-Identity($row) { if ($row.Record) { $row.Record } else { $row } }
function Index-Rows($rows) {
    $index = @{}
    foreach ($row in $rows) { $index[(Get-Identity $row).FormKey] = $row }
    $index
}
$merchants = @(Read-Report merchants)
$npcs = Index-Rows (Read-Report npcs)
$lists = Index-Rows (Read-Report leveled-items)
$containers = Index-Rows (Read-Report containers)
$classes = Index-Rows (Read-Report classes)
$factions = Index-Rows (Read-Report factions)
$references = Index-Rows (Read-Report reference-index)
$inventories = Index-Rows (Read-Report actor-inventories)
$equipment = Index-Rows (@(Read-Report weapons) + @(Read-Report armor))
$artifacts = (Get-Content -LiteralPath $RewardConfig -Raw | ConvertFrom-Json).Artifacts
function Label($key) {
    foreach ($index in @($npcs, $lists, $containers, $classes, $factions, $equipment)) {
        if ($key -and $index.ContainsKey($key)) { return (Get-Identity $index[$key]).EditorID }
    }
    $key
}
$owners = @{}
foreach ($merchant in $merchants) {
    foreach ($placement in $merchant.Original.Placements) {
        if ($placement.ContainerBase) { $owners[$placement.ContainerBase] = @($owners[$placement.ContainerBase]) + $merchant.Record.FormKey }
    }
}
function Visit-Stock($key, $path, $reached, $gated, $rare, $protected, $warnings) {
    $reached[$key] = $true
    if ($artifacts.PSObject.Properties[$key]) { $protected[$key] = $true }
    if ($equipment.ContainsKey($key)) {
        $id = (Get-Identity $equipment[$key]).EditorID
        foreach ($material in @('Glass', 'Ebony', 'Daedric')) {
            if ($id -match $material) { $rare[$material][$key] = $id }
        }
    }
    if (-not $lists.ContainsKey($key)) { return }
    if ($path.Count -gt 100) { $warnings.Add('List nesting exceeds 100; inspect manually.'); return }
    if ($path.ContainsKey($key)) { $warnings.Add("List cycle: $key"); return }
    $path[$key] = $true
    foreach ($entry in $lists[$key].Original.Entries) {
        if ($entry.Level -gt 1) { $gated[$key] = $true }
        Visit-Stock $entry.Reference $path $reached $gated $rare $protected $warnings
    }
    $path.Remove($key)
}
$rows = @(foreach ($merchant in $merchants | Sort-Object { $_.Record.EditorID }) {
    $key = $merchant.Record.FormKey
    $actor = $npcs[$key]
    $services = @($merchant.Original.Services -split ', ' | Where-Object { $_ -and $_ -ne '0' })
    $sales = @($services | Where-Object { $_ -notin @('Training', 'Repair', 'Recharge') })
    $stock = @($merchant.Original.Placements.ContainerBase | Where-Object { $_ } | Sort-Object -Unique)
    $class = $actor.Original.Class
    $actorFactions = @($actor.Original.Factions | ForEach-Object { [ordered]@{ FormKey = $_.FormKey; EditorID = Label $_.FormKey; Rank = $_.Rank } })
    $evidence = (@($stock | ForEach-Object { Label $_ }) + @(Label $class) + @($actorFactions.EditorID)) -join ' '
    $profile = 'General Store'; $reason = 'Broad sale services; specialization requires review.'
    if (-not $sales.Count -and -not $stock.Count) { $profile = 'Service only'; $reason = 'No sale services or linked stock recorded.' }
    elseif ($evidence -match 'Publican') { $profile = 'Specialist'; $reason = 'Publican class/stock identity; food and drink rather than an alchemy shop.' }
    elseif ($evidence -match 'Alchemist' -or ('Potions' -in $services -and 'Ingredients' -in $services -and 'Weapons' -notin $services -and 'Armor' -notin $services -and 'Spells' -notin $services)) { $profile = 'Alchemist'; $reason = 'Alchemy identity or focused potion and ingredient services.' }
    elseif ('Spells' -in $services) { $profile = 'Mage Vendor'; $reason = 'Spell sale service; spell eligibility requires separate review.' }
    elseif ($evidence -match 'Jeweler|Jewelry') { $profile = 'Jeweler'; $reason = 'Jewelry identity in class or linked stock.' }
    elseif ('Weapons' -in $services -and 'Armor' -in $services -and $sales.Count -le 4) { $profile = 'Professional Blacksmith'; $reason = 'Focused equipment services; expertise and material access require review.' }
    elseif ($evidence -match 'Guild' -and $sales.Count -le 4) { $profile = 'Guild Vendor'; $reason = 'Guild evidence with limited sale services; verify vendor role.' }
    elseif ($sales.Count -le 2) { $profile = 'Specialist'; $reason = 'Narrow sale services; verify specialization.' }
    $reached = @{}; $gated = @{}; $protected = @{}
    $rare = @{ Glass = @{}; Ebony = @{}; Daedric = @{} }
    $warnings = [System.Collections.Generic.List[string]]::new()
    $branches = @([ordered]@{ Source = $key; Kind = 'Personal inventory: not automatically sale stock'; Items = @($merchant.Original.Inventory) })
    foreach ($container in $stock) {
        $branches += [ordered]@{ Source = $container; EditorID = Label $container; Kind = 'Linked merchant container'; Items = @($containers[$container].Original.Items) }
        if (@($owners[$container] | Sort-Object -Unique).Count -gt 1) { $warnings.Add("Shared stock base: $container") }
        if ($containers[$container].Original.Script) { $warnings.Add("Scripted stock container: $container") }
    }
    foreach ($branch in $branches) {
        $branchReached = @{}; $branchGated = @{}; $branchProtected = @{}; $branchRare = @{ Glass = @{}; Ebony = @{}; Daedric = @{} }
        foreach ($entry in $branch.Items) {
            Visit-Stock $entry.Reference @{} $reached $gated $rare $protected $warnings
            Visit-Stock $entry.Reference @{} $branchReached $branchGated $branchRare $branchProtected $warnings
        }
        $branch['LevelGatedLists'] = @($branchGated.Keys | Sort-Object)
        $branch['ReachableRareEquipment'] = $branchRare
        $branch['ProtectedItems'] = @($branchProtected.Keys | Sort-Object)
    }
    if (-not $stock.Count -and $profile -ne 'Service only') { $warnings.Add('No linked stock container; personal inventory and services require review.') }
    if ($inventories[$key].Original.Script) { $warnings.Add('Merchant has an attached script.') }
    $locations = @()
    foreach ($placement in $merchant.Original.Placements) {
        if ($placement.MerchantContainerReference -and -not $placement.LinkResolvedToContainer) { $warnings.Add('Unresolved stock-container link.') }
        $locations += @($references[$placement.PlacedNPC.FormKey].Original.UsedBy | Where-Object { $_.RecordType -eq 'Cell' })
    }
    $listRows = @(foreach ($list in $reached.Keys | Where-Object { $lists.ContainsKey($_) } | Sort-Object) {
        $users = @($references[$list].Original.UsedBy | Where-Object { $_ })
        $outside = @($users | Where-Object { -not $reached.ContainsKey($_.FormKey) -and $_.FormKey -notin $stock -and $_.FormKey -ne $key })
        [ordered]@{ FormKey = $list; EditorID = Label $list; LevelGated = $gated.ContainsKey($list)
            Flags = $lists[$list].Original.Flags; ChanceNone = $lists[$list].Original.ChanceNone
            EntryCount = @($lists[$list].Original.Entries).Count; UsedByCount = $users.Count
            OutsideReachedGraphCount = $outside.Count; OutsideReachedGraphSample = @($outside | Select-Object -First 12) }
    })
    [ordered]@{ FormKey = $key; Name = $actor.Name; EditorID = $merchant.Record.EditorID; SourcePlugin = $merchant.Record.SourcePlugin
        ProposedProfile = $profile; Reason = $reason; Services = $services; BarterGold = $merchant.Original.BarterGold
        Class = [ordered]@{ FormKey = $class; EditorID = Label $class }; Factions = $actorFactions; Locations = @($locations | Sort-Object FormKey -Unique)
        Placements = @($merchant.Original.Placements); Branches = $branches; Lists = $listRows; LevelGatedLists = $gated.Count
        ReachableRareEquipment = $rare; ProtectedItems = @($protected.Keys | Sort-Object); Warnings = @($warnings | Sort-Object -Unique) }
})
$limits = @('Existing discovery snapshots, not a fresh load-order run.', 'Personal inventory includes worn/carried goods; reachable items are candidates, not guaranteed sale stock.', 'Reachability is not per-refresh probability; list levels, flags, counts and ChanceNone still apply.', 'Profiles are suggestions from records; barter gold is not proof of wealth.', 'Material detection uses equipment EditorIDs. Other item types and script-generated stock require further inspection.', 'Location evidence uses direct Cell references. Spell eligibility requires a separate audit.')
New-Item -ItemType Directory -Path $Output -Force | Out-Null
[ordered]@{ ProposalOnly = $true; ModifiedRecords = 0; Inputs = @($inputs.ToArray()); RewardConfiguration = [IO.Path]::GetFullPath($RewardConfig); Limitations = $limits; Merchants = $rows } | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $Output 'merchants.json') -Encoding UTF8
$roster = @('# Merchant identity and stock audit', '', 'Record-based proposals. No gameplay changes.', '', '| Merchant | Proposed profile | Stock links | Level-gated lists |', '|---|---|---:|---:|')
$html = [Text.StringBuilder]::new('<!doctype html><meta charset="utf-8"><title>Merchant audit</title><style>body{font:15px system-ui;max-width:1200px;margin:30px auto;padding:20px;background:#171c23;color:#dee5ef}summary{cursor:pointer;padding:12px}details{border:1px solid #465266;margin:8px 0}pre{white-space:pre-wrap;overflow-wrap:anywhere;padding:15px}input{padding:10px;width:95%}</style><h1>Merchant identity and stock audit</h1><p>Existing discovery snapshots. Profiles are suggestions. No inventories changed.</p><input aria-label="Filter merchants" placeholder="Filter merchants, profiles, stock or factions" oninput="for(const d of document.querySelectorAll(''details''))d.hidden=!d.textContent.toLowerCase().includes(this.value.toLowerCase())">')
foreach ($row in $rows) {
    $name = if ($row.Name) { $row.Name } else { $row.EditorID }
    $roster += "| $($name.Replace('|', '/')) ($($row.FormKey)) | $($row.ProposedProfile) | $(@($row.Placements.ContainerBase | Where-Object { $_ } | Sort-Object -Unique).Count) | $($row.LevelGatedLists) |"
    [void]$html.Append('<details><summary>').Append([Net.WebUtility]::HtmlEncode("$name — $($row.ProposedProfile)")).Append('</summary><pre>').Append([Net.WebUtility]::HtmlEncode(($row | ConvertTo-Json -Depth 40))).Append('</pre></details>')
}
$roster | Set-Content -LiteralPath (Join-Path $Output 'merchants.md') -Encoding UTF8
$html.ToString() | Set-Content -LiteralPath (Join-Path $Output 'merchants.html') -Encoding UTF8
$rows | ForEach-Object { [pscustomobject]$_ } | Group-Object ProposedProfile | Sort-Object Name | Select-Object Name, Count | Format-Table
Write-Output "Merchant audit: $($rows.Count) actors; $Output"




