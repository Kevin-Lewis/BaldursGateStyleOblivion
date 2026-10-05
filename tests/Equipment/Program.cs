using System.Text.Json;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
var source = new OblivionMod(ModKey.FromNameAndExtension("Source.esp"), OblivionRelease.Oblivion);
var iron = source.Weapons.AddNew(); iron.EditorID = "WeapIronSword";
var glass = source.Weapons.AddNew(); glass.EditorID = "WeapGlassSword";
var daedric = source.Weapons.AddNew(); daedric.EditorID = "WeapDaedricSword";
var steel = source.Weapons.AddNew(); steel.EditorID = "WeapSteelSword";
var enchanted = source.Weapons.AddNew(); enchanted.EditorID = "EnchIronSword"; enchanted.Enchantment.SetTo(new FormKey(source.ModKey, 0xFFFF));
LeveledItemEntry Entry(FormKey key, short level = 1, short count = 1)
{
    var entry = new LeveledItemEntry { Level = level, Count = count, Unknown = 7, Unknown2 = 9 }; entry.Reference.SetTo(key); return entry;
}
LeveledItem List(string id, params LeveledItemEntry[] entries)
{
    var list = source.LeveledItems.AddNew(); list.EditorID = id; list.Entries.AddRange(entries);
    list.Flags = LeveledFlag.CalculateForEachItemInCount; list.ChanceNone = new Percent(0.25); return list;
}
var child = List("SharedWeapons", Entry(iron.FormKey, 1, 2), Entry(steel.FormKey, 10), Entry(enchanted.FormKey)); child.ChanceNone = Percent.Zero;
var root = List("ActorWeapons", Entry(child.FormKey), Entry(glass.FormKey, 20), Entry(daedric.FormKey, 25));
var settings = EquipmentConfiguration.Load("BaldursGateStyleOblivion/equipment.json");
settings.Profiles["Test"] = new() { Weights = new() { [EquipmentQuality.Poor] = 900, [EquipmentQuality.Common] = 80, [EquipmentQuality.Elite] = 20 }, EnchantedPercent = 0, DaedricPerThousand = 1 };
var records = source.EnumerateMajorRecords().ToDictionary(record => record.FormKey, record => (IMajorRecordGetter)record);
EquipmentPoolBuilder Builder(Func<ModKey, bool>? included = null) => new(records, settings, ModKey.FromNameAndExtension("Patch.esp"), 0x800, included ?? (_ => true));
var builder = Builder(); var result = builder.Build(root.FormKey, "Test");
Dictionary<FormKey, double> Odds(FormKey key, int level, EquipmentPoolBuilder current, Dictionary<FormKey, Dictionary<FormKey, double>>? memo = null)
{
    memo ??= new(); if (memo.TryGetValue(key, out var found)) return found;
    var list = current.Patch.LeveledItems.TryGetValue(key, out var planned) ? planned : records.GetValueOrDefault(key) as ILeveledItemGetter;
    if (list is null) return new() { [key] = 1 };
    var entries = list.Entries.Where(entry => entry.Level <= level).ToArray();
    if (list.Flags?.HasFlag(LeveledFlag.CalculateFromAllLevelsLessThanPlayers) != true && entries.Length > 0)
        entries = entries.Where(entry => entry.Level == entries.Max(item => item.Level)).ToArray();
    var odds = new Dictionary<FormKey, double>();
    foreach (var entry in entries)
        foreach (var (leaf, chance) in Odds(entry.Reference.FormKey, level, current, memo))
            odds[leaf] = odds.GetValueOrDefault(leaf) + chance * (1 - list.ChanceNone.GetValueOrDefault(Percent.Zero).Value) / entries.Length;
    memo[key] = odds; return odds;
}
foreach (var level in new[] { 1, 10, 25, 40 })
{
    var odds = Odds(result.Key, level, builder);
    Check(Math.Abs(odds[iron.FormKey] - 0.75 * 0.9 * 0.999) < 1e-9, "Poor-material weight and root Chance None must be exact");
    Check(Math.Abs(odds[daedric.FormKey] - 0.75 * 0.001) < 1e-9, "Daedric rarity must be exactly 0.1% conditional on a selection");
    Check(!odds.ContainsKey(enchanted.FormKey), "Zero enchanted weighting excludes enchanted variants when mundane choices exist");
}
Check(root.Entries.Any(entry => entry.Level > 1) && source.LeveledItems.Count == 2, "Source and shared merchant/loot lists must stay intact");
Check(builder.Patch.LeveledItems.All(list => list.FormKey.ModKey == builder.Patch.ModKey && list.Entries.Count <= 255 && list.Entries.All(entry => entry.Level == 1)), "All private pools must obey native limits and be static");
Check(builder.Patch.LeveledItems.Any(list => list.Entries.Any(entry => entry.Reference.FormKey == iron.FormKey && entry.Count == 2 && entry.Unknown == 7 && entry.Unknown2 == 9)), "Native item counts and metadata must survive");
Check(builder.Patch.LeveledItems[result.Key].ChanceNone == root.ChanceNone && builder.Patch.LeveledItems[result.Key].Flags!.Value.HasFlag(LeveledFlag.CalculateForEachItemInCount), "Native root chance and count behavior must survive");
Check(builder.Build(root.FormKey, "Test").Key == result.Key, "Identical actor profiles reuse private pools");
var repeat = Builder(); Check(repeat.Build(root.FormKey, "Test").Key == result.Key, "Private record allocation must be deterministic");
var excluded = Builder(_ => false);
try { excluded.Build(root.FormKey, "Test"); throw new Exception("Excluded list accepted"); } catch (InvalidDataException) { }
Check(excluded.Patch.LeveledItems.Count == 0, "Validation failures cannot allocate orphan records");
settings.ListOverrides[root.FormKey.ToString()] = new() { Preserve = true };
try { Builder().Build(root.FormKey, "Test"); throw new Exception("Preserved list accepted"); } catch (InvalidDataException) { }
settings.ListOverrides.Clear();
root.Flags |= LeveledFlag.UseAll;
try { Builder().Build(root.FormKey, "Test"); throw new Exception("UseAll bundle accepted"); } catch (InvalidDataException) { }
root.Flags &= ~LeveledFlag.UseAll;
child.Entries.Add(Entry(root.FormKey));
try { Builder().Build(root.FormKey, "Test"); throw new Exception("Cycle accepted"); } catch (InvalidDataException) { }
child.Entries.RemoveAt(child.Entries.Count - 1);
iron.Script.SetTo(new FormKey(source.ModKey, 0xFFFF));
try { Builder().Build(root.FormKey, "Test"); throw new Exception("Scripted gear accepted"); } catch (InvalidDataException) { }
iron.Script.Clear();
var preciousOnly = List("RestrictedDaedricWeapons", Entry(daedric.FormKey, 25, 3)); records[preciousOnly.FormKey] = preciousOnly;
var mortalBuilder = Builder(); var mortalKey = mortalBuilder.Build(preciousOnly.FormKey, "Elite").Key;
Check(!Odds(mortalKey, 1, mortalBuilder).ContainsKey(daedric.FormKey), "Even elite mortal pools must exclude Daedric weapons and get a compatible mundane fallback");
var dremoraBuilder = Builder(); var dremoraKey = dremoraBuilder.Build(preciousOnly.FormKey, "HighDremora").Key;
Check(Math.Abs(Odds(dremoraKey, 1, dremoraBuilder)[daedric.FormKey] - 0.75 * 0.75) < 1e-9, "High-ranking Dremora should commonly carry Daedric weapons when the source pool offers them");
Check(EquipmentConfiguration.Select(settings, root.FormKey.ToString(), "Source.esp", "Dremora6Valkynaz", 6).Profile == "HighDremora", "High Dremora rank selects the Daedric-eligible profile");
Check(EquipmentConfiguration.Select(settings, root.FormKey.ToString(), "Source.esp", "Dremora0Churl", 3).Profile != "HighDremora", "Low Dremora ranks cannot receive the high-rank profile");
Check(EquipmentConfiguration.Select(settings, root.FormKey.ToString(), "Source.esp", "AlgottheNortherner | ChorrolNorthernGate", 4).Profile != "HighDremora", "Similar mortal names and location names must never grant Daedric eligibility");
var ebony = source.Weapons.AddNew(); ebony.EditorID = "WeapEbonySword"; records[ebony.FormKey] = ebony;
var ebonyRoot = List("RareEbonyWeapons", Entry(iron.FormKey), Entry(ebony.FormKey, 20)); records[ebonyRoot.FormKey] = ebonyRoot;
var wealthyBuilder = Builder(); var wealthyKey = wealthyBuilder.Build(ebonyRoot.FormKey, "HighQuality").Key;
Check(Math.Abs(Odds(wealthyKey, 40, wealthyBuilder)[ebony.FormKey] - 0.75 * 0.001) < 1e-9, "Wealthy Ebony chance must be 0.1% per source selection, independent of Glass variants");
var commonBuilder = Builder(); var commonKey = commonBuilder.Build(ebonyRoot.FormKey, "Common").Key;
Check(!Odds(commonKey, 40, commonBuilder).ContainsKey(ebony.FormKey), "Ordinary equipment pools must exclude Ebony");
var elven = source.Weapons.AddNew(); elven.EditorID = "WeapElvenSword"; records[elven.FormKey] = elven;
var mithril = source.Armors.AddNew(); mithril.EditorID = "ArmorMithrilCuirass"; records[mithril.FormKey] = mithril;
var orcish = source.Armors.AddNew(); orcish.EditorID = "ArmorOrcishCuirass"; records[orcish.FormKey] = orcish;
Check(Builder().Quality(orcish) == EquipmentQuality.HighQuality, "Orcish armor must be explicitly high quality");
settings.Profiles["RaceTest"] = new() { Weights = new() { [EquipmentQuality.Common] = 500, [EquipmentQuality.HighQuality] = 500 }, EnchantedPercent = 0, EbonyPerThousand = 1 };
var raceRoot = List("RaceEquipment", Entry(steel.FormKey), Entry(elven.FormKey, 15), Entry(mithril.FormKey, 15), Entry(ebony.FormKey, 20)); records[raceRoot.FormKey] = raceRoot;
var raceBuilder = Builder(); var neutralKey = raceBuilder.Build(raceRoot.FormKey, "RaceTest").Key;
var elvenKey = raceBuilder.Build(raceRoot.FormKey, "RaceTest", "Elven").Key;
var neutralOdds = Odds(neutralKey, 1, raceBuilder); var elvenOdds = Odds(elvenKey, 40, raceBuilder);
Check(elvenKey != neutralKey && Math.Abs(elvenOdds[elven.FormKey] / elvenOdds[mithril.FormKey] - 8) < 1e-9, "Elven preference strongly favors racial material within the same quality band and has a separate cached pool");
Check(Math.Abs(elvenOdds[steel.FormKey] - neutralOdds[steel.FormKey]) < 1e-9 && Math.Abs(elvenOdds[ebony.FormKey] - neutralOdds[ebony.FormKey]) < 1e-9, "Race preference cannot increase equipment quality or Ebony rarity");
Check(raceBuilder.Build(raceRoot.FormKey, "RaceTest", "Orcish").Key == neutralKey, "Missing racial equipment must preserve and reuse the ordinary pool");
var orcRoot = List("OrcEquipment", Entry(orcish.FormKey, 15), Entry(mithril.FormKey, 15)); records[orcRoot.FormKey] = orcRoot;
var orcBuilder = Builder(); var orcKey = orcBuilder.Build(orcRoot.FormKey, "RaceTest", "Orcish").Key;
Check(Math.Abs(Odds(orcKey, 10, orcBuilder)[orcish.FormKey] / Odds(orcKey, 10, orcBuilder)[mithril.FormKey] - 8) < 1e-9, "Orcs receive the same strong material preference");
Check(raceBuilder.Build(raceRoot.FormKey, "Military", "Elven").Key == raceBuilder.Build(raceRoot.FormKey, "Military").Key, "Faction military profiles take precedence over race preference");
settings.ListOverrides[raceRoot.FormKey.ToString()] = new() { Profile = "RaceTest" };
var curatedRaceBuilder = Builder();
Check(curatedRaceBuilder.Build(raceRoot.FormKey, "RaceTest", "Elven").Key == curatedRaceBuilder.Build(raceRoot.FormKey, "RaceTest").Key, "Individual list definitions take precedence over race preference");
settings.ListOverrides.Clear();
var scriptedActor = source.Npcs.AddNew(); scriptedActor.EditorID = "NamedScriptedActor";
var actorScript = source.Scripts.AddNew(); actorScript.Fields.SourceCode = "scn EquipmentTest\nbegin GameMode\nEquipItem WeapIronSword\nend";
scriptedActor.Script.SetTo(actorScript.FormKey); records[actorScript.FormKey] = actorScript;
Check(EquipmentDistributionModule.Guard(scriptedActor, root.FormKey, records) is not null, "Unresolved script operands must stay guarded");
actorScript.Fields.SourceCode = "scn EquipmentTest\nbegin GameMode\nRemoveAllItems\nend";
Check(EquipmentDistributionModule.Guard(scriptedActor, root.FormKey, records) is not null, "Whole-inventory scripts must stay guarded");
actorScript.Fields.SourceCode = "scn EquipmentTest\nbegin OnDeath\n; EquipItem WeapIronSword\nend";
Check(EquipmentDistributionModule.Guard(scriptedActor, root.FormKey, records) is null, "Commented inventory commands cannot block unrelated equipment");
settings.ActorOverrides[root.FormKey.ToString()] = new() { Profile = "Elite" };
Check(EquipmentConfiguration.Select(settings, root.FormKey.ToString(), "Source.esp", "ActorCategory=Bandit", 2).Profile == "Elite", "Individual actor overrides win over group rules");
settings.Profiles["Invalid"] = new() { Weights = new() { [EquipmentQuality.Poor] = -1 } };
try { EquipmentConfiguration.Validate(settings); throw new Exception("Invalid weights accepted"); } catch (ArgumentException) { }
settings.Profiles.Remove("Invalid");
Console.WriteLine("Equipment checks passed: player-level independence, exact material rarity, enchantments, native counts/chance, private isolation, deterministic reuse, overrides and guards.");

