using System.Text.Json;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
void Check(bool good, string reason) { if (!good) throw new Exception(reason); }
var source = new OblivionMod(ModKey.FromNameAndExtension("Source.esp"), OblivionRelease.Oblivion);
var iron = source.Weapons.AddNew(); iron.EditorID = "WeapIronSword";
var glass = source.Weapons.AddNew(); glass.EditorID = "WeapGlassSword";
var ebony = source.Weapons.AddNew(); ebony.EditorID = "WeapEbonySword";
var daedric = source.Weapons.AddNew(); daedric.EditorID = "WeapDaedricSword";
LeveledItemEntry Entry(FormKey key, short level = 1, short count = 1)
{ var entry = new LeveledItemEntry { Level = level, Count = count, Unknown = 7, Unknown2 = 9 }; entry.Reference.SetTo(key); return entry; }
var nested = source.LeveledItems.AddNew(); nested.EditorID = "LootWeapons";
nested.Entries.AddRange(new[] { Entry(iron.FormKey), Entry(glass.FormKey, 20), Entry(ebony.FormKey, 25), Entry(daedric.FormKey, 30) });
var root = source.LeveledItems.AddNew(); root.EditorID = "LootChest"; root.Entries.Add(Entry(nested.FormKey)); root.ChanceNone = new Percent(.25);
root.Flags = LeveledFlag.CalculateForEachItemInCount;
var records = source.EnumerateMajorRecords().ToDictionary(item => item.FormKey, item => (IMajorRecordGetter)item);
var settings = LootConfiguration.Load("BaldursGateStyleOblivion/loot.json");
LootPoolBuilder Builder() => new(records, settings, ModKey.FromNameAndExtension("Patch.esp"), 0x800, _ => true);
var builder = Builder(); var low = builder.Build(root.FormKey, "Danger2"); var high = builder.Build(root.FormKey, "Daedric6");
Check(Math.Abs(builder.Chance(high.Key, 1) - .003) < 1e-10, "Ebony 3/1000 and Daedric 1/1000, with 25% empty chance.");
var endgame = builder.Build(root.FormKey, "Daedric10");
Check(Math.Abs(builder.Chance(endgame.Key, 1) - .4125) < 1e-10, "Endgame material opportunities: 17.5% Ebony + 37.5% Daedric, after native empty chance.");
Check(Math.Abs(builder.Expected(endgame.Key).GetValueOrDefault("Ebony") - .13125) < 1e-10 &&
    Math.Abs(builder.Expected(endgame.Key).GetValueOrDefault("Daedric") - .28125) < 1e-10, "Daedric-context material odds favor Daedric over Ebony.");
var mortalEndgame = builder.Build(root.FormKey, "Danger10");
Check(Math.Abs(builder.Chance(mortalEndgame.Key, 1) - .2625) < 1e-10, "Ordinary endgame profile offers Ebony without adding Daedric gear.");
Check(Math.Abs(builder.Chance(high.Key, 1, material: "Glass") - .0375) < 1e-10, "Glass has its own 5% tier-6 opportunity, after native empty chance.");
Check(builder.Chance(low.Key, 1, material: "Glass") == 0, "Glass cannot leak through generic premium loot at low tiers.");
Check(Math.Abs(builder.Expected(endgame.Key).GetValueOrDefault("Glass") - .1125) < 1e-10, "Daedric endgame Glass branch remains separate from Ebony/Daedric odds.");
Check(builder.Chance(low.Key, 1) == 0, "Low-danger material restriction.");
Check(Math.Abs(builder.Chance(high.Key, 3) - (1 - Math.Pow(.997, 3))) < 1e-10, "Whole chest multi-roll odds.");
Check(builder.Chance(high.Key, 1, true) > builder.Chance(low.Key, 1, true), "Danger increases premium opportunity.");
Check(builder.Expected(high.Key).GetValueOrDefault("Ebony") > 0, "Rare yield report.");
Check(builder.Build(root.FormKey, "Daedric6").Key == high.Key, "Deterministic pool reuse.");
Check(builder.Patch.LeveledItems.All(list => list.Entries.Count <= 255 && list.Entries.All(entry => entry.Level == 1)), "Native entry limits and no level gates.");
Check(root.Entries[0].Reference.FormKey == nested.FormKey && nested.Entries[1].Level == 20, "Original lists untouched.");
var copy = Builder(); Check(copy.Build(root.FormKey, "Danger2").Key == low.Key, "Deterministic allocation.");
void Guarded(Action change, string name)
{ change(); try { Builder().Build(root.FormKey, "Danger2"); throw new Exception("Missing guard: " + name); } catch (InvalidDataException) {} }
Guarded(() => nested.Flags = LeveledFlag.UseAll, "UseAll"); nested.Flags = null;
Guarded(() => nested.Entries.Add(Entry(root.FormKey)), "cycle"); nested.Entries.RemoveAt(nested.Entries.Count - 1);
Guarded(() => settings.ListOverrides[nested.FormKey.ToString()] = new() { Preserve = true }, "nested preservation"); settings.ListOverrides.Clear();
settings.ListOverrides[root.FormKey.ToString()] = new() { Profile = "Danger2" };

