using BaldursGateStyleOblivion.Core;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Equipment.Tests")]

namespace BaldursGateStyleOblivion.Modules;

internal sealed class EquipmentPoolBuilder
{
    private readonly IReadOnlyDictionary<FormKey, IMajorRecordGetter> records;
    private readonly EquipmentSettings settings;
    private readonly Func<ModKey, bool> included;
    private readonly Dictionary<(FormKey, string, string), FormKey> pools = new();
    private readonly Dictionary<(FormKey, EquipmentQuality?, bool?, string, int, bool, string), FormKey?> filtered = new();
    private readonly HashSet<FormKey> protectedArtifacts;
    public OblivionMod Patch { get; }

    public EquipmentPoolBuilder(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, EquipmentSettings settings,
        ModKey patchKey, uint nextId, Func<ModKey, bool> included, HashSet<FormKey>? protectedArtifacts = null)
    {
        this.records = records; this.settings = settings; this.included = included; this.protectedArtifacts = protectedArtifacts ?? [];
        Patch = new(patchKey, OblivionRelease.Oblivion); Patch.ModHeader.Stats.NextFormID = nextId;
    }

    public static bool IsEquipment(IMajorRecordGetter? record) => record is IWeaponGetter or IArmorGetter or IAmmunitionGetter or IClothingGetter;

    public EquipmentQuality Quality(IMajorRecordGetter record, int requirement = 1)
    {
        if (settings.ItemOverrides.TryGetValue(record.FormKey.ToString(), out var quality)) return quality;
        var id = (record.EditorID ?? "")+" "+BaldursGateStyleOblivion.Combat.PhysicalCombatModule.Material(record.EditorID);
        quality = Regex.IsMatch(id, "Daedric", RegexOptions.IgnoreCase) ? EquipmentQuality.Rare
            : Regex.IsMatch(id, "Ebony|Glass", RegexOptions.IgnoreCase) ? EquipmentQuality.Elite
            : Regex.IsMatch(id, "Orcish|Elven|Mithril|Madness|Amber", RegexOptions.IgnoreCase) ? EquipmentQuality.HighQuality
            : Regex.IsMatch(id, "Dwarven|Chainmail", RegexOptions.IgnoreCase) ? EquipmentQuality.Military
            : Regex.IsMatch(id, "Silver|Upper", RegexOptions.IgnoreCase) ? EquipmentQuality.Professional
            : Regex.IsMatch(id, "Steel|Leather|Middle", RegexOptions.IgnoreCase) ? EquipmentQuality.Common
            : Regex.IsMatch(id, "Iron|Fur|Lower|Rusty", RegexOptions.IgnoreCase) ? EquipmentQuality.Poor
            : Regex.IsMatch(id, "Dremora|GoldenSaint|DarkSeducer|OrderKnight", RegexOptions.IgnoreCase) ? EquipmentQuality.Military
            : EquipmentQuality.Common;
        // Original eligibility describes stronger enchantment/variant records, not a new player-level dependency.
        if (Enchanted(record))
        {
            var variant = requirement >= 25 ? EquipmentQuality.Elite : requirement >= 20 ? EquipmentQuality.HighQuality
                : requirement >= 15 ? EquipmentQuality.Military : requirement >= 10 ? EquipmentQuality.Professional : EquipmentQuality.Common;
            quality = (EquipmentQuality)Math.Max((int)quality, (int)variant);
        }
        return quality;
    }

    private static bool Enchanted(IMajorRecordGetter record) => record switch
    {
        IWeaponGetter item => item.Enchantment.FormKeyNullable is not null,
        IArmorGetter item => item.Enchantment.FormKeyNullable is not null,
        IAmmunitionGetter item => item.Enchantment.FormKeyNullable is not null,
        IClothingGetter item => item.Enchantment.FormKeyNullable is not null,
        _ => false
    };

    private static FormKey? Script(IMajorRecordGetter record) => record switch
    {
        IWeaponGetter item => item.Script.FormKeyNullable,
        IArmorGetter item => item.Script.FormKeyNullable,
        IClothingGetter item => item.Script.FormKeyNullable,
        _ => null
    };

