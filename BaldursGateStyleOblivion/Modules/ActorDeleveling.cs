using System.Text.Json;
using System.Text.Json.Serialization;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;
using BaldursGateStyleOblivion.Discovery;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ActorDeleveling.Tests")]

namespace BaldursGateStyleOblivion.Modules;

internal static class ActorDeleveling
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    internal static bool IsPlayerDependent(bool offset, int minimum, int maximum) =>
        offset && (maximum == 0 || Math.Max(1, minimum) != maximum);

    internal static string[] ChangedFields(int originalLevel, bool offset, int minimum, int maximum, int target,
        bool pinCreatureLevel = false)
    {
        if (!offset && originalLevel == target) return [];
        var pinned = offset && pinCreatureLevel;
        var fields = new List<string>();
        if (originalLevel != (pinned ? 0 : target)) fields.Add("Configuration.LevelOffset");
        if (offset && !pinned) fields.Add("Configuration.Flags.PCLevelOffset");
        if (minimum != (pinned ? target : 0)) fields.Add("Configuration.CalcMin");
        if (maximum != (pinned ? target : 0)) fields.Add("Configuration.CalcMax");
        return fields.ToArray();
    }

    internal static void Apply(Npc actor, int target)
    {
        ActorConfiguration.ValidateFixedLevel(target);
        var config = actor.Configuration ?? throw new InvalidDataException("NPC configuration is missing.");
        config.LevelOffset = (short)target;
        config.Flags &= ~Npc.NpcFlag.PCLevelOffset;
        config.CalcMin = config.CalcMax = 0;
    }

    internal static void Apply(Creature actor, int target)
    {
        ActorConfiguration.ValidateFixedLevel(target);
        var config = actor.Configuration ?? throw new InvalidDataException("Creature configuration is missing.");
        if (config.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset))
        {
            // Equal bounds fix the actual level while retaining native health, damage and skill calculations.
            config.LevelOffset = 0;
            config.CalcMin = config.CalcMax = (ushort)target;
        }
        else
        {
            config.LevelOffset = (short)target;
            config.CalcMin = config.CalcMax = 0;
        }
    }

    internal static ScriptSignal[] ScalingWrites(ScriptSignal[] signals)
    {
        var playerRead = signals.Any(signal => signal.Kind == "PlayerLevelRead");
        string[] powerStats = ["health", "magicka", "fatigue", "strength", "intelligence", "willpower", "agility", "speed", "endurance", "luck",
            "blade", "blunt", "handtohand", "destruction", "alteration", "conjuration", "illusion", "mysticism", "restoration", "marksman", "heavyarmor", "lightarmor", "armorer", "block", "sneak"];
        return signals.Where(signal => signal.Kind == "ActorLevelWrite" || playerRead && signal.Kind == "ActorStatWrite"
            && powerStats.Contains(signal.ActorValue, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    // Vanilla MG04's player-level branch changes equipment, not Caminalda's constant health bonus.
    // Only the exact reviewed source is exempt; changed scripts retain conservative detection.
    internal static bool ReviewedConstantBonus(FormKey owner, string? source) =>
        owner.ToString() == "02D32C:Oblivion.esm" && source is not null &&
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))) ==
        "1E7548EDAE742A4804E6FA49C2790EA2881F16692C55F5BE627B05F5AACE6CBB";

    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, ClassificationSettings settings, PatcherRun run)
    {
        var records = ReadOriginalRecords(state);
        var scriptRisks = ReviewScriptScaling(records, out var scriptRows);
        var changes = DelevelActors(state, profiles, settings, run, scriptRisks);
        WriteScalingPaths(records, scriptRows, run);
        return changes;
    }

    // Exclude this run's output so decisions and diagnostics always use incoming records.
    private static IMajorRecordGetter[] ReadOriginalRecords(IPatcherState<IOblivionMod, IOblivionModGetter> state)
    {
        var winners = new Dictionary<FormKey, IMajorRecordGetter>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(listing => listing.ModKey != state.PatchMod.ModKey))
            if (listing.Enabled && listing.Mod is not null)
                foreach (var record in listing.Mod.EnumerateMajorRecords()) winners.TryAdd(record.FormKey, record);
        return winners.Values.Where(record => !record.IsDeleted).OrderBy(record => record.FormKey.ToString(), StringComparer.Ordinal).ToArray();
    }

    // Source signals are conservative: resolve explicit receivers to actor bases when possible.
    private static Dictionary<FormKey, string> ReviewScriptScaling(IMajorRecordGetter[] records, out List<object> scriptRows)
    {
        var winners = records.ToDictionary(record => record.FormKey);
        var actorKeys = records.Where(record => record is INpcGetter or ICreatureGetter).Select(record => record.FormKey).ToHashSet();
        var placements = records.OfType<IPlacedNpcGetter>().ToDictionary(record => record.FormKey, record => record.Base.FormKey);
        foreach (var record in records.OfType<IPlacedCreatureGetter>()) placements[record.FormKey] = record.Base.FormKey;
        var attached = new Dictionary<FormKey, List<FormKey>>();
        foreach (var record in records)
        {
            var script = record switch { INpcGetter npc => npc.Script.FormKey, ICreatureGetter creature => creature.Script.FormKey, _ => FormKey.Null };
            if (script.IsNull) continue;
            if (!attached.TryGetValue(script, out var actors)) attached[script] = actors = [];
            actors.Add(record.FormKey);
        }
        var byEditorId = records.Where(record => record.EditorID is not null).GroupBy(record => record.EditorID!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().FormKey, StringComparer.OrdinalIgnoreCase);
        var risks = new Dictionary<FormKey, string>();
        var rows = new List<object>();
        void Inspect(IMajorRecordGetter owner, string context, IScriptFieldsGetter? fields)
        {
            if (fields is null) return;
            var signals = ScriptDiscovery.Scan(fields.SourceCode);
            var missingSource = string.IsNullOrWhiteSpace(fields.SourceCode) && ((fields.CompiledScript?.Length ?? 0) > 0 || (fields.MetadataSummary?.CompiledSize ?? 0) > 0);
            var candidates = fields.EnumerateFormLinks().Select(link => placements.GetValueOrDefault(link.FormKey, link.FormKey))
                .Where(actorKeys.Contains).Concat(attached.GetValueOrDefault(owner.FormKey, [])).Distinct().OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
            var writes = ReviewedConstantBonus(owner.FormKey, fields.SourceCode) ? [] : ScalingWrites(signals);
            var affected = writes.SelectMany(signal =>
            {
                if (signal.Receiver is null) return attached.GetValueOrDefault(owner.FormKey, candidates.ToList());
                if (signal.Receiver.Equals("player", StringComparison.OrdinalIgnoreCase)) return new List<FormKey>();
                if (byEditorId.TryGetValue(signal.Receiver, out var target))
                    return new List<FormKey> { placements.GetValueOrDefault(target, target) }.Where(actorKeys.Contains).ToList();
                return candidates.ToList();
            }).Distinct().ToArray();
            if (missingSource) affected = candidates;
            if (writes.Length > 0 || missingSource)
                foreach (var key in affected)
                    risks.TryAdd(key, missingSource ? $"Script {owner.FormKey} has compiled code but no source; scaling requires review."
                        : $"Script {owner.FormKey} contains possible runtime actor scaling; base-level editing is insufficient.");
            if (signals.Any(signal => signal.Kind is "PlayerLevelRead" or "ActorLevelRead" or "ActorLevelWrite") || missingSource)
                rows.Add(new { Record = owner.FormKey.ToString(), owner.EditorID, Context = context, SourceAvailable = !string.IsNullOrWhiteSpace(fields.SourceCode),
                    PossibleRuntimeScaling = writes.Length > 0, AffectedActors = affected.Select(key => key.ToString()).ToArray(), RelatedActors = candidates.Select(key => key.ToString()).ToArray(), Signals = signals });
        }
        foreach (var record in records)
            switch (record)
            {
                case IScriptGetter script: Inspect(script, "Standalone", script.Fields); break;
                case IQuestGetter quest:
                    foreach (var stage in quest.Stages)
                        foreach (var entry in stage.LogEntries) Inspect(quest, $"Quest stage {stage.Stage}", entry.ResultScript);
                    break;
                case IDialogItemGetter dialogue: Inspect(dialogue, "Dialogue result", dialogue.Script); break;
            }
        foreach (var pair in attached)
            if (!winners.TryGetValue(pair.Key, out var record) || record.IsDeleted)
                foreach (var actor in pair.Value) risks.TryAdd(actor, $"Attached script {pair.Key} cannot be resolved.");

        scriptRows = rows;
        return risks;
    }

    // The policy chooses eligibility and target level; this pass plans or writes those changes.
    private static Dictionary<FormKey, string[]> DelevelActors(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, ClassificationSettings settings, PatcherRun run,
        IReadOnlyDictionary<FormKey, string> risks)
    {
        var changes = new Dictionary<FormKey, string[]>();
        var rows = new List<object>();
        var skipped = new Dictionary<string, int>();
        var planned = 0;
        var unchanged = 0;
        var remainingOffsets = 0;
        var plannedRemainingOffsets = 0;
        var remainingDependent = 0;
        var plannedRemainingDependent = 0;
        var fixedBoundActors = 0;

        void Process(IMajorRecordGetter actor, ModKey winner, string? name, string type, int? level, bool offset, int minimum, int maximum, Action<int> apply)
        {
            profiles.TryGetValue(actor.FormKey, out var profile);
            var decision = ActorLevelPolicy.Decide(settings, actor.FormKey.ToString(), actor.EditorID, name,
                type == "Creature", offset, level, profile, risks.GetValueOrDefault(actor.FormKey));
            var fields = decision.TargetLevel is not null && level is not null
                ? ChangedFields(level.Value, offset, minimum, maximum, decision.TargetLevel.Value, pinCreatureLevel: type == "Creature")
                : [];
            var status = decision.TargetLevel switch
            {
                null => "Skipped",
                _ when fields.Length == 0 => "Unchanged",
                _ when run.Settings.ReportOnly => "WouldModify",
                _ => "Modified"
            };
            if (decision.TargetLevel is null) skipped[decision.Reason] = skipped.GetValueOrDefault(decision.Reason) + 1;
            else if (fields.Length == 0) unchanged++;
            else
            {
                planned++;
                if (!run.Settings.ReportOnly)
                {
                    apply(decision.TargetLevel.Value);
                    changes.Add(actor.FormKey, fields);
                }
            }
            var originalDependent = IsPlayerDependent(offset, minimum, maximum);
            var preservesOffset = offset && (type == "Creature" || decision.TargetLevel is null);
            if (offset && (preservesOffset || run.Settings.ReportOnly)) remainingOffsets++;
            if (preservesOffset) plannedRemainingOffsets++;
            if (originalDependent && (decision.TargetLevel is null || run.Settings.ReportOnly)) remainingDependent++;
            if (originalDependent && decision.TargetLevel is null) plannedRemainingDependent++;
            if (preservesOffset && (!originalDependent || decision.TargetLevel is not null && !run.Settings.ReportOnly)) fixedBoundActors++;
            rows.Add(new { FormKey = actor.FormKey.ToString(), actor.EditorID, Name = name, RecordType = type,
                SourcePlugin = actor.FormKey.ModKey.ToString(), WinningOverridePlugin = winner.ToString(),
                Original = new { Level = level, PCLevelOffset = offset, MinimumLevel = minimum, MaximumLevel = maximum },
                PowerTier = decision.Tier, decision.Handling, decision.TargetLevel, Status = status, decision.Rule, decision.Reason,
                LevelMode = decision.TargetLevel is null ? "Preserved" : offset && type == "Creature" ? "FixedBoundsNativeCalculation" : "FixedLevel",
                ClassificationReason = profile?.Dimensions.GetValueOrDefault("PowerTier")?.Selected.Reason,
                PlannedFields = fields, ModifiedFields = changes.GetValueOrDefault(actor.FormKey, []) });
        }
        foreach (var context in state.LoadOrder.PriorityOrder
            .Where(listing => listing.ModKey != state.PatchMod.ModKey)
            .Npc().WinningContextOverrides()
            .Where(context => run.Includes(context.Record.FormKey.ModKey))
            .OrderBy(context => context.Record.FormKey.ToString(), StringComparer.Ordinal))
        {
            var actor = context.Record;
            var config = actor.Configuration;
            Process(actor, context.ModKey, actor.Name, "NPC", config?.LevelOffset, config?.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset) == true,
                config?.CalcMin ?? 0, config?.CalcMax ?? 0, target => Apply(state.PatchMod.Npcs.GetOrAddAsOverride(actor), target));
        }
        foreach (var context in state.LoadOrder.PriorityOrder
            .Where(listing => listing.ModKey != state.PatchMod.ModKey)
            .Creature().WinningContextOverrides()
            .Where(context => run.Includes(context.Record.FormKey.ModKey))
            .OrderBy(context => context.Record.FormKey.ToString(), StringComparer.Ordinal))
        {
            var actor = context.Record;
            var config = actor.Configuration;
            Process(actor, context.ModKey, actor.Name, "Creature", config?.LevelOffset, config?.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset) == true,
                config?.CalcMin ?? 0, config?.CalcMax ?? 0, target => Apply(state.PatchMod.Creatures.GetOrAddAsOverride(actor), target));
        }
        run.WriteReport(".actor-deleveling.json", new { ReportOnly = run.Settings.ReportOnly, LevelMapping = settings.LevelMapping,
            Summary = new { PlannedRecords = planned, ModifiedRecords = changes.Count, UnchangedRecords = unchanged,
                SkippedRecords = skipped.Values.Sum(), RemainingPCLevelOffsetActors = remainingOffsets, PlannedRemainingPCLevelOffsetActors = plannedRemainingOffsets,
                RemainingPlayerDependentActors = remainingDependent, PlannedRemainingPlayerDependentActors = plannedRemainingDependent,
                FixedBoundOffsetActors = fixedBoundActors, SkippedReasons = skipped }, Actors = rows }, Options);
        run.Log($"Actor deleveling: {changes.Count} modified, {planned} planned, {unchanged} unchanged, {skipped.Values.Sum()} skipped; {remainingDependent} player-dependent actors remain; {fixedBoundActors} use fixed bounds and native calculation.");
        return changes;
    }

    // Encounter selection stays unchanged. Report indirect scaling through nested lists.
    private static void WriteScalingPaths(IMajorRecordGetter[] records, List<object> scriptRows, PatcherRun run)
    {
        var leveledLists = records.OfType<ILeveledCreatureGetter>().ToDictionary(list => list.FormKey);
        bool PlayerDependent(ILeveledCreatureGetter list)
        {
            var pending = new Stack<FormKey>();
            var visited = new HashSet<FormKey>();
            pending.Push(list.FormKey);
            while (pending.TryPop(out var key))
            {
                if (!visited.Add(key) || !leveledLists.TryGetValue(key, out var current)) continue;
                foreach (var entry in current.Entries ?? [])
                {
                    if (entry.Level > 1) return true;
                    if (leveledLists.ContainsKey(entry.Reference.FormKey)) pending.Push(entry.Reference.FormKey);
                }
            }
            return false;
        }
        var lists = leveledLists.Values.Where(list => run.Includes(list.FormKey.ModKey)).Select(list => new
        {
            Record = list.FormKey.ToString(), list.EditorID, Flags = list.Flags?.ToString(),
            DependsOnPlayerLevel = PlayerDependent(list),
            Entries = list.Entries?.Select(entry => new { entry.Level, Reference = entry.Reference.FormKey.ToString(), entry.Count }).ToArray(),
            NestedLists = list.Entries?.Where(entry => leveledLists.ContainsKey(entry.Reference.FormKey)).Select(entry => entry.Reference.FormKey.ToString()).Distinct().ToArray()
        }).ToArray();
        run.WriteReport(".actor-scaling-paths.json", new { LeveledCreatureLists = lists, Scripts = scriptRows,
            Limitations = new[] { "Leveled creature list selection is unchanged; nested lists may still depend on player level.",
                "Script signals are conservative review candidates, not proof of runtime behavior. Indirect calls and compiled-only scripts cannot be fully analyzed.",
                "Already-spawned actors and quest behavior require in-game validation." } }, Options);
    }
}
