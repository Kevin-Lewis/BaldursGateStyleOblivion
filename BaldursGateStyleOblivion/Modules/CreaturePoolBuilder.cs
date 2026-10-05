using BaldursGateStyleOblivion.Classification;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CreatureLists.Tests")]

namespace BaldursGateStyleOblivion.Modules;

internal sealed class CreaturePoolBuilder
{
    private readonly IReadOnlyDictionary<FormKey, IMajorRecordGetter> records;
    private readonly IReadOnlyDictionary<FormKey, ActorProfile> profiles;
    private readonly CreatureListSettings settings;
    private readonly HashSet<FormKey> included;
    private readonly Dictionary<FormKey, (CreatureListDefinition Definition, string Rule)> decisions;
    private readonly HashSet<FormKey> shared = [];
    private bool privatePools;
    private readonly Dictionary<FormKey, ILeveledCreatureGetter> built = [];
    public OblivionMod PlannedPatch { get; }
    public Dictionary<FormKey, string> Reasons { get; } = [];
    public HashSet<FormKey> Accepted { get; } = [];
    public HashSet<FormKey> ReachableChanges { get; } = [];

    public CreaturePoolBuilder(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, CreatureListSettings settings, ModKey patchKey,
        uint nextFormId, HashSet<FormKey> included)
    {
        this.records = records; this.profiles = profiles; this.settings = settings; this.included = included;
        PlannedPatch = new(patchKey, OblivionRelease.Oblivion);
        PlannedPatch.ModHeader.Stats.NextFormID = nextFormId;
        decisions = records.Values.OfType<ILeveledCreatureGetter>().ToDictionary(list => list.FormKey,
            list => CreatureListConfiguration.Select(settings, list.FormKey.ToString(), list.EditorID, list.FormKey.ModKey.ToString()));
        // Lists used by preserved parents must retain their original meaning. Safe branches get private copies.
        foreach (var list in records.Values.OfType<ILeveledCreatureGetter>())
            if (decisions[list.FormKey].Definition.Policy == CreatureListPolicy.Preserve || Guard(list, decisions[list.FormKey].Definition) is not null)
                MarkDescendants(list.FormKey, shared, []);
    }

    private void MarkDescendants(FormKey key, HashSet<FormKey> found, HashSet<FormKey> path)
    {
        if (!path.Add(key) || records.GetValueOrDefault(key) is not ILeveledCreatureGetter list) return;
        foreach (var entry in list.Entries ?? [])
            if (records.GetValueOrDefault(entry.Reference.FormKey) is ILeveledCreatureGetter)
            {
                found.Add(entry.Reference.FormKey);
                MarkDescendants(entry.Reference.FormKey, found, path);
            }
    }

    // Realm branches change selection locally without overriding their shared source lists.
    public FormKey BuildPrivate(FormKey key)
    {
        privatePools = true;
        var result = Build(key, decisions[key].Definition, []);
        CollectChanges(result.FormKey, []);
        return result.FormKey;
    }

    public void Plan()
    {
        foreach (var list in records.Values.OfType<ILeveledCreatureGetter>().OrderBy(list => list.FormKey.ToString(), StringComparer.Ordinal))
        {
            if (!included.Contains(list.FormKey)) continue;
            var (definition, _) = decisions[list.FormKey];
            var guard = Guard(list, definition);
            if (definition.Policy == CreatureListPolicy.Preserve || guard is not null)
            { Reasons[list.FormKey] = guard ?? definition.Reason; continue; }
            if (shared.Contains(list.FormKey))
            {
                Reasons[list.FormKey] = "Shared with a preserved parent; original retained. Selected parent branches may use a private static copy.";
                continue;
            }
            try
            {
                _ = Build(list.FormKey, definition, []);
                Accepted.Add(list.FormKey);
                CollectChanges(list.FormKey, []);
                Reasons.TryAdd(list.FormKey, definition.Reason);
            }
            catch (InvalidDataException exception) { Reasons[list.FormKey] = exception.Message; }
        }
    }

