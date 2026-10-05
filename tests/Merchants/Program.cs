using System.Text.Json;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

void Check(bool good, string message) { if (!good) throw new Exception(message); }
var source = new OblivionMod(ModKey.FromNameAndExtension("Source.esp"), OblivionRelease.Oblivion);
var iron = source.Weapons.AddNew(); iron.EditorID = "WeapIronSword";
var orcish = source.Weapons.AddNew(); orcish.EditorID = "WeapOrcishSword";
var elven = source.Weapons.AddNew(); elven.EditorID = "WeapElvenSword";
var glass = source.Weapons.AddNew(); glass.EditorID = "WeapGlassSword";
var ebony = source.Weapons.AddNew(); ebony.EditorID = "WeapEbonySword";
var daedric = source.Weapons.AddNew(); daedric.EditorID = "WeapDaedricSword";
var artifact = source.Weapons.AddNew(); artifact.EditorID = "ProtectedSword";
var ordinaryPotion = source.Potions.AddNew(); ordinaryPotion.EditorID = "PotionNormal";
var strongPotion = source.Potions.AddNew(); strongPotion.EditorID = "PotionStrong";
LeveledItemEntry Entry(FormKey key, short level = 1, short count = 1)
{ var entry = new LeveledItemEntry { Level = level, Count = count, Unknown = 7, Unknown2 = 9 }; entry.Reference.SetTo(key); return entry; }
var weapons = source.LeveledItems.AddNew(); weapons.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
weapons.Entries.AddRange(new[] { Entry(iron.FormKey), Entry(orcish.FormKey, 15), Entry(elven.FormKey, 15), Entry(glass.FormKey, 20), Entry(ebony.FormKey, 25), Entry(daedric.FormKey, 30), Entry(artifact.FormKey) });
var root = source.LeveledItems.AddNew(); root.Flags = LeveledFlag.CalculateForEachItemInCount;
root.ChanceNone = new Percent(.25); root.Entries.Add(Entry(weapons.FormKey, 1, 2));
var potions = source.LeveledItems.AddNew(); potions.Entries.AddRange(new[] { Entry(ordinaryPotion.FormKey, 1, 2), Entry(strongPotion.FormKey, 20, 3) });
var bundle = source.LeveledItems.AddNew(); bundle.Flags = LeveledFlag.UseAll;
bundle.Entries.AddRange(new[] { Entry(ordinaryPotion.FormKey, 1, 2), Entry(strongPotion.FormKey, 20, 3) });
var records = source.EnumerateMajorRecords().ToDictionary(r => r.FormKey, r => (IMajorRecordGetter)r);
var settings = MerchantConfiguration.Load("BaldursGateStyleOblivion/merchants.json");
MerchantPoolBuilder Builder(HashSet<FormKey>? unsafeLists = null, Func<ModKey, bool>? included = null) =>
    new(records, settings, ModKey.FromNameAndExtension("Patch.esp"), 0x800, included ?? (_ => true), [artifact.FormKey], unsafeLists ?? []);
