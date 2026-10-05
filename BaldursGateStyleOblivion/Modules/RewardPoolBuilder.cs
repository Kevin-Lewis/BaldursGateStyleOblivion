using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Rewards.Tests")]
namespace BaldursGateStyleOblivion.Modules;

internal sealed class RewardPoolBuilder
{
    private readonly IReadOnlyDictionary<FormKey, IMajorRecordGetter> records;
    private readonly Func<ModKey, bool> included;
    private readonly Dictionary<(FormKey, int), FormKey> nested = new();
    public OblivionMod Patch { get; }
    public RewardPoolBuilder(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, ModKey key, uint nextId, Func<ModKey, bool> included)
    {
        this.records = records; this.included = included;
        Patch = new(key, OblivionRelease.Oblivion); Patch.ModHeader.Stats.NextFormID = nextId;
    }
    public LeveledItem Build(ILeveledItemGetter source, RewardChoice choice)
    {
        if (choice.Fingerprint.Length > 0 && RewardRecords.Fingerprint(source) != choice.Fingerprint)
            throw new InvalidDataException("Reviewed reward list changed; retained.");
        Inspect(source.FormKey, choice.SelectionLevel, []);
        var entries = Eligible(source, choice.SelectionLevel);
        if (choice.Variant is {} variant)
        {
            var key = FormKey.Factory(variant);
            var selected = source.Entries.Where(e => e.Reference.FormKey == key).ToArray();
            if (selected.Length == 0) throw new InvalidDataException("Explicit variant is not a direct member of this reward family.");
            Inspect(key, choice.SelectionLevel, []);
            entries = [selected.OrderByDescending(e => e.Level).First()];
        }
        if (choice.Count is not null && entries.Length != 1) throw new InvalidDataException("Fixed count requires a single reward outcome.");
        var target = new LeveledItem(source.FormKey, OblivionRelease.Oblivion); target.DeepCopyIn(source);
        target.Entries.Clear();
        foreach (var original in entries)
        {
            var entry = original.DeepCopy(); entry.Level = 1;
            if (choice.Count is {} count) entry.Count = (short)count;
            if (records[entry.Reference.FormKey] is ILeveledItemGetter child) entry.Reference.SetTo(CopyNested(child, choice.SelectionLevel));
            target.Entries.Add(entry);
        }
        target.Flags = (source.Flags ?? 0) | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
        return target;
    }
    private static ILeveledItemEntryGetter[] Eligible(ILeveledItemGetter list, int level)
    {
        var eligible = list.Entries.Where(e => e.Level <= level).ToArray();
        if (eligible.Length == 0) throw new InvalidDataException("No reward entries at the configured selection level.");
        if (list.Flags?.HasFlag(LeveledFlag.CalculateFromAllLevelsLessThanPlayers) != true && list.Flags?.HasFlag(LeveledFlag.UseAll) != true)
            eligible = eligible.Where(e => e.Level == eligible.Max(e => e.Level)).ToArray();
        return eligible;
    }
    private void Inspect(FormKey key, int level, HashSet<FormKey> path)
    {
        if (!included(key.ModKey) || !records.TryGetValue(key, out var record)) throw new InvalidDataException($"Excluded or unresolved reward {key}.");
        if (record is not ILeveledItemGetter list) return;
        if (!path.Add(key)) throw new InvalidDataException($"Reward list cycle: {key}.");
        foreach (var entry in Eligible(list, level))
        {
            if (entry.Count is <= 0) throw new InvalidDataException("Nonpositive reward entry count.");
            Inspect(entry.Reference.FormKey, level, path);
        }
        path.Remove(key);
    }
    private FormKey CopyNested(ILeveledItemGetter source, int level)
    {
        if (nested.TryGetValue((source.FormKey, level), out var existing)) return existing;
        var selected = Build(source, new() { SelectionLevel = level });
        var copy = Patch.LeveledItems.AddNew(); copy.DeepCopyIn(selected); copy.EditorID = $"BGSO_Reward_{copy.FormKey.ID:X6}";
        nested[(source.FormKey, level)] = copy.FormKey; return copy.FormKey;
    }
}