    public Dictionary<(EquipmentQuality Quality, bool Enchanted, string Material), int> Inspect(FormKey key)
    {
        var result = new Dictionary<(EquipmentQuality, bool, string), int>();
        void Visit(FormKey current, int requirement, HashSet<FormKey> path)
        {
            if (!included(current.ModKey)) throw new InvalidDataException($"Excluded dependency {current}.");
            if (!records.TryGetValue(current, out var record)) throw new InvalidDataException($"Unresolved dependency {current}.");
            if (record is ILeveledItemGetter list)
            {
                if (!path.Add(current)) throw new InvalidDataException($"List cycle at {current}.");
                if (settings.ListOverrides.GetValueOrDefault(current.ToString())?.Preserve == true)
                    throw new InvalidDataException($"Explicitly preserved list {current}.");
                if (Regex.IsMatch(list.EditorID ?? "", @"^(MQ|MG|FG|TG|DB|DA|SQ|MS|SE\d|ND|Test|Arena)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException($"Quest or test equipment list {current} retained.");
                if (list.Entries is null || list.Entries.Count == 0) throw new InvalidDataException($"Empty list {current}.");
                if (list.Flags?.HasFlag(LeveledFlag.UseAll) == true)
                    throw new InvalidDataException($"UseAll equipment bundle {current} retained.");
                foreach (var entry in list.Entries) Visit(entry.Reference.FormKey, Math.Max(requirement, entry.Level), path);
                path.Remove(current); return;
            }
            if (protectedArtifacts.Contains(current)) throw new InvalidDataException($"Protected artifact {current} retained.");
            if ((record.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0) throw new InvalidDataException($"Quest-item equipment {current} retained.");
            if (!IsEquipment(record)) throw new InvalidDataException("Mixed loot or consumable list; deferred to loot distribution.");
            var script = Script(record);
            if (script is not null) throw new InvalidDataException($"Scripted equipment {current} retained.");
            if (Quality(record, requirement) == EquipmentQuality.Artifact) throw new InvalidDataException($"Artifact equipment {current} retained.");
            var band = (Quality(record, requirement), Enchanted(record), Material(record));
            result[band] = result.GetValueOrDefault(band) + 1;
        }
        Visit(key, 1, []); return result;
    }

    public (FormKey Key, string Reason) Build(FormKey root, string profile, string preferredMaterial = "")
    {
        var available = Inspect(root); // Complete validation before allocating records.
        if (settings.ListOverrides.TryGetValue(root.ToString(), out var assignment) && assignment.Profile is {} selected) profile = selected;
        var distribution = settings.Profiles[profile];
        if (!distribution.PreferRaceMaterial || distribution.Weights.GetValueOrDefault(EquipmentQuality.HighQuality) == 0 ||
            settings.ListOverrides.ContainsKey(root.ToString()) || !HasPreferredMaterial(root, preferredMaterial, 1)) preferredMaterial = "";
        if (pools.TryGetValue((root, profile, preferredMaterial), out var cached)) return (cached, "Reused private equipment pool.");
        var ordinary = available.Keys.Where(band => band.Material == "Other").ToArray();
        var weights = ordinary.Select(band => band.Quality).Distinct().Order().ToDictionary(quality => quality,
            quality => distribution.Weights.GetValueOrDefault(quality));
        var reason = "Static quality and enchantment weights; Ebony/Daedric use separate encounter eligibility and rarity.";
        var choices = new Dictionary<FormKey, int>();
        FormKey ordinaryBranch;
        if (ordinary.Length == 0)
        {
            _ = Fallback(root, true, true);
            ordinaryBranch = Fallback(root, true);
            reason += " Restricted precious-only pool: supplied compatible mundane equipment for the common outcomes.";
        }
        else
        {
            if (weights.Values.Sum() == 0)
            {
                weights[weights.Keys.Min()] = 1;
                reason += " Retained the lowest available ordinary faction quality.";
            }
            foreach (var (quality, weight) in weights.Where(pair => pair.Value > 0))
            {
                var hasPlain = ordinary.Any(band => band.Quality == quality && !band.Enchanted);
                var hasEnchanted = ordinary.Any(band => band.Quality == quality && band.Enchanted);
                var plain = hasPlain && (distribution.EnchantedPercent < 100 || !hasEnchanted) ? Filter(root, quality, false, "Other", 1, true, preferredMaterial) : null;
                var enchanted = hasEnchanted && (distribution.EnchantedPercent > 0 || plain is null) ? Filter(root, quality, true, "Other", 1, true, preferredMaterial) : null;
                FormKey branch;
                if (plain is null) branch = enchanted!.Value;
                else if (enchanted is null) branch = plain.Value;
                else branch = Selector(new() { [plain.Value] = 100 - distribution.EnchantedPercent, [enchanted.Value] = distribution.EnchantedPercent });
                choices[branch] = weight;
            }
            ordinaryBranch = choices.Count == 1 ? choices.Keys.Single() : Selector(choices);
        }
        choices = new() { [ordinaryBranch] = 1000 };
        foreach (var (material, rate) in new[] { ("Ebony", distribution.EbonyPerThousand), ("Daedric", distribution.DaedricPerThousand) })
        {
            if (rate == 0 || !available.Keys.Any(band => band.Material == material)) continue;
            var rare = Filter(root, null, null, material, 1, true)!.Value;
            choices[rare] = rate; choices[ordinaryBranch] -= rate;
        }
        var source = (ILeveledItemGetter)records[root];
        var target = Patch.LeveledItems.AddNew(); target.DeepCopyIn(source); target.EditorID = $"BGSO_Equipment_{target.FormKey.ID:X6}";
        target.Entries.Clear(); target.Entries.AddRange(Tickets(choices).Select(key => Entry(key)));
        target.Flags = (source.Flags ?? 0) | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        if (preferredMaterial.Length > 0) reason += $" High-quality {preferredMaterial} options receive a strong race preference; quality and precious-material chances are unchanged.";
        pools[(root, profile, preferredMaterial)] = target.FormKey;
        return (target.FormKey, reason);
    }

    private FormKey? Filter(FormKey key, EquipmentQuality? quality, bool? enchanted, string material, int requirement, bool root, string preferredMaterial = "")
    {
        if (records[key] is not ILeveledItemGetter source)
            return (quality is null || Quality(records[key], requirement) == quality) &&
                (enchanted is null || Enchanted(records[key]) == enchanted) && Material(records[key]) == material ? key : null;
        var cache = (key, quality, enchanted, material, requirement, root, preferredMaterial);
        if (filtered.TryGetValue(cache, out var existing)) return existing;
        var entries = new List<LeveledItemEntry>();
        foreach (var original in source.Entries!)
        {
            var reference = Filter(original.Reference.FormKey, quality, enchanted, material, Math.Max(requirement, original.Level), false, preferredMaterial);
            if (reference is null) continue;
            var entry = original.DeepCopy(); entry.Level = 1; entry.Reference.SetTo(reference.Value); entries.Add(entry);
        }
        if (entries.Count == 0) { filtered[cache] = null; return null; }
        if (quality == EquipmentQuality.HighQuality && preferredMaterial.Length > 0)
        {
            var preferred = entries.Where(entry => records.GetValueOrDefault(entry.Reference.FormKey) is {} item &&
                (item.EditorID ?? "").Contains(preferredMaterial, StringComparison.OrdinalIgnoreCase)).ToArray();
            var extraCopies = preferred.Length == 0 ? 0 : Math.Min(settings.RaceMaterialWeight - 1, (255 - entries.Count) / preferred.Length);
            for (var copy = 0; copy < extraCopies; copy++) entries.AddRange(preferred.Select(entry => entry.DeepCopy()));
        }
        var target = Patch.LeveledItems.AddNew(); target.DeepCopyIn(source); target.EditorID = $"BGSO_EquipmentBand_{target.FormKey.ID:X6}";
        target.Entries.Clear(); target.Entries.AddRange(entries); target.Flags = (source.Flags ?? 0) | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        if (root) target.ChanceNone = Percent.Zero; // Original root chance is applied once by the outer pool.
        filtered[cache] = target.FormKey; return target.FormKey;
    }

    private bool HasPreferredMaterial(FormKey key, string material, int requirement)
    {
        if (material.Length == 0) return false;
        if (records[key] is ILeveledItemGetter list)
            return list.Entries.Any(entry => HasPreferredMaterial(entry.Reference.FormKey, material, Math.Max(requirement, entry.Level)));
        return Quality(records[key], requirement) == EquipmentQuality.HighQuality &&
            (records[key].EditorID ?? "").Contains(material, StringComparison.OrdinalIgnoreCase);
    }

    private static string Material(IMajorRecordGetter record) => Regex.IsMatch(record.EditorID ?? "", "Daedric", RegexOptions.IgnoreCase)
        ? "Daedric" : Regex.IsMatch(record.EditorID ?? "", "Ebony", RegexOptions.IgnoreCase) ? "Ebony" : "Other";

    private FormKey Fallback(FormKey key, bool root, bool validateOnly = false)
    {
        if (records[key] is not ILeveledItemGetter source)
        {
            var item = records[key];
            bool Compatible(IMajorRecordGetter candidate) => (item, candidate) switch
            {
                (IWeaponGetter a, IWeaponGetter b) => a.Data?.Type == b.Data?.Type,
                (IArmorGetter a, IArmorGetter b) => a.ClothingFlags?.BipedFlags == b.ClothingFlags?.BipedFlags &&
                    ArmorFlags.IsHeavy(a) == ArmorFlags.IsHeavy(b),
                (IAmmunitionGetter, IAmmunitionGetter) => true,
                (IClothingGetter a, IClothingGetter b) => a.ClothingFlags?.BipedFlags == b.ClothingFlags?.BipedFlags,
                _ => false
            };
            var replacement = records.Values.Where(candidate => included(candidate.FormKey.ModKey) && !protectedArtifacts.Contains(candidate.FormKey) && Compatible(candidate) &&
                    Material(candidate) == "Other" && !Enchanted(candidate) && Script(candidate) is null &&
                    Regex.IsMatch(candidate.EditorID ?? "", @"^(WeapSteel|ArmorSteel|ArmorLeather|Dremora|ArrowSteel)", RegexOptions.IgnoreCase) &&
                    (candidate.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) == 0)
                .OrderBy(candidate => candidate.FormKey.ModKey != key.ModKey)
                .ThenBy(candidate => !(candidate.EditorID ?? "").StartsWith("Dremora", StringComparison.OrdinalIgnoreCase))
                .ThenBy(candidate => candidate.FormKey.ToString(), StringComparer.Ordinal).FirstOrDefault();
            return replacement?.FormKey ?? throw new InvalidDataException($"No compatible mundane replacement for restricted equipment {key}.");
        }
        if (validateOnly)
        {
            foreach (var entry in source.Entries) _ = Fallback(entry.Reference.FormKey, false, true);
            return key;
        }
        // Keep all native multiplicities and counts while replacing precious-only leaves.
        var entries = source.Entries.Select(original =>
        {
            var entry = original.DeepCopy(); entry.Level = 1; entry.Reference.SetTo(Fallback(original.Reference.FormKey, false)); return entry;
        }).ToArray();
        var target = Patch.LeveledItems.AddNew(); target.DeepCopyIn(source); target.EditorID = $"BGSO_EquipmentFallback_{target.FormKey.ID:X6}";
        target.Entries.Clear(); target.Entries.AddRange(entries);
        target.Flags = (source.Flags ?? 0) | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        if (root) target.ChanceNone = Percent.Zero;
        return target.FormKey;
    }

    private FormKey Selector(Dictionary<FormKey, int> choices)
    {
        var list = Patch.LeveledItems.AddNew(); list.EditorID = $"BGSO_EquipmentChoice_{list.FormKey.ID:X6}";
        list.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers | LeveledFlag.CalculateForEachItemInCount;
        list.Entries.AddRange(Tickets(choices).Select(key => Entry(key))); return list.FormKey;
    }

    private IEnumerable<FormKey> Tickets(Dictionary<FormKey, int> weights)
    {
        // A fixed 1000 draws permits 0.1% rarity without exceeding the native 255-entry limit.
        int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
        var divisor = weights.Values.Aggregate(Gcd);
        if (weights.Values.Sum() / divisor <= 255)
            return weights.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value / divisor)).ToArray();
        var total = weights.Values.Sum();
        var allocation = weights.Select(pair => (pair.Key, Exact: pair.Value * 1000.0 / total)).ToArray();
        var counts = allocation.ToDictionary(pair => pair.Key, pair => (int)pair.Exact);
        foreach (var pair in allocation.OrderByDescending(pair => pair.Exact - (int)pair.Exact).ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                     .Take(1000 - counts.Values.Sum())) counts[pair.Key]++;
        var tickets = counts.SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).ToArray();
        if (counts.Count == 1) return [counts.Keys.Single()];
        // Ten equally sized pages preserve exact probabilities and avoid truncation.
        return tickets.Chunk(100).Select(page =>
        {
            var list = Patch.LeveledItems.AddNew(); list.EditorID = $"BGSO_EquipmentDraw_{list.FormKey.ID:X6}";
            list.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers | LeveledFlag.CalculateForEachItemInCount;
            list.Entries.AddRange(page.Select(key => Entry(key))); return list.FormKey;
        }).ToArray();
    }

    private static LeveledItemEntry Entry(FormKey key)
    {
        var entry = new LeveledItemEntry { Level = 1, Count = 1 }; entry.Reference.SetTo(key); return entry;
    }
}