Check(MerchantStock.CanSell(iron, "Weapons, Repair") && !MerchantStock.CanSell(iron, "Spells, Ingredients"), "Personal sale services distinguish stock from unsold carried weapons.");
Check(MerchantStock.InventoryLeaves(records, root.FormKey, []).Any(item => MerchantStock.CanSell(item, "Weapons")), "Nested personal sale stock detection.");
var builder = Builder();
HashSet<FormKey> Leaves(FormKey key)
{
    if (!builder.Patch.LeveledItems.TryGetValue(key, out var list)) return [key];
    return list.Entries.SelectMany(e => Leaves(e.Reference.FormKey)).ToHashSet();
}
var smith = builder.Build(root.FormKey, "Professional Blacksmith", "");
Check(Leaves(smith).SetEquals([iron.FormKey]), "Ordinary stock must obey quality and material exclusions.");
var preferred = builder.Build(root.FormKey, "Professional Blacksmith", "Orcish");
Check(Leaves(preferred).SetEquals([iron.FormKey, orcish.FormKey]), "Orcish preference admits existing Orcish equipment without adding Elven or rare materials.");
var expert = builder.Build(root.FormKey, "Expert Specialist", "");
Check(Leaves(expert).SetEquals([iron.FormKey, orcish.FormKey, elven.FormKey, glass.FormKey]), "Expert Glass branch excludes Ebony/Daedric/artifacts.");
Check(builder.Build(root.FormKey, "Expert Specialist", "") == expert, "Private pool reuse.");
var low = builder.Build(potions.FormKey, "Alchemist", "");
var high = builder.Build(potions.FormKey, "Expert Alchemist", "");
Check(Leaves(low).SetEquals([ordinaryPotion.FormKey]) && Leaves(high).SetEquals([strongPotion.FormKey]), "Fixed selection benchmarks respect vanilla highest-eligible variants.");
Check(builder.Patch.LeveledItems[high].Entries.Single().Count == 3, "Native reward quantities retained.");
var all = builder.Build(bundle.FormKey, "Expert Alchemist", "");
Check(builder.Patch.LeveledItems[all].Flags!.Value.HasFlag(LeveledFlag.UseAll) && builder.Patch.LeveledItems[all].Entries.Count == 2, "UseAll bundles preserve all eligible goods.");
Check(builder.Patch.LeveledItems.All(l => l.Entries.Count <= 255 && l.Entries.All(e => e.Level == 1)), "Generated stock obeys native limits and has no player-level gates.");
Check(weapons.Entries.Any(e => e.Level == 30) && root.Entries[0].Reference.FormKey == weapons.FormKey, "Original shared source lists unchanged.");
Check(builder.Patch.LeveledItems[smith].ChanceNone!.Value.Value == .25 && builder.Patch.LeveledItems[smith].Entries[0].Unknown == 7, "ChanceNone and native entry data preserved.");
var candidates = builder.Patch.LeveledItems.Where(l => l.Entries.Any(e => e.Reference.FormKey == orcish.FormKey)).ToArray();
Check(candidates.Any(l => l.Entries.Count(e => e.Reference.FormKey == orcish.FormKey) == 8 && l.Entries.Count(e => e.Reference.FormKey == iron.FormKey) == 1), "Strong material weighting.");
void Guard(Action action, string name)
{ try { action(); throw new Exception("Missing guard: " + name); } catch (InvalidDataException) {} }
var guarded = Builder([weapons.FormKey]); Guard(() => guarded.Build(root.FormKey, "General Store", ""), "script-mutated nested list");
Check(guarded.Patch.LeveledItems.Count == 0, "Rejected graph must allocate no private records.");
Guard(() => Builder(included: _ => false).Build(root.FormKey, "General Store", ""), "excluded plugin");
settings.PreservedLists.Add(weapons.FormKey.ToString()); Guard(() => Builder().Build(root.FormKey, "General Store", ""), "explicit list preservation"); settings.PreservedLists.Clear();
weapons.Entries.Add(Entry(root.FormKey)); Guard(() => Builder().Build(root.FormKey, "General Store", ""), "cycle"); weapons.Entries.RemoveAt(weapons.Entries.Count - 1);
var rareOnly = source.LeveledItems.AddNew(); rareOnly.Entries.Add(Entry(daedric.FormKey)); records[rareOnly.FormKey] = rareOnly;
var empty = builder.Build(rareOnly.FormKey, "General Store", "");
Check(builder.Patch.LeveledItems[empty].Entries.Count == 0 && builder.Patch.LeveledItems[empty].ChanceNone == Percent.One, "Banned-only stock becomes empty rather than guaranteeing a rare item.");
var before = builder.Patch.LeveledItems.Count; Guard(() => builder.Build(bundle.FormKey, "Expert Specialist", ""), "Glass on UseAll root");
Check(builder.Patch.LeveledItems.Count == before, "UseAll rejection allocates no orphan pools.");
var invalid = JsonSerializer.Deserialize<MerchantSettings>("{\"Profiles\":{\"General Store\":null}}", MerchantConfiguration.Options)!;
try { MerchantConfiguration.Validate(invalid); throw new Exception("Null profile accepted."); } catch (ArgumentException) {}
var selected = MerchantConfiguration.Select(settings, root.FormKey.ToString(), "Source.esp", "ChestVendorRindirsStaffs01 MerchTrader");
Check(selected.Profile == "Mage Vendor", "Stock specialty precedes general service identity.");
settings.Overrides[root.FormKey.ToString()] = new() { Profile = "Fence", Preserve = true };
Check(MerchantConfiguration.Select(settings, root.FormKey.ToString(), "Source.esp", "MerchSmith").Preserve, "Explicit merchant override wins.");
settings.Overrides.Remove(root.FormKey.ToString());
var rate = settings.Profiles["Expert Specialist"].GlassPercent; settings.Profiles["Expert Specialist"].GlassPercent = 6;
try { MerchantConfiguration.Validate(settings); throw new Exception("Invalid rarity accepted."); } catch (ArgumentException) {} settings.Profiles["Expert Specialist"].GlassPercent = rate;
var curated = builder.GlassOffer([glass.FormKey.ToString()], "Expert Specialist");
Check(builder.Patch.LeveledItems[curated].Entries.Single().Count == 1, "Curated offer has one item per refresh, not a full equipment set.");
Guard(() => builder.GlassOffer([daedric.FormKey.ToString()], "Expert Specialist"), "curated offer rejects non-Glass equipment");
Directory.CreateDirectory("artifacts/merchant-review/fixture");
builder.Patch.WriteToBinary("artifacts/merchant-review/fixture/Patch.esp");
using (var saved = OblivionMod.CreateFromBinaryOverlay("artifacts/merchant-review/fixture/Patch.esp", OblivionRelease.Oblivion))
{
    Check(saved.LeveledItems[empty].Entries.Count == 0, "Empty native stock pool round-trip.");
    Check(saved.LeveledItems.All(l => l.Entries.All(e => e.Level == 1)), "Native round-trip remains static.");
    double Chance(FormKey key, FormKey leaf)
    {
        if (!saved.LeveledItems.TryGetValue(key, out var list)) return key == leaf ? 1 : 0;
        if (list.Entries.Count == 0) return 0;
        return list.Entries.Sum(e => Chance(e.Reference.FormKey, leaf)) / list.Entries.Count * (1 - (list.ChanceNone ?? Percent.Zero).Value);
    }
    Check(Math.Abs(Chance(curated, glass.FormKey) - .01) < 1e-10, "Curated native Glass offer chance is exactly 1%.");
    Check(Math.Abs(Chance(expert, glass.FormKey) - .0075) < 1e-10, "Glass chance remains 1% before native 25% empty chance.");
}
Console.WriteLine("Merchant native fixture checks passed.");
if (args.Length == 3 && args[0] is "--check-patch" or "--check-combined")
{
    using var saved = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    var originals = new Dictionary<FormKey, IMajorRecordGetter>();
    var mods = new List<IOblivionModDisposableGetter>();
    try
    {
        foreach (var name in new[] { "Oblivion.esm", "DLCShiveringIsles.esp", "DLCHorseArmor.esp", "DLCSpellTomes.esp", "DLCOrrery.esp", "DLCVileLair.esp", "DLCMehrunesRazor.esp", "DLCThievesDen.esp", "Knights.esp", "DLCBattlehornCastle.esp", "DLCFrostcrag.esp" })
        {
            var mod = OblivionMod.CreateFromBinaryOverlay(Path.Combine(args[2], name), OblivionRelease.Oblivion); mods.Add(mod);
            foreach (var record in mod.EnumerateMajorRecords()) originals[record.FormKey] = record;
        }
        var protectedKeys = RewardRecords.Protected(RewardConfiguration.Load("BaldursGateStyleOblivion/rewards.json"));
        var generated = saved.LeveledItems.Where(l => (l.EditorID ?? "").StartsWith("BGSO_Merchant", StringComparison.Ordinal)).ToDictionary(l => l.FormKey);
        var reached = new HashSet<FormKey>();
        void Visit(FormKey key)
        {
            if (!reached.Add(key)) return;
            if (generated.TryGetValue(key, out var list))
            {
                foreach (var entry in list.Entries) { Check(entry.Level == 1 && entry.Count > 0, "Native static stock entries."); Visit(entry.Reference.FormKey); }
                return;
            }
            Check(originals.TryGetValue(key, out var item), "Generated stock has an unresolved reference: " + key);
            Check(!protectedKeys.Contains(key), "Protected reward leaked into generated stock: " + key);
            if (EquipmentPoolBuilder.IsEquipment(item)) Check(!(item!.EditorID ?? "").Contains("Daedric", StringComparison.OrdinalIgnoreCase) &&
                !(item!.EditorID ?? "").Contains("Ebony", StringComparison.OrdinalIgnoreCase), "Forbidden random merchant equipment: " + key);
        }
        foreach (var key in generated.Keys) Visit(key);
        Check((args[0] == "--check-patch" ? saved.LeveledItems.ToArray() : generated.Values.ToArray()).All(l => l.FormKey.ModKey == saved.ModKey), "Merchant module must create private lists instead of overriding source lists.");
        Check(saved.Containers.Count > 0 && saved.Npcs.Count > 0, "Candidate must cover linked and personal stock.");
        foreach (var container in saved.Containers.Where(c => (c.EditorID ?? "").StartsWith("BGSO_Merchant", StringComparison.Ordinal)))
            foreach (var entry in container.Items) Check(originals.ContainsKey(entry.Item.FormKey) || generated.ContainsKey(entry.Item.FormKey), "Container reference unresolved.");
        Console.WriteLine($"Official merchant candidate checks passed: {generated.Count} private lists, {saved.Containers.Count(c => (c.EditorID ?? "").StartsWith("BGSO_Merchant", StringComparison.Ordinal))} private stock containers.");
    }
    finally { foreach (var mod in mods) mod.Dispose(); }
}
