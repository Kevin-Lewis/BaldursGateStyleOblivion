using System.Text.Json;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;
namespace BaldursGateStyleOblivion.Modules;

internal static class WorldLoot
{
    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, PatcherRun run)
    {
        var config = Path.Combine(run.DataDirectory, "loot.json");
        if (!File.Exists(config)) config = Path.Combine(AppContext.BaseDirectory, "loot.json");
        if (!string.IsNullOrWhiteSpace(run.Settings.LootConfigurationFile)) config = Path.GetFullPath(run.Settings.LootConfigurationFile, run.DataDirectory);
        var settings = LootConfiguration.Load(config);
        var records = new Dictionary<FormKey, IMajorRecordGetter>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(listing => listing.Enabled && listing.Mod is not null && run.IsInputPlugin(listing.ModKey)))
            foreach (var record in listing.Mod!.EnumerateMajorRecords()) records.TryAdd(record.FormKey, record);
        records = records.Where(pair => !pair.Value.IsDeleted).ToDictionary(pair => pair.Key, pair => pair.Value);
        var geography = ReadGeography(run);
        var builder = new LootPoolBuilder(records, settings, state.PatchMod.ModKey, state.PatchMod.ModHeader.Stats.NextFormID, run.Includes, RewardRecords.Protected(RewardConfiguration.Load(RewardRecords.PathFor(run))));
        var changes = new Dictionary<FormKey, string[]>();
        var rows = new List<object>();
        var openingOdds = new Dictionary<FormKey, (double Rare, double Premium, double Glass, double Value)>();
        var redirects = new Dictionary<FormKey, FormKey>();
        var containers = new Dictionary<(FormKey, string), FormKey>();
        var merchantReferences = records.Values.OfType<IPlacedNpcGetter>().Select(npc => npc.MerchantContainer.FormKeyNullable).OfType<FormKey>().ToHashSet();
        var scriptFields = QuestRewards.ScriptContexts(records.Values).Select(context => context.Fields).ToArray();
        var scriptReferences = scriptFields.SelectMany(fields => fields.EnumerateFormLinks()).Select(link => link.FormKey).ToHashSet();
        var unsafeListReferences = UnsafeListReferences(scriptFields);
        var pureEquipment = new Dictionary<FormKey, bool>();
        var scriptedRoots = new Dictionary<FormKey, bool>();
        var modifiedSlots = 0;
        var balanceRows = new List<(string Profile, double Rare, double Premium, double Glass, double Value)>();
        var totals = new Dictionary<string, Dictionary<string, double>>();
        var placementCells = new Dictionary<FormKey, FormKey>();
        foreach (var cell in records.Values.OfType<ICellGetter>())
            foreach (var placed in cell.Persistent.Concat(cell.Temporary).Concat(cell.VisibleWhenDistant)) placementCells.TryAdd(placed.FormKey, cell.FormKey);

        List<object> Plan(IMajorRecordGetter owner, (FormKey Reference, long Count)[] inventory, string selected, string? guard, Action<int, FormKey> apply, bool deathLoot = false)
        {
            var plans = new List<object>();
            var yields = new Dictionary<string, double>();
            double noRare = 1, noPremium = 1, noGlass = 1, value = 0;
            for (var index = 0; index < inventory.Length; index++)
            {
                var item = inventory[index]; var root = item.Reference;
                if (records.GetValueOrDefault(root) is not ILeveledItemGetter list) continue;
                var target = root; var reason = guard ?? "";
                Dictionary<string, double>? expected = null;
                try
                {
                    if (guard is not null) throw new InvalidDataException(guard);
                    if (item.Count is <= 0 or > int.MaxValue) throw new InvalidDataException("Non-standard native inventory count retained.");
                    if (owner is INpcGetter or ICreatureGetter)
                    {
                        var actorGuard = EquipmentDistributionModule.Guard(owner, root, records);
                        if (actorGuard is not null) throw new InvalidDataException(actorGuard);
                        if (!pureEquipment.TryGetValue(root, out var equipmentOnly)) pureEquipment[root] = equipmentOnly = PureEquipment(root, records, []);
                        if (!deathLoot && equipmentOnly) throw new InvalidDataException("Equipment branch remains under Phase 9.");
                    }
                    if (!scriptedRoots.TryGetValue(root, out var scriptLinked)) scriptedRoots[root] = scriptLinked = ScriptTouches(root, unsafeListReferences, records, []);
                    if (scriptLinked) throw new InvalidDataException("Script-mutated or source-unavailable loot branch retained.");
                    var built = builder.Build(root, selected); target = built.Key; reason = built.Reason;
                    expected = builder.Expected(target).ToDictionary(pair => pair.Key, pair => pair.Value * item.Count);
                    noRare *= 1 - builder.Chance(target, (int)item.Count);
                    noPremium *= 1 - builder.Chance(target, (int)item.Count, premium: true);
                    noGlass *= 1 - builder.Chance(target, (int)item.Count, material: "Glass");
                    value += builder.ExpectedValue(target) * item.Count;
                    foreach (var pair in expected) yields[pair.Key] = yields.GetValueOrDefault(pair.Key) + pair.Value;
                    apply(index, target); modifiedSlots++;
                }
                catch (InvalidDataException error) { reason = error.Message; }
                plans.Add(new { Original = root.ToString(), list.EditorID, item.Count, Planned = target.ToString(),
                    Status = root == target ? "Preserved" : run.Settings.ReportOnly ? "Planned" : "Modified", Reason = reason, ExpectedItemYield = expected });
            }
            var previous = openingOdds.GetValueOrDefault(owner.FormKey);
            openingOdds[owner.FormKey] = (1 - (1 - previous.Rare) * noRare, 1 - (1 - previous.Premium) * noPremium, 1 - (1 - previous.Glass) * noGlass, previous.Value + value);
            if (!totals.TryGetValue(selected, out var sum)) totals[selected] = sum = new();
            foreach (var pair in yields) sum[pair.Key] = sum.GetValueOrDefault(pair.Key) + pair.Value;
            return plans;
        }

        object Odds(FormKey key)
        {
            var odds = openingOdds.GetValueOrDefault(key);
            return new { RareChancePercent = odds.Rare * 100, PremiumChancePercent = odds.Premium * 100,
                GlassChancePercent = odds.Glass * 100, ExpectedBaseValue = odds.Value, Scope = "Processed branches only; fixed/preserved loot excluded. Value is base item value, not sale proceeds; soul gems excluded from value estimate." };
        }
        foreach (var placed in records.Values.OfType<IPlacedObjectGetter>().Where(placed => run.Includes(placed.FormKey.ModKey))
                     .OrderBy(placed => placed.FormKey.ToString(), StringComparer.Ordinal))
        {
            if (records.GetValueOrDefault(placed.Base.FormKey) is not IContainerGetter source || !run.Includes(source.FormKey.ModKey)) continue;
            var cellKey = placementCells.GetValueOrDefault(placed.FormKey);
            var cell = records.GetValueOrDefault(cellKey) as ICellGetter;
            var location = geography.GetValueOrDefault(cellKey.ToString());
            var (tier, daedric, locationReason) = Location(location);
            var evidence = $"{source.EditorID} | {source.Name} | {placed.EditorID} | {cell?.EditorID} | {cell?.Name}";
            var selected = LootConfiguration.Select(settings, placed.FormKey.ToString(), source.FormKey.ModKey.ToString(), evidence, tier, daedric);
            if (!settings.Overrides.ContainsKey(placed.FormKey.ToString()) && settings.Overrides.TryGetValue(source.FormKey.ToString(), out var baseOverride))
                selected = (baseOverride.Profile ?? selected.Profile, selected.Category, "Container base override", baseOverride.Preserve);
            var guard = selected.Preserve ? "Configured preservation." : ContainerGuard(source, placed, merchantReferences, scriptReferences, cell);
            if (cell is null) guard ??= "Placement cell unresolved.";
            Container? copy = null;
            void Apply(int index, FormKey key)
            {
                if (copy is null)
                {
                    copy = builder.Patch.Containers.AddNew(); copy.DeepCopyIn(source); copy.EditorID = $"BGSO_LootContainer_{copy.FormKey.ID:X6}";
                }
                copy.Items[index].Item.SetTo(key);
            }
            var cached = containers.GetValueOrDefault((source.FormKey, selected.Profile));
            var plans = Plan(placed, source.Items.Select(item => (item.Item.FormKey, (long)item.Count)).ToArray(), selected.Profile, guard, Apply);
            if (copy is not null)
            {
                if (!cached.IsNull) builder.Patch.Containers.Remove(copy.FormKey);
                else containers[(source.FormKey, selected.Profile)] = cached = copy.FormKey;
                redirects[placed.FormKey] = cached;
            }
            if (plans.Count > 0)
            {
                var odds = openingOdds.GetValueOrDefault(placed.FormKey);
                balanceRows.Add((selected.Profile, odds.Rare, odds.Premium, odds.Glass, odds.Value));
                rows.Add(new { FormKey = placed.FormKey.ToString(), Source = source.FormKey.ToString(), source.EditorID,
                Name = source.Name?.ToString(), Kind = "Container", Cell = cellKey.ToString(), CellName = cell?.Name?.ToString() ?? cell?.EditorID,
                SourcePlugin = source.FormKey.ModKey.ToString(), Tier = tier, DaedricContext = daedric, selected.Category, selected.Profile, selected.Rule,
                Evidence = evidence, LocationReason = locationReason, Odds = Odds(placed.FormKey), Plans = plans });
            }
        }
        // Ordinary carried loot is independent of location. Equipped gear was handled by Phase 9.
        foreach (var actor in records.Values.Where(item => item is INpcGetter or ICreatureGetter).Where(item => run.Includes(item.FormKey.ModKey))
                     .OrderBy(item => item.FormKey.ToString(), StringComparer.Ordinal))
        {
            var tier = profiles.GetValueOrDefault(actor.FormKey)?.Tier?.Value ?? 2;
            var id = actor.EditorID ?? "";
            var selected = LootConfiguration.Select(settings, actor.FormKey.ToString(), actor.FormKey.ModKey.ToString(), id, tier,
                Regex.IsMatch(id, "Dremora|Daedra|Xivilai", RegexOptions.IgnoreCase));
            var inventory = actor is INpcGetter npc ? npc.Items.Select(item => (item.Item.FormKey, (long)(item.Count ?? 1))).ToArray() : ((ICreatureGetter)actor).Items.Select(item => (item.Item.FormKey, (long)(item.Count ?? 1))).ToArray();
            var merchant = actor is INpcGetter trader && (trader.AIData?.BuySellServices ?? 0) != 0;
            var guard = selected.Preserve ? "Configured preservation." : merchant ? "Merchant inventory retained." : null;
            var plans = Plan(actor, inventory, selected.Profile, guard, (index, key) =>
            {
                if (run.Settings.ReportOnly) return;
                if (actor is INpcGetter n) state.PatchMod.Npcs.GetOrAddAsOverride(n).Items[index].Item.SetTo(key);
                else state.PatchMod.Creatures.GetOrAddAsOverride((ICreatureGetter)actor).Items[index].Item.SetTo(key);
                changes[actor.FormKey] = ["Items.Reference"];
            });
            var death = actor is INpcGetter deadNpc ? deadNpc.DeathItem.FormKeyNullable : ((ICreatureGetter)actor).DeathItem.FormKeyNullable;
            if (death is {} deathKey && records.GetValueOrDefault(deathKey) is ILeveledItemGetter)
            {
                plans.AddRange(Plan(actor, [(deathKey, 1)], selected.Profile, guard, (_, key) =>
                {
                    if (run.Settings.ReportOnly) return;
                    if (actor is INpcGetter n) state.PatchMod.Npcs.GetOrAddAsOverride(n).DeathItem.SetTo(key);
                    else state.PatchMod.Creatures.GetOrAddAsOverride((ICreatureGetter)actor).DeathItem.SetTo(key);
                    changes[actor.FormKey] = changes.GetValueOrDefault(actor.FormKey, []).Append("DeathItem").ToArray();
                }, deathLoot: true));
            }
            if (plans.Count > 0)
            {
                var odds = openingOdds.GetValueOrDefault(actor.FormKey);
                balanceRows.Add((selected.Profile, odds.Rare, odds.Premium, odds.Glass, odds.Value));
                rows.Add(new { FormKey = actor.FormKey.ToString(), Source = actor.FormKey.ToString(), actor.EditorID,
                Name = actor is INpcGetter n ? n.Name?.ToString() : ((ICreatureGetter)actor).Name?.ToString(), Kind = "ActorLoot",
                Cell = (string?)null, CellName = (string?)null, SourcePlugin = actor.FormKey.ModKey.ToString(), Tier = tier,
                DaedricContext = Regex.IsMatch(id, "Dremora|Daedra|Xivilai", RegexOptions.IgnoreCase), selected.Category, selected.Profile, selected.Rule, Evidence = id, LocationReason = "Actor power tier; no player-level input.", Odds = Odds(actor.FormKey), Plans = plans });
            }
        }
        if (!run.Settings.ReportOnly)
        {
            foreach (var list in builder.Patch.LeveledItems) { state.PatchMod.LeveledItems.Add(list); changes[list.FormKey] = ["NewRecord"]; }
            foreach (var container in builder.Patch.Containers) { state.PatchMod.Containers.Add(container); changes[container.FormKey] = ["NewRecord"]; }
            state.PatchMod.ModHeader.Stats.NextFormID = builder.Patch.ModHeader.Stats.NextFormID;
            var contexts = state.LoadOrder.PriorityOrder.Where(listing => listing.Enabled && listing.Mod is not null && run.IsInputPlugin(listing.ModKey))
                .SelectMany(listing => listing.Mod!.EnumerateMajorRecordContexts<IPlacedObject, IPlacedObjectGetter>(state.LinkCache))
                .GroupBy(context => context.Record.FormKey).ToDictionary(group => group.Key, group => group.First());
            foreach (var pair in redirects) { contexts[pair.Key].GetOrAddAsOverride(state.PatchMod).Base.SetTo(pair.Value); changes[pair.Key] = ["Base"]; }
        }
        run.WriteReport(".world-loot.json", new { run.Settings.ReportOnly, ConfigurationFile = config,
            Summary = new { Records = rows.Count, ProcessedInventoryLists = modifiedSlots, RedirectedContainers = redirects.Count,
                PrivateContainers = builder.Patch.Containers.Count, PrivateLists = builder.Patch.LeveledItems.Count }, Records = rows,
            ExpectedItemYieldByProfile = totals,
            BalanceByProfile = balanceRows.GroupBy(row => row.Profile).Select(group => new { Profile = group.Key, Records = group.Count(),
                MeanRareChancePercent = group.Average(row => row.Rare) * 100, MaximumRareChancePercent = group.Max(row => row.Rare) * 100,
                MeanPremiumChancePercent = group.Average(row => row.Premium) * 100, MeanGlassChancePercent = group.Average(row => row.Glass) * 100, MeanExpectedBaseValue = group.Average(row => row.Value) }).ToArray(),
            Limitations = new[] { "Expected item yield includes nested empty chances and counts; it is not a percentage chance per chest.",
                "Fixed contents, merchant stock, scripted/quest branches, UseAll bundles are preserved with reasons.",
                "Profiles only redistribute existing source families. No new artifacts, extra gold or extra inventory rolls.",
                "Fresh/reset inventories are needed in existing saves. Ordinary leveled death loot uses actor power; guarded rewards remain intact." }
        }, LootConfiguration.Options);
        run.Log($"World loot: {redirects.Count} container placements, {modifiedSlots} inventory lists planned; {builder.Patch.LeveledItems.Count} private lists.");
        return changes;
    }
    private static bool PureEquipment(FormKey key, IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, HashSet<FormKey> seen)
    {
        if (!seen.Add(key)) return false;
        if (records.GetValueOrDefault(key) is not ILeveledItemGetter list) return EquipmentPoolBuilder.IsEquipment(records.GetValueOrDefault(key));
        return list.Entries.Count > 0 && list.Entries.All(entry => PureEquipment(entry.Reference.FormKey, records, new(seen)));
    }
    private static string? ContainerGuard(IContainerGetter container, IPlacedObjectGetter placed, HashSet<FormKey> merchants,
        HashSet<FormKey> scripts, ICellGetter? cell)
    {
        if (merchants.Contains(placed.FormKey) || Regex.IsMatch(container.EditorID ?? "", "Merchant|Vendor|StolenGoods|Evidence|RespawnChest", RegexOptions.IgnoreCase)) return "Merchant/service container retained.";
        if ((container.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0) return "Quest container retained.";
        if (container.Script.FormKeyNullable is not null || scripts.Contains(container.FormKey) || scripts.Contains(placed.FormKey)) return "Scripted/referenced container retained.";
        if (Regex.IsMatch($"{container.EditorID} {cell?.EditorID}", "Test|Warehouse|HoldingCell|Dummy|PlayerHouse|Sigil", RegexOptions.IgnoreCase)) return "Helper/player-storage/reward container retained.";
        return null;
    }
    internal static HashSet<FormKey> UnsafeListReferences(IEnumerable<IScriptFieldsGetter> fields) => fields
        .Where(script => string.IsNullOrWhiteSpace(script.SourceCode) || Regex.IsMatch(
            string.Join("\n", script.SourceCode.Split('\n').Select(line => line.Split(';')[0])),
            @"\b(AddToLeveledList|RemoveFromLeveledList)\b", RegexOptions.IgnoreCase))
        .SelectMany(script => script.EnumerateFormLinks()).Select(link => link.FormKey).ToHashSet();

    private static bool ScriptTouches(FormKey key, HashSet<FormKey> scripts, IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, HashSet<FormKey> seen)
    {
        if (!seen.Add(key)) return false;
        return records.GetValueOrDefault(key) is ILeveledItemGetter list &&
            (scripts.Contains(key) || list.Entries.Any(entry => ScriptTouches(entry.Reference.FormKey, scripts, records, seen)));
    }
    private static Dictionary<string, JsonElement> ReadGeography(PatcherRun run)
    {
        if (!run.Settings.EnableGeographicDiscovery) return new();
        using var document = JsonDocument.Parse(File.ReadAllText(run.ReportPath(".geography.json")));
        return document.RootElement.GetProperty("Locations").EnumerateArray().ToDictionary(row => row.GetProperty("FormKey").GetString()!, row => row.Clone());
    }
    private static (int Tier, bool Daedric, string Reason) Location(JsonElement row)
    {
        if (row.ValueKind == JsonValueKind.Undefined) return (2, false, "No geographic profile; modest fallback.");
        var proposal = row.GetProperty("Proposal");
        var tier = proposal.GetProperty("MaximumTier").ValueKind == JsonValueKind.Number ? proposal.GetProperty("MaximumTier").GetInt32() : 2;
        var dungeon = row.GetProperty("Dungeon");
        if (!proposal.GetProperty("HasOverride").GetBoolean() && dungeon.ValueKind == JsonValueKind.Object && dungeon.GetProperty("Profile").GetProperty("Enabled").GetBoolean())
        {
            var profile = dungeon.GetProperty("Profile"); var range = profile.GetProperty("LootTierRange");
            tier = range.ValueKind == JsonValueKind.Object ? range.GetProperty("MaximumTier").GetInt32() : profile.GetProperty("BasePowerTier").GetInt32();
        }
        var text = (row.GetProperty("EditorID").GetString() ?? "") + " " + row.GetProperty("Worldspace").GetRawText();
        var daedric = Regex.IsMatch(text, @"Oblivion|Deadlands", RegexOptions.IgnoreCase);
        return (tier, daedric, dungeon.ValueKind == JsonValueKind.Object ? "Dungeon loot range/cap." : "Geographic danger range.");
    }
}