if (args.Length == 2 && args[0] == "--dry")
{
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    Check(!patch.EnumerateMajorRecords().Any(), "Report-only mode must write no gameplay records");
    using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1])!, "Reports/BaldursGateStyleOblivion.equipment-distribution.json")));
    Check(report.RootElement.GetProperty("ReportOnly").GetBoolean() && report.RootElement.GetProperty("Summary").GetProperty("ModifiedActors").GetInt32() == 0 &&
        report.RootElement.GetProperty("GeneratedPools").GetArrayLength() > 0, "Dry run must still report planned equipment pools");
    Console.WriteLine("Equipment report-only mode verified: complete plans, zero gameplay overrides.");
}
else if (args.Length == 2)
{
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[0], OblivionRelease.Oblivion);
    using var baseline = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    var data = @"F:\SteamLibrary\steamapps\common\Oblivion\Data";
    var inputs = patch.MasterReferences.Select(master => OblivionMod.CreateFromBinaryOverlay(Path.Combine(data, master.Master.ToString()), OblivionRelease.Oblivion)).ToArray();
    try
    {
        var originals = inputs.Reverse().SelectMany(mod => mod.EnumerateMajorRecords()).GroupBy(record => record.FormKey).ToDictionary(group => group.Key, group => group.First());
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0])!, "Reports/BaldursGateStyleOblivion.equipment-distribution.json")));
        var pools = patch.LeveledItems.ToDictionary(list => list.FormKey);
        var checkedLists = new HashSet<FormKey>();
        void Static(FormKey key)
        {
            Check(pools.TryGetValue(key, out var list), "Written equipment dependency must resolve to a private list");
            if (!checkedLists.Add(key)) return;
            Check(list!.Entries.Count is > 0 and <= 255 && list.Entries.All(entry => entry.Level == 1), "Written equipment pools must be static and within native limits");
            foreach (var entry in list.Entries)
                if (entry.Reference.FormKey.ModKey == patch.ModKey) Static(entry.Reference.FormKey);
                else Check(originals.TryGetValue(entry.Reference.FormKey, out var record) && EquipmentPoolBuilder.IsEquipment(record), "Every equipment leaf must resolve to equipment");
        }
        var count = 0;
        foreach (var actor in report.RootElement.GetProperty("Actors").EnumerateArray())
        {
            var key = FormKey.Factory(actor.GetProperty("FormKey").GetString()!);
            var current = patch.Npcs.TryGetValue(key, out var npc) ? npc.Items.Cast<IItemEntryGetter>().ToArray()
                : patch.Creatures.TryGetValue(key, out var creature) ? creature.Items.Cast<IItemEntryGetter>().ToArray() : null;
            if (current is not null)
            {
                var originalActor = originals[key];
                var originalItems = originalActor is INpcGetter originalNpc ? originalNpc.Items.Cast<IItemEntryGetter>().ToArray() : ((ICreatureGetter)originalActor).Items.Cast<IItemEntryGetter>().ToArray();
                Check(current.Length == originalItems.Length, "Equipment distribution cannot add or remove inventory slots");
                var redirects = actor.GetProperty("Inventories").EnumerateArray().Where(plan => plan.GetProperty("Status").GetString() == "Modified")
                    .ToDictionary(plan => FormKey.Factory(plan.GetProperty("Original").GetString()!), plan => new FormKey(patch.ModKey, FormKey.Factory(plan.GetProperty("Planned").GetString()!).ID));
                for (var index = 0; index < current.Length; index++)
                    Check(current[index].Count == originalItems[index].Count && current[index].Item.FormKey == redirects.GetValueOrDefault(originalItems[index].Item.FormKey, originalItems[index].Item.FormKey), "Every fixed item, retained loot slot, count and equipment redirect must match its source inventory");
            }
            foreach (var plan in actor.GetProperty("Inventories").EnumerateArray().Where(plan => plan.GetProperty("Status").GetString() == "Modified"))
            {
                var planned = FormKey.Factory(plan.GetProperty("Planned").GetString()!);
                var target = new FormKey(patch.ModKey, planned.ID);
                Check(current!.Any(item => item.Item.FormKey == target && item.Count == plan.GetProperty("Count").GetInt32()), "Written actor must reference its planned equipment pool with the original count");
                var original = (ILeveledItemGetter)originals[FormKey.Factory(plan.GetProperty("Original").GetString()!)];
                Check(pools[target].ChanceNone == original.ChanceNone && (pools[target].Flags & LeveledFlag.CalculateForEachItemInCount) == (original.Flags & LeveledFlag.CalculateForEachItemInCount), "Written root must preserve no-item and per-count behavior");
                Static(target); count++;
            }
        }
        foreach (var npc in patch.Npcs)
            if (baseline.Npcs.TryGetValue(npc.FormKey, out var prior))
                Check(npc.Configuration?.LevelOffset == prior.Configuration?.LevelOffset && npc.Configuration?.CalcMin == prior.Configuration?.CalcMin &&
                    npc.Configuration?.CalcMax == prior.Configuration?.CalcMax && npc.Configuration?.Flags == prior.Configuration?.Flags,
                    "Equipment redirects must preserve existing actor deleveling and calculation flags");
        Check(count > 1000 && patch.Weapons.Count == baseline.Weapons.Count && patch.Armors.Count == baseline.Armors.Count && patch.Containers.Count == baseline.Containers.Count, "Broad equipment coverage must not rebalance weapon/armor records or modify merchant/loot containers");
        Check(patch.LeveledItems.All(list => list.FormKey.ModKey == patch.ModKey), "Global source item lists must remain unmodified");
        Console.WriteLine($"Written plugin verified: {count} inventory redirects, {checkedLists.Count} reachable static private lists, no weapon/armor or container changes.");
    }
    finally { foreach (var input in inputs) input.Dispose(); }
}

