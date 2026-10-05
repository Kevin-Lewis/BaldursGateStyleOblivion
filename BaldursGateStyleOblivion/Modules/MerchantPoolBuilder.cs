using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Merchants.Tests")]
namespace BaldursGateStyleOblivion.Modules;

internal sealed class MerchantPoolBuilder
{
    private readonly IReadOnlyDictionary<FormKey, IMajorRecordGetter> records;
    private readonly MerchantSettings settings;
    private readonly Func<ModKey, bool> included;
    private readonly HashSet<FormKey> protectedItems;
    private readonly HashSet<FormKey> unsafeLists;
    private readonly EquipmentPoolBuilder equipment;
    private readonly Dictionary<(FormKey, string, string), FormKey> pools = new();
    private readonly Dictionary<(FormKey, string, string, bool, int), FormKey?> filtered = new();
    private readonly Dictionary<FormKey, string?> inspected = new();
    public OblivionMod Patch { get; }
    public MerchantPoolBuilder(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, MerchantSettings settings,
        ModKey patchKey, uint nextId, Func<ModKey, bool> included, HashSet<FormKey> protectedItems, HashSet<FormKey> unsafeLists)
    {
        this.records = records; this.settings = settings; this.included = included;
        this.protectedItems = protectedItems; this.unsafeLists = unsafeLists;
        equipment = new(records, new EquipmentSettings(), patchKey, nextId, included);
        Patch = new(patchKey, OblivionRelease.Oblivion); Patch.ModHeader.Stats.NextFormID = nextId;
    }
    private void Inspect(FormKey key, HashSet<FormKey> path)
    {
        if (inspected.TryGetValue(key, out var reason))
        {
            if (reason is not null) throw new InvalidDataException(reason);
            return;
        }
        try
        {
            if (!included(key.ModKey) || !records.TryGetValue(key, out var record)) throw new InvalidDataException($"Excluded or unresolved stock dependency {key}.");
            if (record is ILeveledItemGetter list)
            {
                if (settings.PreservedLists.Contains(key.ToString())) throw new InvalidDataException($"Explicitly preserved stock list {key}.");
                if (unsafeLists.Contains(key)) throw new InvalidDataException($"Script-mutated stock list {key} retained.");
                if (!path.Add(key) || path.Count > 100) throw new InvalidDataException($"Stock list cycle or excessive nesting at {key}.");
                if (list.Entries.Count > 255) throw new InvalidDataException($"Oversized stock list {key}.");
                foreach (var entry in list.Entries)
                {
                    if (entry.Count is null or <= 0) throw new InvalidDataException($"Nonpositive stock count at {key}.");
                    Inspect(entry.Reference.FormKey, path);
                }
                path.Remove(key);
            }
            else if (!protectedItems.Contains(key))
            {
                var script = record switch
                {
                    IWeaponGetter x => x.Script.FormKeyNullable, IArmorGetter x => x.Script.FormKeyNullable,
                    IClothingGetter x => x.Script.FormKeyNullable, IMiscellaneousGetter x => x.Script.FormKeyNullable,
                    IPotionGetter x => x.Script.FormKeyNullable, IBookGetter x => x.Script.FormKeyNullable,
                    ILightGetter x => x.Script.FormKeyNullable, _ => null
                };
                if (script is not null) throw new InvalidDataException($"Scripted stock item {key} retained for review.");
                if (record is not IItemGetter) throw new InvalidDataException($"Unsupported stock dependency {key}.");
            }
        }
        catch (InvalidDataException error) { inspected[key] = error.Message; throw; }
        inspected[key] = null;
    }
    public FormKey Build(FormKey root, string profile, string material)
    {
        Inspect(root, []); // Validate the complete source graph before allocating records.
        if (pools.TryGetValue((root, profile, material), out var existing)) return existing;
        var source = (ILeveledItemGetter)records[root];
        if (source.Flags?.HasFlag(LeveledFlag.UseAll) == true && settings.Profiles[profile].GlassPercent > 0)
            throw new InvalidDataException("Glass probability on a UseAll root requires individual review.");
        var ordinary = Filter(root, profile, material, false, 1) ?? Empty();
        var glass = settings.Profiles[profile].GlassPercent > 0 ? Filter(root, profile, material, true, 1) : null;
        if (glass is null) return pools[(root, profile, material)] = ordinary;

        var selector = Patch.LeveledItems.AddNew(); selector.EditorID = $"BGSO_MerchantDraw_{selector.FormKey.ID:X6}";
        selector.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers | (source.Flags.GetValueOrDefault() & LeveledFlag.CalculateForEachItemInCount);
        var rate = settings.Profiles[profile].GlassPercent;
        for (var i = 0; i < 100; i++)
        {
            var entry = new LeveledItemEntry { Level = 1, Count = 1 };
            entry.Reference.SetTo(i < rate ? glass.Value : ordinary); selector.Entries.Add(entry);
        }
        return pools[(root, profile, material)] = selector.FormKey;
    }
    public FormKey GlassOffer(IEnumerable<string> items, string profile)
    {
        var keys = items.Select(item => FormKey.Factory(item)).Distinct().OrderBy(k => k.ToString(), StringComparer.Ordinal).ToArray();
        foreach (var key in keys)
        {
            Inspect(key, []);
                        var enchanted = records[key] switch
            {
                IWeaponGetter weapon => !weapon.Enchantment.IsNull, IArmorGetter armor => !armor.Enchantment.IsNull,
                IAmmunitionGetter ammo => !ammo.Enchantment.IsNull, _ => false
            };
            if (enchanted || protectedItems.Contains(key) || records[key] is not (IWeaponGetter or IArmorGetter or IAmmunitionGetter) ||
                !(records[key].EditorID ?? "").Contains("Glass", StringComparison.OrdinalIgnoreCase) ||
                (records[key].MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0)
                throw new InvalidDataException($"Curated Glass offer must use ordinary Glass equipment: {key}.");
        }
        if (keys.Length == 0) throw new InvalidDataException("Empty curated Glass offer.");
        var offer = Patch.LeveledItems.AddNew(); offer.EditorID = $"BGSO_MerchantGlass_{offer.FormKey.ID:X6}";
        offer.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        offer.ChanceNone = new Percent(1 - settings.Profiles[profile].GlassPercent / 100.0);
        foreach (var key in keys)
        {
            var entry = new LeveledItemEntry { Level = 1, Count = 1 }; entry.Reference.SetTo(key); offer.Entries.Add(entry);
        }
        return offer.FormKey;
    }
    private FormKey Empty()
    {
        var empty = Patch.LeveledItems.AddNew(); empty.EditorID = $"BGSO_MerchantEmpty_{empty.FormKey.ID:X6}";
        empty.ChanceNone = Percent.One; empty.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        return empty.FormKey;
    }
    private FormKey? Filter(FormKey key, string profileName, string material, bool glass, int requirement)
    {
        var record = records[key]; var profile = settings.Profiles[profileName];
        if (record is not ILeveledItemGetter source)
        {
            if (protectedItems.Contains(key) || (record.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0) return null;
            var id = record.EditorID ?? "";
            if (!EquipmentPoolBuilder.IsEquipment(record)) return glass ? null : key;
            if (id.Contains("Daedric", StringComparison.OrdinalIgnoreCase) || id.Contains("Ebony", StringComparison.OrdinalIgnoreCase)) return null;
            var isGlass = id.Contains("Glass", StringComparison.OrdinalIgnoreCase);
            if (glass != isGlass) return null;
            var quality = equipment.Quality(record, requirement);
            var limit = material.Length > 0 && id.Contains(material, StringComparison.OrdinalIgnoreCase)
                ? EquipmentQuality.HighQuality : profile.MaxEquipmentQuality;
            return glass || quality <= limit ? key : null;
        }
        var cache = (key, profileName, material, glass, requirement);
        if (filtered.TryGetValue(cache, out var known)) return known;
        var benchmark = glass ? Math.Max(30, profile.SelectionLevel) : profile.SelectionLevel;
        var entries = new List<(short Level, LeveledItemEntry Entry)>();
        foreach (var original in source.Entries.Where(e => e.Level <= benchmark))
        {
            var target = Filter(original.Reference.FormKey, profileName, material, glass, Math.Max(requirement, original.Level));
            if (target is null) continue;
            var entry = original.DeepCopy(); entry.Level = 1; entry.Reference.SetTo(target.Value);
            entries.Add((original.Level, entry));
        }
        if (entries.Count == 0) return filtered[cache] = null;
        var flags = source.Flags.GetValueOrDefault();
        if (!flags.HasFlag(LeveledFlag.CalculateFromAllLevelsLessThanPlayers) && !flags.HasFlag(LeveledFlag.UseAll))
            entries = entries.Where(e => e.Level == entries.Max(e => e.Level)).ToList();
        var list = Patch.LeveledItems.AddNew(); list.DeepCopyIn(source); list.EditorID = $"BGSO_Merchant_{list.FormKey.ID:X6}";
        list.Entries.Clear(); list.Entries.AddRange(entries.Select(e => e.Entry));
        list.Flags = flags | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        if (!glass && material.Length > 0 && !flags.HasFlag(LeveledFlag.UseAll))
        {
            var preferred = entries.Where(e => records.GetValueOrDefault(e.Entry.Reference.FormKey) is {} item &&
                (item.EditorID ?? "").Contains(material, StringComparison.OrdinalIgnoreCase)).ToArray();
            var copies = preferred.Length == 0 ? 0 : Math.Min(settings.MaterialWeight - 1, (255 - list.Entries.Count) / preferred.Length);
            for (var i = 0; i < copies; i++) list.Entries.AddRange(preferred.Select(e => e.Entry.DeepCopy()));
        }
        return filtered[cache] = list.FormKey;
    }
}
