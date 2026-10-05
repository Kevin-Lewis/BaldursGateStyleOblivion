using BaldursGateStyleOblivion.Classification;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Geography.Tests")]

namespace BaldursGateStyleOblivion.Modules;

// Private copies preserve the meaning of shared lists outside this location profile.
internal sealed class GeographicEncounterPools
{
    private readonly IReadOnlyDictionary<FormKey, IMajorRecordGetter> records;
    private readonly IReadOnlyDictionary<FormKey, ActorProfile> profiles;
    private readonly Func<FormKey, bool> included;
    private readonly HashSet<FormKey> curated;
    private readonly Dictionary<(FormKey Key, int Min, int Max), FormKey> built = [];
    private readonly HashSet<FormKey> validated = [];
    private readonly HashSet<(FormKey Key, int Min, int Max)> validatedWeights = [];
    private readonly Dictionary<(FormKey Key, int Min, int Max), int> weightsByRange = [];
    public OblivionMod Patch { get; }

    public GeographicEncounterPools(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, Func<FormKey, bool> included,
        HashSet<FormKey> curated, ModKey patchKey, uint nextId)
    {
        this.records = records; this.profiles = profiles; this.included = included; this.curated = curated;
        Patch = new(patchKey, OblivionRelease.Oblivion);
        Patch.ModHeader.Stats.NextFormID = nextId;
    }

    public FormKey Build(FormKey key, int min, int max)
    {
        // Validate the complete branch before allocating records or caching a partial result.
        Validate(key, []);
        ValidateWeights(key, min, max);
        return CopyPool(key, min, max);
    }

    private void Validate(FormKey key, HashSet<FormKey> path)
    {
        if (validated.Contains(key)) return;
        if (!included(key)) throw new InvalidDataException("An encounter dependency is excluded.");
        if (curated.Contains(key)) throw new InvalidDataException("Manually curated or preserved pool retains its selection weights.");
        if (!path.Add(key)) throw new InvalidDataException("Cyclic encounter pool preserved.");
        try
        {
            if (records.GetValueOrDefault(key) is ILeveledCreatureGetter list)
            {
                if (list.Script.FormKeyNullable is not null || list.Template.FormKeyNullable is not null ||
                    list.Flags?.HasFlag(LeveledFlag.UseAll) == true)
                    throw new InvalidDataException("Script, template or UseAll encounter preserved.");
                if (list.Entries is null || list.Entries.Count == 0 || list.Entries.Any(entry => entry.Level > 1))
                    throw new InvalidDataException("Empty or still level-gated encounter preserved.");
                foreach (var entry in list.Entries) Validate(entry.Reference.FormKey, path);
            }
            else if (records.GetValueOrDefault(key) is INpcGetter npc && npc.Script.FormKeyNullable is not null ||
                records.GetValueOrDefault(key) is ICreatureGetter creature && creature.Script.FormKeyNullable is not null)
                throw new InvalidDataException("Scripted actor encounter preserved.");
            else if (records.GetValueOrDefault(key) is not (INpcGetter or ICreatureGetter))
                throw new InvalidDataException("Missing or unsupported encounter dependency.");
        }
        finally { path.Remove(key); }
        validated.Add(key);
    }

    private void ValidateWeights(FormKey key, int min, int max)
    {
        if (validatedWeights.Contains((key, min, max)) || records.GetValueOrDefault(key) is not ILeveledCreatureGetter list) return;
        var weights = list.Entries!.Select(entry => Weight(entry.Reference.FormKey, min, max)).ToArray();
        var count = weights.All(weight => weight == weights[0]) ? weights.Length : weights.Sum();
        if (count > 255) throw new InvalidDataException("Weighting would exceed the 255-entry limit.");
        foreach (var entry in list.Entries) ValidateWeights(entry.Reference.FormKey, min, max);
        validatedWeights.Add((key, min, max));
    }

    private FormKey CopyPool(FormKey key, int min, int max)
    {
        if (records.GetValueOrDefault(key) is not ILeveledCreatureGetter source) return key;
        if (built.TryGetValue((key, min, max), out var cached)) return cached;
        var weights = source.Entries!.Select(entry => Weight(entry.Reference.FormKey, min, max)).ToArray();
        // Uniform weighting cannot change selection probabilities. Keep native multiplicities.
        if (weights.All(weight => weight == weights[0])) Array.Fill(weights, 1);
        var entries = new List<LeveledCreatureEntry>();
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var original = source.Entries[index];
            var reference = CopyPool(original.Reference.FormKey, min, max);
            for (var repeat = 0; repeat < weights[index]; repeat++)
            {
                var entry = original.DeepCopy(); entry.Reference.SetTo(reference); entries.Add(entry);
            }
        }
        if (CreaturePoolBuilder.SameEntries(source.Entries, entries))
        { built[(key, min, max)] = key; return key; }
        var target = Patch.LeveledCreatures.AddNew(); target.DeepCopyIn(source);
        target.EditorID = $"BGSO_Geo_{key.ID:X6}_{min}_{max}_{target.FormKey.ID:X6}";
        target.Entries!.Clear(); target.Entries.AddRange(entries);
        built[(key, min, max)] = target.FormKey;
        return target.FormKey;
    }

    private int Weight(FormKey key, int min, int max)
    {
        if (weightsByRange.TryGetValue((key, min, max), out var cached)) return cached;
        if (records.GetValueOrDefault(key) is not ILeveledCreatureGetter list)
            return profiles.GetValueOrDefault(key)?.Tier?.Value is { } tier && tier >= min && tier <= max ? 3 : 1;
        // A mixed branch receives a proportionate preference; its internal pool is weighted separately.
        var weights = list.Entries!.Select(entry => Weight(entry.Reference.FormKey, min, max)).ToArray();
        var weight = (int)Math.Round(weights.Average(), MidpointRounding.AwayFromZero);
        weightsByRange[(key, min, max)] = weight;
        return weight;
    }
}
