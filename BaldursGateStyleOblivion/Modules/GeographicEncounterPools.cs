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
    private readonly Dictionary<(FormKey Key, int Min, int Max, int Cap, bool Boss), FormKey> built = [];
    private readonly HashSet<FormKey> validated = [];
    private readonly HashSet<(FormKey Key, int Min, int Max, int Cap, bool Boss)> validatedWeights = [];
    private readonly Dictionary<(FormKey Key, int Min, int Max, int Cap, bool Boss), int> weightsByRange = [];
    private readonly Dictionary<(FormKey Key, int Min, int Cap, double Chance), FormKey> specialPools = [];
    private FormKey? leyawiinPool;
    private readonly Dictionary<(FormKey Key, string Realm), FormKey> realmPools = [];
    public OblivionMod Patch { get; }

    public GeographicEncounterPools(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, Func<FormKey, bool> included,
        HashSet<FormKey> curated, ModKey patchKey, uint nextId)
    {
        this.records = records; this.profiles = profiles; this.included = included; this.curated = curated;
        Patch = new(patchKey, OblivionRelease.Oblivion);
        Patch.ModHeader.Stats.NextFormID = nextId;
    }

    // Only the two admission-quest spawns change; the shared mythic pool stays intact.
    public bool TryBuildLeyawiinPool(FormKey cell, IPlacedObjectGetter placed, out FormKey target)
    {
        target = placed.Base.FormKey;
        if (cell != FormKey.Factory("03379A:Oblivion.esm") ||
            (placed.FormKey != FormKey.Factory("03CD6C:Oblivion.esm") && placed.FormKey != FormKey.Factory("03EE88:Oblivion.esm")) ||
            target != FormKey.Factory("0340B1:Oblivion.esm")) return false;
        if (leyawiinPool is { } cached) { target = cached; return true; }
        if (!included(target) || curated.Contains(target))
            throw new InvalidDataException("Leyawiin quest exception preserves excluded or manually configured pools.");
        var source = (ILeveledCreatureGetter)records[target];
        if (source.Script.FormKeyNullable is not null || source.Template.FormKeyNullable is not null ||
            source.Flags?.HasFlag(LeveledFlag.UseAll) == true)
            throw new InvalidDataException("Leyawiin quest exception preserves scripted, template or UseAll pools.");
        var imp = FormKey.Factory("01E649:Oblivion.esm");
        var troll = FormKey.Factory("002DBC:Oblivion.esm");
        var entries = source.Entries?.Where(entry => entry.Reference.FormKey == imp || entry.Reference.FormKey == troll).ToArray() ?? [];
        if (!entries.Any(entry => entry.Reference.FormKey == imp) || !entries.Any(entry => entry.Reference.FormKey == troll) ||
            entries.Any(entry => entry.Count != 1))
            throw new InvalidDataException("Leyawiin quest exception requires the original count-one imp and troll entries.");
        Validate(imp, []); Validate(troll, []);
        var pool = Patch.LeveledCreatures.AddNew(); pool.DeepCopyIn(source);
        pool.EditorID = "BGSO_LeyawiinRecommendation";
        pool.Entries!.Clear();
        // Nine imp entries and one troll entry: 90% / 10%, independent of player level.
        foreach (var (key, weight) in new[] { (imp, 9), (troll, 1) })
            for (var index = 0; index < weight; index++)
            {
                var entry = entries.First(entry => entry.Reference.FormKey == key).DeepCopy();
                entry.Level = 1; pool.Entries.Add(entry);
            }
        leyawiinPool = target = pool.FormKey;
        return true;
    }

    public FormKey BuildRealm(FormKey key, RealmEncounter realm, CreatureListSettings settings)
    {
        if (realmPools.TryGetValue((key, realm.Name), out var cached)) return cached;
        void CheckBranch(FormKey reference, HashSet<FormKey> path)
        {
            if (!included(reference) || curated.Contains(reference)) throw new InvalidDataException("Realm dependency is excluded or manually curated.");
            if (!path.Add(reference)) throw new InvalidDataException("Cyclic realm pool preserved.");
            if (records.GetValueOrDefault(reference) is ILeveledCreatureGetter list)
            {
                if (list.Entries is null || list.Entries.Count == 0 || list.Entries.Any(entry => entry.Level > 1))
                    throw new InvalidDataException("Realm pool requires the static Phase 4 plan.");
                foreach (var entry in list.Entries) CheckBranch(entry.Reference.FormKey, path);
            }
            else if (records.GetValueOrDefault(reference) is not (INpcGetter or ICreatureGetter))
                throw new InvalidDataException("Missing realm dependency.");
            path.Remove(reference);
        }
        CheckBranch(key, []);
        var lists = records.Values.OfType<ILeveledCreatureGetter>().ToArray();
        var local = new CreatureListSettings { ReviewedScripts = settings.ReviewedScripts };
        foreach (var list in lists)
            local.FormKeyOverrides[list.FormKey.ToString()] = new()
            { Policy = CreatureListPolicy.WeightedPool, AllowSpecial = true, Weights = realm.Weights, Reason = realm.Name };
        var builder = new CreaturePoolBuilder(records, profiles, local, Patch.ModKey, Patch.ModHeader.Stats.NextFormID,
            lists.Where(list => included(list.FormKey)).Select(list => list.FormKey).ToHashSet());
        var target = builder.BuildPrivate(key);
        foreach (var change in builder.ReachableChanges.OrderBy(value => value.ToString(), StringComparer.Ordinal))
        {
            var copy = builder.PlannedPatch.LeveledCreatures[change];
            copy.EditorID = $"BGSO_Realm_{change.ID:X6}";
            Patch.LeveledCreatures.Add(copy);
        }
        Patch.ModHeader.Stats.NextFormID = builder.PlannedPatch.ModHeader.Stats.NextFormID;
        realmPools[(key, realm.Name)] = target;
        return target;
    }

    public FormKey Build(FormKey key, int min, int max, int cap = -1, bool boss = false)
    {
        // Validate the complete branch before allocating records or caching a partial result.
        Validate(key, []);
        ValidateWeights(key, min, max, cap, boss);
        return CopyPool(key, min, max, cap, boss);
    }

    public FormKey BuildSpecial(FormKey key, int min, int cap, double chance)
    {
        if (specialPools.TryGetValue((key, min, cap, chance), out var cached)) return cached;
        Validate(key, []);
        var source = (ILeveledCreatureGetter)records[key];
        var leaves = new List<(ILeveledCreatureEntryGetter Entry, double Probability)>();
        void Flatten(FormKey reference, double probability, bool root)
        {
            var list = (ILeveledCreatureGetter)records[reference];
            if ((!root && list.ChanceNone?.Value > 0) || list.Entries!.Any(entry => entry.Count != 1))
                throw new InvalidDataException("Explicit special chance requires count-one pools without nested no-spawn chances.");
            foreach (var entry in list.Entries)
                if (records[entry.Reference.FormKey] is ILeveledCreatureGetter) Flatten(entry.Reference.FormKey, probability / list.Entries.Count, false);
                else leaves.Add((entry, probability / list.Entries.Count));
        }
        Flatten(key, 1, true);
        bool Rare(ILeveledCreatureEntryGetter entry) => profiles.GetValueOrDefault(entry.Reference.FormKey)?.Tier?.Value > cap;
        var common = leaves.Where(value => !Rare(value.Entry)).ToArray();
        var rare = leaves.Where(value => Rare(value.Entry)).ToArray();
        if (common.Length == 0 || rare.Length == 0) return Build(key, min, cap, cap);
        List<LeveledCreatureEntry> Entries((ILeveledCreatureEntryGetter Entry, double Probability)[] values, bool tapered)
        {
            var grouped = values.GroupBy(value => (value.Entry.Reference.FormKey, value.Entry.Unknown, value.Entry.Unknown2))
                .Select(group => (Entry: group.First().Entry, Probability: group.Sum(value => value.Probability) *
                    (tapered ? Weight(group.First().Entry.Reference.FormKey, min, cap, cap, false) : 1))).ToArray();
            var total = grouped.Sum(value => value.Probability);
            var entries = new List<LeveledCreatureEntry>();
            foreach (var value in grouped)
                for (var index = 0; index < Math.Max(1, (int)Math.Round(100 * value.Probability / total)); index++) entries.Add(value.Entry.DeepCopy());
            if (entries.Count > 255) throw new InvalidDataException("Explicit special pool exceeds 255 entries.");
            return entries;
        }
        // Prepare both complete pools before allocation, so a guarded branch leaves no orphan records.
        var commonEntries = Entries(common, true); var rareEntries = Entries(rare, false);
        var regular = Patch.LeveledCreatures.AddNew(); regular.DeepCopyIn(source);
        regular.EditorID = $"BGSO_DungeonCommon_{regular.FormKey.ID:X6}"; regular.ChanceNone = new Noggog.Percent(0);
        regular.Entries!.Clear(); regular.Entries.AddRange(commonEntries);
        var rareTickets = (int)Math.Round(chance * 100);
        if (rareTickets == 0)
        { regular.ChanceNone = source.ChanceNone; specialPools[(key, min, cap, chance)] = regular.FormKey; return regular.FormKey; }
        var special = Patch.LeveledCreatures.AddNew(); special.DeepCopyIn(source);
        special.EditorID = $"BGSO_DungeonRare_{special.FormKey.ID:X6}"; special.ChanceNone = new Noggog.Percent(0);
        special.Entries!.Clear(); special.Entries.AddRange(rareEntries);
        var target = Patch.LeveledCreatures.AddNew(); target.DeepCopyIn(source);
        target.EditorID = $"BGSO_DungeonSpecial_{target.FormKey.ID:X6}"; target.Entries!.Clear();
        for (var ticket = 0; ticket < 100; ticket++)
        { var entry = new LeveledCreatureEntry { Level = 1, Count = 1 }; entry.Reference.SetTo(ticket < rareTickets ? special.FormKey : regular.FormKey); target.Entries.Add(entry); }
        specialPools[(key, min, cap, chance)] = target.FormKey;
        return target.FormKey;
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

    private void ValidateWeights(FormKey key, int min, int max, int cap, bool boss)
    {
        if (validatedWeights.Contains((key, min, max, cap, boss)) || records.GetValueOrDefault(key) is not ILeveledCreatureGetter list) return;
        var weights = list.Entries!.Select(entry => Weight(entry.Reference.FormKey, min, max, cap, boss)).ToArray();
        var count = weights.All(weight => weight == weights[0]) ? weights.Length : weights.Sum();
        if (count > 255 && (cap < 0 || list.Entries.Any(entry => entry.Count != 1))) throw new InvalidDataException("Weighting would exceed the 255-entry limit.");
        foreach (var entry in list.Entries) ValidateWeights(entry.Reference.FormKey, min, max, cap, boss);
        validatedWeights.Add((key, min, max, cap, boss));
    }

    private FormKey CopyPool(FormKey key, int min, int max, int cap, bool boss)
    {
        if (records.GetValueOrDefault(key) is not ILeveledCreatureGetter source) return key;
        if (built.TryGetValue((key, min, max, cap, boss), out var cached)) return cached;
        var weights = source.Entries!.Select(entry => Weight(entry.Reference.FormKey, min, max, cap, boss)).ToArray();
        // Uniform weighting cannot change selection probabilities. Keep native multiplicities.
        if (weights.All(weight => weight == weights[0])) Array.Fill(weights, 1);
        var entries = new List<LeveledCreatureEntry>();
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var original = source.Entries[index];
            var reference = CopyPool(original.Reference.FormKey, min, max, cap, boss);
            for (var repeat = 0; repeat < weights[index]; repeat++)
            {
                var entry = original.DeepCopy(); entry.Reference.SetTo(reference); entries.Add(entry);
            }
        }
        if (CreaturePoolBuilder.SameEntries(source.Entries, entries))
        { built[(key, min, max, cap, boss)] = key; return key; }
        var target = Patch.LeveledCreatures.AddNew(); target.DeepCopyIn(source);
        target.EditorID = $"BGSO_Geo_{key.ID:X6}_{min}_{max}_{cap}_{boss}_{target.FormKey.ID:X6}";
        if (entries.Count > 255)
        {
            // For count-one pools, group equal-weight entries without multiplying the root past 255.
            var groups = source.Entries.Select((entry, index) => (Entry: entry, Weight: weights[index]))
                .GroupBy(value => value.Weight).OrderBy(group => group.Key).ToArray();
            var total = groups.Sum(group => group.Count() * group.Key);
            entries.Clear();
            foreach (var group in groups)
            {
                var child = Patch.LeveledCreatures.AddNew(); child.DeepCopyIn(source);
                child.EditorID = $"BGSO_DungeonBand_{child.FormKey.ID:X6}";
                child.ChanceNone = new Noggog.Percent(0);
                child.Entries!.Clear();
                foreach (var value in group)
                { var entry = value.Entry.DeepCopy(); entry.Reference.SetTo(CopyPool(entry.Reference.FormKey, min, max, cap, boss)); child.Entries.Add(entry); }
                var tickets = Math.Max(1, (int)Math.Round(100.0 * group.Count() * group.Key / total));
                for (var ticket = 0; ticket < tickets; ticket++)
                { var entry = new LeveledCreatureEntry { Level = 1, Count = 1 }; entry.Reference.SetTo(child.FormKey); entries.Add(entry); }
            }
        }
        target.Entries!.Clear(); target.Entries.AddRange(entries);
        built[(key, min, max, cap, boss)] = target.FormKey;
        return target.FormKey;
    }

    private int Weight(FormKey key, int min, int max, int cap, bool boss)
    {
        if (weightsByRange.TryGetValue((key, min, max, cap, boss), out var cached)) return cached;
        if (records.GetValueOrDefault(key) is not ILeveledCreatureGetter list)
        {
            var tier = profiles.GetValueOrDefault(key)?.Tier?.Value;
            if (tier is null || tier < min || tier > max) return 1;
            if (cap < 0 || boss) return 3;
            return tier < cap ? 4 : 2;
        }
        // A mixed branch receives a proportionate preference; its internal pool is weighted separately.
        var weights = list.Entries!.Select(entry => Weight(entry.Reference.FormKey, min, max, cap, boss)).ToArray();
        var weight = (int)Math.Round(weights.Average(), MidpointRounding.AwayFromZero);
        weightsByRange[(key, min, max, cap, boss)] = weight;
        return weight;
    }
}
