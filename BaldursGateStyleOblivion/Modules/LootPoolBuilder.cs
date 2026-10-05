using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Loot.Tests")]
namespace BaldursGateStyleOblivion.Modules;

internal sealed class LootPoolBuilder
{
    private readonly IReadOnlyDictionary<FormKey, IMajorRecordGetter> records;
    private readonly LootSettings settings;
    private readonly Func<ModKey, bool> included;
    private readonly EquipmentPoolBuilder equipment;
    private readonly Dictionary<FormKey, HashSet<string>> inspected = new();
    private readonly Dictionary<FormKey, string> rejected = new();
    private readonly Dictionary<(FormKey, string), FormKey> pools = new();
    private readonly Dictionary<(FormKey, string, int, bool), FormKey?> filtered = new();
    private readonly Dictionary<(FormKey, string, bool, string), double> chanceCache = new();
    private readonly Dictionary<FormKey, double> valueCache = new();
    private readonly Dictionary<FormKey, string> bandPools = new();
    private readonly Dictionary<(FormKey, string), Dictionary<string, double>> expectedCache = new();
    private readonly HashSet<FormKey> protectedArtifacts;
    public OblivionMod Patch { get; }
    public LootPoolBuilder(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, LootSettings settings, ModKey key, uint nextId, Func<ModKey, bool> included, HashSet<FormKey>? protectedArtifacts = null)
    {
        this.records = records; this.settings = settings; this.included = included; this.protectedArtifacts = protectedArtifacts ?? [];
        equipment = new(records, new EquipmentSettings(), key, nextId, included);
        Patch = new(key, OblivionRelease.Oblivion); Patch.ModHeader.Stats.NextFormID = nextId;
    }
    internal string Band(IMajorRecordGetter item, int requirement)
    {
        var id = item.EditorID ?? "";
        if (Regex.IsMatch(id, "Daedric", RegexOptions.IgnoreCase) && EquipmentPoolBuilder.IsEquipment(item)) return "Daedric";
        if (Regex.IsMatch(id, "Ebony", RegexOptions.IgnoreCase) && EquipmentPoolBuilder.IsEquipment(item)) return "Ebony";
        if (Regex.IsMatch(id, "Glass", RegexOptions.IgnoreCase) && EquipmentPoolBuilder.IsEquipment(item)) return "Glass";
        var tier = settings.ItemTiers.GetValueOrDefault(item.FormKey.ToString(), -1);
        if (tier >= 0) return tier >= 4 ? "Premium" : "Ordinary";
        if (EquipmentPoolBuilder.IsEquipment(item)) return equipment.Quality(item, requirement) >= EquipmentQuality.HighQuality ? "Premium" : "Ordinary";
        // Eligibility is source evidence for potion/scroll strength, never a player-level input.
        if (item is IPotionGetter or IBookGetter or ISoulGemGetter or IAlchemicalApparatusGetter && requirement >= 15) return "Premium";
        if (Regex.IsMatch(id, "Grand|Greater|Strong|Perfect|Flawless|Diamond", RegexOptions.IgnoreCase)) return "Premium";
        return "Ordinary";
    }
    private HashSet<string> Inspect(FormKey root)
    {
        if (inspected.TryGetValue(root, out var known)) return known;
        if (rejected.TryGetValue(root, out var reason)) throw new InvalidDataException(reason);
        var bands = new HashSet<string>();
        void Visit(FormKey key, int requirement, HashSet<FormKey> path)
        {
            if (!included(key.ModKey) || !records.TryGetValue(key, out var item)) throw new InvalidDataException($"Excluded or unresolved loot dependency {key}.");
            if (item is ILeveledItemGetter list)
            {
                if (!path.Add(key)) throw new InvalidDataException($"Loot list cycle at {key}.");
                if (settings.ListOverrides.GetValueOrDefault(key.ToString())?.Preserve == true) throw new InvalidDataException($"List explicitly preserved: {key}.");
                if (list.Entries.Count == 0 || list.Entries.Count > 255) throw new InvalidDataException($"Empty or oversized loot list {key}.");
                if (list.Flags?.HasFlag(LeveledFlag.UseAll) == true) throw new InvalidDataException($"UseAll bundle {key}: retained for review.");
                if (Regex.IsMatch(list.EditorID ?? "", @"^(MQ|MG|FG|TG|DB|DA|SQ|MS|SE\d|Test|Arena)|Sigil", RegexOptions.IgnoreCase))
                    throw new InvalidDataException($"Quest/reward list {key}: retained for review.");
                foreach (var entry in list.Entries)
                {
                    if (entry.Count is null or <= 0) throw new InvalidDataException($"Nonpositive loot count at {key}.");
                    Visit(entry.Reference.FormKey, Math.Max(requirement, entry.Level), path);
                }
                path.Remove(key); return;
            }
            if (protectedArtifacts.Contains(key)) throw new InvalidDataException($"Protected artifact {key} retained.");
            if ((item.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0)
                throw new InvalidDataException($"Quest item {key} retained.");
            var script = item switch { IWeaponGetter x => x.Script.FormKeyNullable, IArmorGetter x => x.Script.FormKeyNullable,
                IClothingGetter x => x.Script.FormKeyNullable, IMiscellaneousGetter x => x.Script.FormKeyNullable,
                IPotionGetter x => x.Script.FormKeyNullable, IBookGetter x => x.Script.FormKeyNullable, ILightGetter x => x.Script.FormKeyNullable, _ => null };
            if (script is not null && !TeachingTome(item, script.Value) && !ReviewedItemScript(script.Value)) throw new InvalidDataException($"Scripted loot item {key} retained.");
            if (item is not (IWeaponGetter or IArmorGetter or IAmmunitionGetter or IClothingGetter or IPotionGetter or IIngredientGetter or IMiscellaneousGetter or IBookGetter or ISoulGemGetter or IKeyGetter or IAlchemicalApparatusGetter or ILightGetter))
                throw new InvalidDataException($"Unsupported loot type {item.GetType().Name}.");
            bands.Add(Band(item, requirement));
        }
        try { Visit(root, 1, []); }
        catch (InvalidDataException error) { rejected[root] = error.Message; throw; }
        inspected[root] = bands; return bands;
    }
    private bool ReviewedItemScript(FormKey key) => settings.ReviewedItemScripts.TryGetValue(key.ToString(), out var expected) &&
        records.GetValueOrDefault(key) is IScriptGetter script && script.Fields is {} fields && RewardRecords.Fingerprint(fields) == expected;

    private bool TeachingTome(IMajorRecordGetter item, FormKey script)
    {
        if (item is not IBookGetter || item.FormKey.ModKey.ToString() != "DLCSpellTomes.esp" ||
            records.GetValueOrDefault(script) is not IScriptGetter definition || definition.Fields?.SourceCode is not {} code) return false;
        code = string.Join("\n", code.Split('\n').Select(line => line.Split(';')[0]));
        return Regex.IsMatch(code, @"\bAddSpell\b", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(code, @"\b(SetStage|SetQuestObject|AddItem|RemoveItem|RemoveAllItems|AddToLeveledList|RemoveFromLeveledList)\b", RegexOptions.IgnoreCase);
    }
    public (FormKey Key, string Reason) Build(FormKey root, string profile)
    {
        if (settings.ListOverrides.GetValueOrDefault(root.ToString())?.Profile is {} manual) profile = manual;
        var available = Inspect(root);

        if (pools.TryGetValue((root, profile), out var cached)) return (cached, "Reused static private loot pool.");
        var rates = settings.Profiles[profile];
        var choices = new Dictionary<FormKey, int>();
        var ordinary = Filter(root, "Ordinary", 1, true);
        var premiumOnly = ordinary is null;
        if (premiumOnly)
        {
            // Chance None stores whole percentages; an empty choice preserves fractional material rates exactly.
            var empty = Patch.LeveledItems.AddNew(); empty.EditorID = $"BGSO_LootEmpty_{empty.FormKey.ID:X6}";
            empty.ChanceNone = Percent.One; empty.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
            empty.Entries.Add(Entry(Filter(root, available.Order(StringComparer.Ordinal).First(), 1, true)!.Value));
            ordinary = empty.FormKey;
        }
        choices[ordinary!.Value] = 1000;
        foreach (var (band, rate) in new[] { ("Premium", rates.PremiumPerThousand), ("Glass", rates.GlassPerThousand), ("Ebony", rates.EbonyPerThousand), ("Daedric", rates.DaedricPerThousand) })
        {
            if (rate == 0 || !available.Contains(band)) continue;
            choices[Filter(root, band, 1, true)!.Value] = rate;
            if (ordinary is {} commonKey) choices[commonKey] -= rate;
        }
        var source = (ILeveledItemGetter)records[root];
        var target = Patch.LeveledItems.AddNew(); target.DeepCopyIn(source); target.EditorID = $"BGSO_Loot_{target.FormKey.ID:X6}";
        target.Entries.Clear(); target.Entries.AddRange(Tickets(choices.Where(pair => pair.Value > 0).ToDictionary()).Select(Entry));
        target.Flags = (source.Flags ?? 0) | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        pools[(root, profile)] = target.FormKey;
        return (target.FormKey, premiumOnly
            ? "Static premium-only family; profile rarity adds an empty outcome instead of guaranteeing valuable loot."
            : "Static source-family loot; danger changes premium/material odds, counts and native Chance None retained.");
    }
    private FormKey? Filter(FormKey key, string band, int requirement, bool root)
    {
        if (records[key] is not ILeveledItemGetter source) return Band(records[key], requirement) == band ? key : null;
        var cache = (key, band, requirement, root);
        if (filtered.TryGetValue(cache, out var existing)) return existing;
        var entries = new List<LeveledItemEntry>();
        foreach (var original in source.Entries)
        {
            var target = Filter(original.Reference.FormKey, band, Math.Max(requirement, original.Level), false);
            if (target is null) continue;
            var entry = original.DeepCopy(); entry.Level = 1; entry.Reference.SetTo(target.Value); entries.Add(entry);
        }
        if (entries.Count == 0) return filtered[cache] = null;
        var list = Patch.LeveledItems.AddNew(); list.DeepCopyIn(source); list.EditorID = $"BGSO_LootBand_{list.FormKey.ID:X6}";
        list.Entries.Clear(); list.Entries.AddRange(entries); list.Flags = (source.Flags ?? 0) | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        if (root) list.ChanceNone = Percent.Zero;
        bandPools[list.FormKey] = band;
        return filtered[cache] = list.FormKey;
    }
    private IEnumerable<FormKey> Tickets(Dictionary<FormKey, int> choices)
    {
        int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
        var divisor = choices.Values.Aggregate(Gcd);
        if (choices.Values.Sum() / divisor <= 255)
            return choices.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value / divisor)).ToArray();
        var tickets = choices.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).ToArray();
        return tickets.Chunk(100).Select(page =>
        {
            var list = Patch.LeveledItems.AddNew(); list.EditorID = $"BGSO_LootDraw_{list.FormKey.ID:X6}";
            list.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers | LeveledFlag.CalculateForEachItemInCount;
            list.Entries.AddRange(page.Select(Entry)); return list.FormKey;
        }).ToArray();
    }
    private static LeveledItemEntry Entry(FormKey key)
    {
        var entry = new LeveledItemEntry { Level = 1, Count = 1 }; entry.Reference.SetTo(key); return entry;
    }
    public double Chance(FormKey key, int count, bool premium = false, string band = "", string material = "")
    {
        band = bandPools.GetValueOrDefault(key, band);
        var cache = (key, band, premium, material);
        var item = Patch.LeveledItems.TryGetValue(key, out var planned) ? planned : records.GetValueOrDefault(key);
        if (!chanceCache.TryGetValue(cache, out var chance))
        {
            if (item is ILeveledItemGetter list)
                chance = list.Entries.Sum(entry => Chance(entry.Reference.FormKey, entry.Count ?? 1, premium, band, material)) / list.Entries.Count * (1 - (list.ChanceNone ?? Percent.Zero).Value);
            else
            {
                var selected = band.Length > 0 ? band : item is null ? "" : Band(item, 1);
                chance = (material.Length > 0 ? selected == material : selected is "Ebony" or "Daedric" || premium && (selected is "Premium" or "Glass")) ? 1 : 0;
            }
            chanceCache[cache] = chance;
        }
        return item is ILeveledItemGetter source && source.Flags?.HasFlag(LeveledFlag.CalculateForEachItemInCount) == true
            ? 1 - Math.Pow(1 - chance, count) : chance;
    }
    public double ExpectedValue(FormKey key)
    {
        if (valueCache.TryGetValue(key, out var known)) return known;
        var item = Patch.LeveledItems.TryGetValue(key, out var planned) ? planned : records.GetValueOrDefault(key);
        double value = item switch
        {
            IWeaponGetter x => x.Data?.Value ?? 0, IArmorGetter x => x.Data?.Value ?? 0,
            IClothingGetter x => x.Data?.Value ?? 0, IAmmunitionGetter x => x.Data?.Value ?? 0,
            IMiscellaneousGetter x => x.Data?.Value ?? 0, IPotionGetter x => x.Data?.Value ?? 0,
            IBookGetter x => x.Data?.Value ?? 0, IIngredientGetter x => x.Data?.Value ?? 0, IAlchemicalApparatusGetter x => x.Data?.Value ?? 0,
            _ => 0
        };
        if (item is ILeveledItemGetter list)
            value = list.Entries.Sum(entry => ExpectedValue(entry.Reference.FormKey) * (entry.Count ?? 1)) / list.Entries.Count * (1 - (list.ChanceNone ?? Percent.Zero).Value);
        valueCache[key] = value; return value;
    }
    // Expected item yield includes nested Chance None and native entry quantities; it is not a probability per chest.
    public Dictionary<string, double> Expected(FormKey key, string selectedBand = "")
    {
        selectedBand = bandPools.GetValueOrDefault(key, selectedBand);
        var cache = (key, selectedBand);
        if (expectedCache.TryGetValue(cache, out var known)) return known;
        var result = new Dictionary<string, double>();
        var item = Patch.LeveledItems.TryGetValue(key, out var planned) ? planned : records.GetValueOrDefault(key);
        if (item is not ILeveledItemGetter list) { if (item is not null) result[selectedBand.Length > 0 ? selectedBand : Band(item, 1)] = 1; return result; }
        foreach (var entry in list.Entries)
            foreach (var pair in Expected(entry.Reference.FormKey, selectedBand))
                result[pair.Key] = result.GetValueOrDefault(pair.Key) + pair.Value * (entry.Count ?? 1) * (1 - (list.ChanceNone ?? Percent.Zero).Value) / list.Entries.Count;
        expectedCache[cache] = result;
        return result;
    }
}