    private string? Guard(ILeveledCreatureGetter list, CreatureListDefinition definition)
    {
        if (!included.Contains(list.FormKey)) return "Originating plugin is excluded.";
        if (!ScriptReviewed(list.Script.FormKeyNullable)) return "Scripted list requires individual script review, or its reviewed implementation changed.";
        if (definition.AllowSpecial && list.Template.FormKeyNullable is { } template)
        {
            var script = records.GetValueOrDefault(template) switch
            {
                INpcGetter npc => npc.Script.FormKeyNullable,
                ICreatureGetter creature => creature.Script.FormKeyNullable,
                _ => (FormKey?)null
            };
            if (records.GetValueOrDefault(template) is not (INpcGetter or ICreatureGetter))
                return "Missing or invalid encounter template.";
            if (!ScriptReviewed(script)) return "Template script requires individual review, or its reviewed implementation changed.";
        }
        if (list.Flags?.HasFlag(LeveledFlag.UseAll) == true) return "UseAll encounter preserved; weighting could change encounter composition.";
        if (list.Entries is null || list.Entries.Count == 0) return "Empty list preserved.";
        if (!definition.AllowSpecial)
        {
            if (list.Template.FormKeyNullable is not null) return "Template-bearing encounter requires individual review.";
            if (System.Text.RegularExpressions.Regex.IsMatch(list.EditorID ?? "", @"^(?:MQ|MG|FG|TG|DB|DA|SQ|MS|SE\d|ND|Test|Wabbajack|Arena)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "Quest or special encounter preserved.";
        }
        return null;
    }

    private bool ScriptReviewed(FormKey? key) => key is null ||
        (records.GetValueOrDefault(key.Value) is IScriptGetter script &&
         settings.ReviewedScripts.TryGetValue(key.Value.ToString(), out var fingerprint) &&
         string.Equals(fingerprint, ScriptFingerprint(script), StringComparison.OrdinalIgnoreCase));

    internal static string ScriptFingerprint(IScriptGetter script)
    {
        var content = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            SourceCode = script.Fields?.SourceCode,
            Compiled = script.Fields?.CompiledScript is { } compiled ? Convert.ToHexString(compiled.ToArray()) : "",
            References = script.EnumerateFormLinks().Select(link => link.FormKey.ToString()).ToArray()
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
    }

    private ILeveledCreatureGetter Build(FormKey key, CreatureListDefinition inherited, HashSet<FormKey> path)
    {
        if (!path.Add(key)) throw new InvalidDataException($"Nested-list cycle at {key}; encounter preserved.");
        try
        {
            if (records.GetValueOrDefault(key) is not ILeveledCreatureGetter source)
                throw new InvalidDataException($"Missing or invalid nested list {key}.");
            var definition = decisions[key].Definition;
            if (definition.Policy == CreatureListPolicy.Preserve && !settings.FormKeyOverrides.ContainsKey(key.ToString()))
                definition = inherited;
            var guard = Guard(source, definition);
            if (guard is not null) throw new InvalidDataException($"Dependency {key}: {guard}");
            if (definition.Policy == CreatureListPolicy.Preserve)
            {
                if (!IsStatic(key, [])) throw new InvalidDataException($"Dependency {key}: explicitly preserved list is not a verified static actor pool.");
                return source;
            }
            if (built.TryGetValue(key, out var cached)) return cached;
            var entries = definition.Policy == CreatureListPolicy.CuratedPool ? CuratedEntries(definition) : source.Entries!.Select(entry => entry.DeepCopy()).ToList();
            foreach (var entry in entries)
            {
                var reference = entry.Reference.FormKey;
                if (records.GetValueOrDefault(reference) is ILeveledCreatureGetter)
                    entry.Reference.SetTo(Build(reference, new() { Policy = CreatureListPolicy.StaticPool, Reason = "Nested dependency of a selected encounter." }, path).FormKey);
                else if (records.GetValueOrDefault(reference) is not (INpcGetter or ICreatureGetter))
                    throw new InvalidDataException($"Missing or non-actor entry {reference}; encounter preserved.");
                entry.Level = 1;
            }
            if (entries.Count > 255) throw new InvalidDataException("Pool exceeds Oblivion's 255-entry limit.");
            if (definition.Policy == CreatureListPolicy.WeightedPool &&
                (settings.FormKeyOverrides.ContainsKey(key.ToString()) ||
                 CreatureListDeleveling.DependsOnLevel(key, reference => records.GetValueOrDefault(reference) as ILeveledCreatureGetter, [])))
                entries = WeightedEntries(source, entries, definition.Weights ?? settings.Weights);
            var flags = source.Flags.GetValueOrDefault() | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
            // Equal level-one entries are static with either value of CalculateFromAllLevels. Avoid needless overrides.
            if (SameEntries(source.Entries!, entries)) { built[key] = source; return source; }
            LeveledCreature target;
            if (privatePools || shared.Contains(key))
            {
                target = PlannedPatch.LeveledCreatures.AddNew();
                target.DeepCopyIn(source);
                target.EditorID = $"BGSO_Static_{key.ID:X6}_{target.FormKey.ID:X6}";
            }
            else
            {
                target = source.DeepCopy();
                PlannedPatch.LeveledCreatures.Add(target);
            }
            target.Entries!.Clear();
            target.Entries.AddRange(entries);
            target.Flags = flags;
            built[key] = target;
            return target;
        }
        finally { path.Remove(key); }
    }

    private static List<LeveledCreatureEntry> CuratedEntries(CreatureListDefinition definition)
    {
        var entries = new List<LeveledCreatureEntry>();
        foreach (var entry in definition.Entries)
            for (var ticket = 0; ticket < entry.Weight; ticket++)
            {
                var value = new LeveledCreatureEntry { Level = 1, Count = entry.Count };
                value.Reference.SetTo(FormKey.Factory(entry.Reference));
                entries.Add(value);
            }
        return entries;
    }

    private List<LeveledCreatureEntry> WeightedEntries(ILeveledCreatureGetter source, List<LeveledCreatureEntry> entries, PoolWeights weights)
    {
        if (entries.Any(entry => entry.Count.GetValueOrDefault(1) != 1))
        { Reasons[source.FormKey] = "Static pool; native multi-actor counts preserved without reweighting."; return entries; }
        // Pool identity uses name, role and tier, so ten cosmetic variants do not outweigh one different creature.
        var groups = entries.GroupBy(entry => Identity(entry.Reference.FormKey)).OrderBy(group => group.Key.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Tier).ThenBy(group => group.Key.Role, StringComparer.Ordinal).ToArray();
        if (groups.Any(group => group.Key.Tier is null) || groups.Length > 100)
        { Reasons[source.FormKey] = "Static pool; incomplete tier evidence or too many groups for reliable automatic weights."; return entries; }
        if (groups.Length < 2) return entries;
        int Band(int tier) => tier <= weights.CommonMaxTier ? 0 : tier <= weights.StrongMaxTier ? 1 : 2;
        var bands = groups.Select(group => Band(group.Key.Tier!.Value)).ToArray();
        var bandCounts = bands.GroupBy(band => band).ToDictionary(group => group.Key, group => group.Count());
        int[] bandWeights = [weights.Common, weights.Strong, weights.Rare];
        var raw = bands.Select(band => (double)bandWeights[band] / bandCounts[band]).ToArray();
        var tickets = AllocateTickets(raw);
        var result = new List<LeveledCreatureEntry>();
        for (var index = 0; index < groups.Length; index++)
        {
            var variants = groups[index].ToArray();
            var reference = variants[0].Reference.FormKey;
            if (variants.Length > 1)
            {
                var variantList = new LeveledCreature(PlannedPatch.GetNextFormKey(), OblivionRelease.Oblivion)
                { Entries = new(variants.Select(entry => entry.DeepCopy())) };
                PlannedPatch.LeveledCreatures.Add(variantList);
                variantList.EditorID = $"BGSO_Variants_{source.FormKey.ID:X6}_{variantList.FormKey.ID:X6}";
                variantList.Flags = source.Flags.GetValueOrDefault() | LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
                reference = variantList.FormKey;
            }
            for (var ticket = 0; ticket < tickets[index]; ticket++)
            {
                var entry = variants[0].DeepCopy(); entry.Reference.SetTo(reference); entry.Count = 1;
                result.Add(entry);
            }
        }
        return result;
    }

    internal static int[] AllocateTickets(double[] weights)
    {
        var exact = weights.Select(weight => weight / weights.Sum() * 100).ToArray();
        var tickets = exact.Select(value => Math.Max(1, (int)Math.Floor(value))).ToArray();
        while (tickets.Sum() > 100)
        {
            var index = Enumerable.Range(0, weights.Length).Where(index => tickets[index] > 1)
                .OrderByDescending(index => tickets[index] - exact[index]).ThenBy(index => index).First();
            tickets[index]--;
        }
        while (tickets.Sum() < 100)
        {
            var index = Enumerable.Range(0, weights.Length).OrderByDescending(index => exact[index] - tickets[index]).ThenBy(index => index).First();
            tickets[index]++;
        }
        return tickets;
    }

    private (string Name, int? Tier, string Role) Identity(FormKey key)
    {
        if (records.GetValueOrDefault(key) is INpcGetter npc)
            return (npc.Name ?? npc.EditorID ?? key.ToString(), Tier(key), profiles.GetValueOrDefault(key)?.Dimensions.GetValueOrDefault("CombatRole")?.Selected.Value.ToString() ?? "");
        if (records.GetValueOrDefault(key) is ICreatureGetter creature)
            return (creature.Name ?? creature.EditorID ?? key.ToString(), Tier(key), "");
        if (PlannedPatch.LeveledCreatures.TryGetValue(key, out var generated))
        {
            var tiers = Leaves(generated.FormKey, []).Select(Tier).ToArray();
            return (generated.EditorID ?? key.ToString(), tiers.Length > 0 && tiers.All(tier => tier is not null) ? tiers.Max() : null, "Nested pool");
        }
        if (records.GetValueOrDefault(key) is ILeveledCreatureGetter list)
        {
            var tiers = Leaves(key, []).Select(Tier).ToArray();
            return (list.EditorID ?? key.ToString(), tiers.Length > 0 && tiers.All(tier => tier is not null) ? tiers.Max() : null, "Nested pool");
        }
        return (key.ToString(), null, "");
    }

    private int? Tier(FormKey key) => profiles.GetValueOrDefault(key)?.Dimensions.GetValueOrDefault("PowerTier")?.Selected.Value is int tier ? tier : null;

    private IEnumerable<FormKey> Leaves(FormKey key, HashSet<FormKey> path)
    {
        if (!path.Add(key)) yield break;
        var list = GetList(key);
        if (list is null) yield return key;
        else foreach (var entry in list.Entries ?? [])
            foreach (var leaf in Leaves(entry.Reference.FormKey, path)) yield return leaf;
        path.Remove(key);
    }

    public ILeveledCreatureGetter? GetList(FormKey key) => PlannedPatch.LeveledCreatures.TryGetValue(key, out var planned)
        ? planned : records.GetValueOrDefault(key) as ILeveledCreatureGetter;

    private bool IsStatic(FormKey key, HashSet<FormKey> path)
    {
        if (!path.Add(key)) return false;
        if (records.GetValueOrDefault(key) is not ILeveledCreatureGetter list)
        { path.Remove(key); return records.GetValueOrDefault(key) is INpcGetter or ICreatureGetter; }
        var result = list.Script.FormKeyNullable is null && list.Flags?.HasFlag(LeveledFlag.UseAll) != true
            && list.Entries is not null && list.Entries.All(entry => entry.Level <= 1 && records.ContainsKey(entry.Reference.FormKey)
            && IsStatic(entry.Reference.FormKey, path));
        path.Remove(key);
        return result;
    }

    private void CollectChanges(FormKey key, HashSet<FormKey> visited)
    {
        if (!visited.Add(key) || GetList(key) is not { } list) return;
        if (PlannedPatch.LeveledCreatures.ContainsKey(key)) ReachableChanges.Add(key);
        foreach (var entry in list.Entries ?? []) CollectChanges(entry.Reference.FormKey, visited);
    }

    internal static bool SameEntries(IEnumerable<ILeveledCreatureEntryGetter> original, IEnumerable<ILeveledCreatureEntryGetter> target) =>
        original.Select(entry => (entry.Level, entry.Reference.FormKey, entry.Count, entry.Unknown, entry.Unknown2))
            .SequenceEqual(target.Select(entry => (entry.Level, entry.Reference.FormKey, entry.Count, entry.Unknown, entry.Unknown2)));
}