var manual = Builder(); Check(manual.Chance(manual.Build(root.FormKey, "Daedric6").Key, 1) == 0, "List override takes priority.");
Check(LootConfiguration.Select(settings, root.FormKey.ToString(), "Source.esp", "House cupboard", 8, false).Profile == "Modest", "Household profile remains modest.");
var precious = source.LeveledItems.AddNew(); precious.EditorID = "LootPrecious"; precious.Entries.Add(Entry(ebony.FormKey, 25));
records[precious.FormKey] = precious;
var rareOnly = Builder(); var rarePool = rareOnly.Build(precious.FormKey, "Danger6");
Check(Math.Abs(rareOnly.Chance(rarePool.Key, 1) - .003) < 1e-10, "Precious-only pools add an empty outcome to enforce rarity.");
var glassOnly = source.LeveledItems.AddNew(); glassOnly.EditorID = "LootGlassOnly"; glassOnly.Entries.Add(Entry(glass.FormKey, 20)); records[glassOnly.FormKey] = glassOnly;
var glassOnlyResult = rareOnly.Build(glassOnly.FormKey, "Danger3");
Directory.CreateDirectory("artifacts/loot-review/glass-fixture");
rareOnly.Patch.WriteToBinary("artifacts/loot-review/glass-fixture/Patch.esp");
using (var savedRare = OblivionMod.CreateFromBinaryOverlay("artifacts/loot-review/glass-fixture/Patch.esp", OblivionRelease.Oblivion))
{
    double SavedChance(FormKey key, FormKey leaf)
    {
        if (!savedRare.LeveledItems.TryGetValue(key, out var list)) return key == leaf ? 1 : 0;
        return list.Entries.Sum(entry => SavedChance(entry.Reference.FormKey, leaf)) / list.Entries.Count * (1 - (list.ChanceNone ?? Percent.Zero).Value);
    }
    Check(Math.Abs(SavedChance(rarePool.Key, ebony.FormKey) - .003) < 1e-10, "Written premium-only pool preserves sub-percent chances without Chance None rounding.");
    Check(Math.Abs(SavedChance(glassOnlyResult.Key, glass.FormKey) - .002) < 1e-10, "Written Glass-only pool preserves the exact 0.2% profile chance.");
}
var bannedOnly = Builder(); Check(bannedOnly.Chance(bannedOnly.Build(precious.FormKey, "Danger2").Key, 1) == 0, "Ineligible precious-only pool becomes empty, static and valid.");
var torch = source.Lights.AddNew(); torch.EditorID = "Torch01"; records[torch.FormKey] = torch;
var clutter = source.LeveledItems.AddNew(); clutter.EditorID = "LootClutter"; clutter.Entries.AddRange(new[] { Entry(torch.FormKey), Entry(iron.FormKey, 10) }); records[clutter.FormKey] = clutter;
var torches = Builder(); Check(!torches.Build(clutter.FormKey, "Danger2").Key.IsNull, "Native torch/clutter lists are supported.");
Console.WriteLine("Loot fixtures passed: static nested pools, danger/rarity, native counts/Chance None, reuse, overrides and guards.");
if (args.Length == 2 && args[0] == "--dry")
{
    using var dry = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    Check(!dry.EnumerateMajorRecords().Any(), "Report-only loot output must contain no gameplay records.");
    using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1])!, "Reports/BaldursGateStyleOblivion.world-loot.json")));
    Check(report.RootElement.GetProperty("ReportOnly").GetBoolean() && report.RootElement.GetProperty("Summary").GetProperty("ProcessedInventoryLists").GetInt32() > 0, "Report-only mode must still create loot plans.");
    Console.WriteLine("Report-only loot verified: plans produced, empty standalone ESP.");
}
else if (args.Length > 0)
{
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[0], OblivionRelease.Oblivion);
    using var document = JsonDocument.Parse(File.ReadAllText(args[1]));
    var rows = document.RootElement.GetProperty("Records").EnumerateArray().ToArray();
    var originals = Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data", "*.esm").Concat(Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data", "DLC*.esp")).Concat(Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data", "Knights.esp")).Select(path => OblivionMod.CreateFromBinaryOverlay(path, OblivionRelease.Oblivion)).ToArray();
    try
    {
        var originalRecords = originals.SelectMany(mod => mod.EnumerateMajorRecords()).GroupBy(record => record.FormKey).ToDictionary(group => group.Key, group => group.Last());
        var patchRecords = patch.EnumerateMajorRecords().ToDictionary(record => record.FormKey);
        if (File.Exists("artifacts/realm-review/BaldursGateStyleOblivion.esp"))
        {
            using var prior = OblivionMod.CreateFromBinaryOverlay("artifacts/realm-review/BaldursGateStyleOblivion.esp", OblivionRelease.Oblivion);
            foreach (var old in prior.Npcs)
                if (patch.Npcs.TryGetValue(old.FormKey, out var current)) Check(current.Configuration?.LevelOffset == old.Configuration?.LevelOffset &&
                    current.Configuration?.CalcMin == old.Configuration?.CalcMin && current.Configuration?.CalcMax == old.Configuration?.CalcMax &&
                    current.Configuration?.Flags == old.Configuration?.Flags, "Actor level/flags unaffected by loot.");
        }
        using var equipmentReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1])!, "BaldursGateStyleOblivion.equipment-distribution.json")));
        foreach (var actor in equipmentReport.RootElement.GetProperty("Actors").EnumerateArray())
        {
            var key = FormKey.Factory(actor.GetProperty("FormKey").GetString()!);
            if (!patchRecords.TryGetValue(key, out var record)) continue;
            var inventory = record is INpcGetter npc ? npc.Items.Cast<IItemEntryGetter>().ToArray() : ((ICreatureGetter)record).Items.Cast<IItemEntryGetter>().ToArray();
            foreach (var plan in actor.GetProperty("Inventories").EnumerateArray().Where(plan => plan.GetProperty("Status").GetString() == "Modified"))
            {
                var target = new FormKey(patch.ModKey, FormKey.Factory(plan.GetProperty("Planned").GetString()!).ID);
                Check(inventory.Any(item => item.Item.FormKey == target), "Phase 9 equipped gear redirect retained.");
            }
        }
        var seen = new HashSet<FormKey>();
        void Visit(FormKey key)
        {
            if (!seen.Add(key)) return;
            if (patchRecords.GetValueOrDefault(key) is not ILeveledItemGetter list) { Check(originalRecords.ContainsKey(key), "Unresolved loot leaf " + key); return; }
            Check((list.EditorID ?? "").StartsWith("BGSO_Loot"), "Loot linked outside its private pools.");
            Check(list.Entries.Count is > 0 and <= 255 && list.Entries.All(entry => entry.Level == 1), "Static written pool invariant.");
            foreach (var entry in list.Entries) Visit(entry.Reference.FormKey);
        }
        var placements = patch.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().ToDictionary(item => item.FormKey);
        foreach (var row in rows)
        {
            var modified = row.GetProperty("Plans").EnumerateArray().Where(plan => plan.GetProperty("Status").GetString() == "Modified").ToArray();
            if (modified.Length == 0) continue;
            foreach (var plan in modified) Visit(new FormKey(patch.ModKey, FormKey.Factory(plan.GetProperty("Planned").GetString()!).ID));
            if (row.GetProperty("Kind").GetString() != "Container") continue;
            var key = FormKey.Factory(row.GetProperty("FormKey").GetString()!);
            var sourceKey = FormKey.Factory(row.GetProperty("Source").GetString()!);
            var container = (IContainerGetter)patchRecords[placements[key].Base.FormKey];
            var original = (IContainerGetter)originalRecords[sourceKey];
            Check(container.Items.Count == original.Items.Count && container.Data?.Flags == original.Data?.Flags && container.Script.FormKey == original.Script.FormKey, "Container metadata retained.");
            for (var i = 0; i < original.Items.Count; i++)
            {
                Check(container.Items[i].Count == original.Items[i].Count, "Container native inventory quantity retained.");
                if (originalRecords[original.Items[i].Item.FormKey] is not ILeveledItemGetter)
                    Check(container.Items[i].Item.FormKey == original.Items[i].Item.FormKey, "Fixed contents retained.");
            }
        }
        var rewardFamilies = RewardConfiguration.Load("BaldursGateStyleOblivion/rewards.json").Lists.Keys.Select(key => FormKey.Factory(key)).ToHashSet();
        Check(!patch.LeveledItems.Any(list => !(list.EditorID ?? "").StartsWith("BGSO_") && !rewardFamilies.Contains(list.FormKey)), "Only explicitly configured quest reward families may override global lists.");
        Check(!patch.Weapons.Any() && !patch.Armors.Any(), "Item stats unchanged.");
        Console.WriteLine($"Written loot plugin verified: {seen.Count} reachable records; container counts/fixed contents intact, ordinary global lists and item stats untouched; curated quest rewards handled separately.");
    }
    finally { foreach (var mod in originals) mod.Dispose(); }
}

