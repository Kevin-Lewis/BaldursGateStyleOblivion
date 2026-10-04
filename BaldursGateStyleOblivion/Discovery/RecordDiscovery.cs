using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Discovery;

internal sealed record RecordIdentity(string FormKey, string? EditorID, string RecordType, string SourcePlugin, string WinningOverridePlugin);
internal sealed record DiscoveryRecord(RecordIdentity Record, object Original);
internal sealed record ListEntry(short Level, string Reference, short? Count);

internal static class RecordDiscovery
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static void Write(IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run)
    {
        // Keep the first record in priority order, including deletion tombstones.
        var winners = new Dictionary<FormKey, (IMajorRecordGetter Record, ModKey Plugin)>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(listing => listing.ModKey != state.PatchMod.ModKey))
            if (listing.Enabled && listing.Mod is not null)
                foreach (var record in listing.Mod.EnumerateMajorRecords())
                    winners.TryAdd(record.FormKey, (record, listing.ModKey));
        var all = winners.Values.Where(value => !value.Record.IsDeleted)
            .OrderBy(value => value.Record.FormKey.ToString(), StringComparer.Ordinal).Select(value => value.Record).ToArray();
        var selected = all.Where(record => run.Includes(record.FormKey.ModKey)).ToArray();
        RecordIdentity Identity(IMajorRecordGetter record) => new(record.FormKey.ToString(), record.EditorID,
            record.GetType().Name.Replace("BinaryOverlay", "", StringComparison.Ordinal),
            record.FormKey.ModKey.ToString(), winners[record.FormKey].Plugin.ToString());
        DiscoveryRecord Row(IMajorRecordGetter record, object original) => new(Identity(record), original);
        var counts = new Dictionary<string, int>();
        void Report(string name, IEnumerable<DiscoveryRecord> records)
        {
            var rows = records.OrderBy(row => row.Record.FormKey, StringComparer.Ordinal).ToArray();
            counts[name] = rows.Length;
            run.WriteReport($".{name}.json", new { Records = rows }, Options);
            run.Log($"Discovery: {rows.Length} {name} records.");
        }

        var watched = all.Where(record => record is INpcGetter or ICreatureGetter or IItemGetter or ISpellGetter
            or IContainerGetter or ILeveledCreatureGetter or IClassGetter or IRaceGetter or IFactionGetter or ICombatStyleGetter or IQuestGetter or IScriptGetter)
            .ToDictionary(record => record.FormKey);
        var usedBy = watched.Keys.ToDictionary(key => key, _ => new List<RecordIdentity>());
        foreach (var record in all)
        {
            // These grouping records expose descendants' links; index descendants themselves instead.
            if (record is ICellGetter or IWorldspaceGetter) continue;
            var links = record is IDialogTopicGetter topic ? topic.Quests.Select(link => link.FormKey)
                : record.EnumerateFormLinks().Select(link => link.FormKey);
            foreach (var key in links.Distinct())
                if (usedBy.TryGetValue(key, out var users)) users.Add(Identity(record));
        }
        Report("reference-index", watched.Values.Where(record => run.Includes(record.FormKey.ModKey))
            .Select(record => Row(record, new
            {
                UsedBy = usedBy[record.FormKey].OrderBy(user => user.FormKey, StringComparer.Ordinal).ToArray()
            })));

        var lists = all.Where(record => record is ILeveledItemGetter or ILeveledCreatureGetter).ToDictionary(record => record.FormKey);
        ListEntry[] Entries(IMajorRecordGetter record) => record switch
        {
            ILeveledItemGetter list => list.Entries?.Select(e => new ListEntry(e.Level, e.Reference.FormKey.ToString(), e.Count)).ToArray() ?? [],
            ILeveledCreatureGetter list => list.Entries?.Select(e => new ListEntry(e.Level, e.Reference.FormKey.ToString(), e.Count)).ToArray() ?? [],
            _ => []
        };
        var graph = lists.ToDictionary(pair => pair.Key, pair => Entries(pair.Value)
            .Select(entry => FormKey.Factory(entry.Reference)).Where(lists.ContainsKey).Distinct()
            .OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray());
        var cycles = FindCycles(graph);
        Report("scaling-audit", selected.Where(record => record is INpcGetter or ICreatureGetter or ILeveledItemGetter or ILeveledCreatureGetter)
            .Select(record => Row(record, record switch
            {
                INpcGetter npc => new
                {
                    LevelMode = npc.Configuration is null ? "Unknown" : npc.Configuration.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset) ? "PlayerLevelOffset" : "FixedLevel",
                    Configuration = ScalarFields(npc.Configuration), Stats = ScalarFields(npc.Stats),
                    AutoCalcStats = npc.Configuration?.Flags.HasFlag(Npc.NpcFlag.AutoCalcStats),
                    CombatStyle = npc.CombatStyle.FormKeyNullable?.ToString()
                },
                ICreatureGetter creature => (object)new
                {
                    LevelMode = creature.Configuration is null ? "Unknown" : creature.Configuration.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset) ? "PlayerLevelOffset" : "FixedLevel",
                    Configuration = ScalarFields(creature.Configuration), Stats = ScalarFields(creature.Data),
                    CombatStyle = creature.CombatStyle.FormKeyNullable?.ToString()
                },
                _ => ListSnapshot(record, Entries(record), graph[record.FormKey])
            })));
        Report("list-dependencies", selected.Where(record => lists.ContainsKey(record.FormKey)).Select(record => Row(record, new
        {
            NestedLists = graph[record.FormKey].Select(key => Identity(lists[key])).ToArray(),
            MissingEntryReferences = Entries(record).Where(entry => !winners.TryGetValue(FormKey.Factory(entry.Reference), out var target) || target.Record.IsDeleted)
                .Select(entry => entry.Reference).Distinct().OrderBy(key => key, StringComparer.Ordinal).ToArray()
        })));

        Report("actor-inventories", selected.Where(record => record is INpcGetter or ICreatureGetter).Select(record => Row(record, record switch
        {
            INpcGetter npc => new { Items = Inventory(npc.Items), Spells = npc.Spells.Select(link => link.FormKey.ToString()).ToArray(),
                DeathItem = npc.DeathItem.FormKeyNullable?.ToString(), Script = npc.Script.FormKeyNullable?.ToString() },
            ICreatureGetter creature => (object)new { Items = Inventory(creature.Items), Spells = creature.Spells.Select(link => link.FormKey.ToString()).ToArray(),
                DeathItem = creature.DeathItem.FormKeyNullable?.ToString(), Script = creature.Script.FormKeyNullable?.ToString() },
            _ => throw new InvalidOperationException()
        })));
        var placedNpcs = all.OfType<IPlacedNpcGetter>().Where(npc => npc.Base.FormKeyNullable.HasValue)
            .GroupBy(npc => npc.Base.FormKeyNullable!.Value).ToDictionary(group => group.Key, group => group.ToArray());
        object MerchantLink(IPlacedNpcGetter placed)
        {
            var referenceKey = placed.MerchantContainer.FormKeyNullable;
            var reference = referenceKey.HasValue && winners.TryGetValue(referenceKey.Value, out var target)
                && !target.Record.IsDeleted ? target.Record as IPlacedObjectGetter : null;
            var baseKey = reference?.Base.FormKeyNullable;
            var container = baseKey.HasValue && winners.TryGetValue(baseKey.Value, out var baseRecord)
                && !baseRecord.Record.IsDeleted ? baseRecord.Record as IContainerGetter : null;
            return new
            {
                PlacedNPC = Identity(placed), MerchantContainerReference = referenceKey?.ToString(),
                ContainerBase = baseKey?.ToString(), LinkResolvedToContainer = container is not null,
                Stock = container is null ? null : Inventory(container.Items)
            };
        }
        Report("merchants", selected.OfType<INpcGetter>().Where(npc => (npc.AIData?.BuySellServices ?? 0) != 0
            || (placedNpcs.TryGetValue(npc.FormKey, out var placements) && placements.Any(p => !p.MerchantContainer.IsNull)))
            .Select(npc => Row(npc, new
            {
                Evidence = "NPC service flags or explicit placed-NPC merchant-container link; service-only actors may be trainers or repairers.",
                Services = npc.AIData?.BuySellServices.ToString(), BarterGold = npc.Configuration?.BarterGold,
                Inventory = Inventory(npc.Items),
                Placements = placedNpcs.GetValueOrDefault(npc.FormKey, []).Select(MerchantLink).ToArray()
            })));

        Report("classes", selected.OfType<IClassGetter>().Select(record => Row(record, new
        {
            record.Name, Data = ScalarFields(record.Data),
            PrimaryAttributes = record.Data?.PrimaryAttributes.ToArray().Select(value => value.ToString()).ToArray(),
            SecondaryAttributes = record.Data?.SecondaryAttributes.ToArray().Select(value => value.ToString()).ToArray(),
            Training = ScalarFields(record.Data?.Training)
        })));
        Report("factions", selected.OfType<IFactionGetter>().Select(record => Row(record, new
        {
            record.Name, Flags = record.Flags?.ToString(), record.CrimeGoldMultiplier,
            Relations = record.Relations.Select(relation => new { Target = relation.Target.FormKey.ToString(), relation.Modifier }).ToArray(),
            Ranks = record.Ranks.Select(rank => new { rank.RankNumber, MaleName = rank.Name?.Male, FemaleName = rank.Name?.Female }).ToArray()
        })));
        Report("combat-styles", selected.OfType<ICombatStyleGetter>().Select(record => Row(record, new
        {
            Data = ScalarFields(record.Data), Advanced = ScalarFields(record.Advanced)
        })));
        Report("quests", selected.OfType<IQuestGetter>().Select(record => Row(record, new
        {
            record.Name, Script = record.Script.FormKeyNullable?.ToString(), Data = ScalarFields(record.Data),
            Conditions = record.Conditions.Select(condition => ScalarFields(condition)).ToArray(),
            Stages = record.Stages.Select(stage => new
            {
                stage.Stage, Entries = stage.LogEntries.Select(entry => new
                {
                    entry.Entry, Flags = entry.Flags?.ToString(), Conditions = entry.Conditions.Select(condition => ScalarFields(condition)).ToArray(),
                    ResultScript = ScriptDiscovery.Inspect(entry.ResultScript)
                }).ToArray()
            }).ToArray(),
            Targets = record.Targets.Select(target => new { Target = target.Data?.Target.FormKey.ToString(),
                Conditions = target.Conditions.Select(condition => ScalarFields(condition)).ToArray() }).ToArray()
        })));
        Report("scripts", selected.OfType<IScriptGetter>().Select(record => Row(record, ScriptDiscovery.Inspect(record.Fields))));
        var candidates = new List<DiscoveryRecord>();
        var sources = 0;
        var missingSources = 0;
        void InspectScript(IMajorRecordGetter owner, string context, IScriptFieldsGetter? script, string? quest = null)
        {
            if (script is null) return;
            if (string.IsNullOrWhiteSpace(script.SourceCode) && (script.CompiledScript?.Length ?? 0) == 0
                && (script.MetadataSummary?.CompiledSize ?? 0) == 0 && !script.EnumerateFormLinks().Any()) return;
            sources++;
            if (string.IsNullOrWhiteSpace(script.SourceCode)) missingSources++;
            var signals = ScriptDiscovery.Scan(script.SourceCode);
            if (signals.Length > 0 || string.IsNullOrWhiteSpace(script.SourceCode))
                candidates.Add(Row(owner, new { Context = context, Quest = quest, Assessment = "CandidateOnly", Script = ScriptDiscovery.Inspect(script) }));
        }
        foreach (var record in selected)
            switch (record)
            {
                case IScriptGetter script: InspectScript(script, "Standalone", script.Fields); break;
                case IQuestGetter quest:
                    foreach (var stage in quest.Stages)
                        for (var i = 0; i < stage.LogEntries.Count; i++)
                            InspectScript(quest, $"Stage {stage.Stage}, entry {i}", stage.LogEntries[i].ResultScript, quest.FormKey.ToString());
                    break;
                case IDialogItemGetter dialogue:
                    InspectScript(dialogue, "Dialogue result", dialogue.Script, dialogue.Quest.FormKeyNullable?.ToString());
                    break;
            }
        Report("script-candidates", candidates);
        run.WriteReport(".discovery-summary.json", new
        {
            RecordCounts = counts, ScannedWinningRecords = all.Length, SelectedWinningRecords = selected.Length,
            ScriptSourcesInspected = sources, ScriptSourcesUnavailable = missingSources,
            ListCycleBackEdges = cycles.Where(edge => run.Includes(edge.From.ModKey) || run.Includes(edge.To.ModKey))
                .Select(edge => new { From = edge.From.ToString(), To = edge.To.ToString() }).ToArray(),
            Limitations = new[] { "Reference index is direct FormKey usage, not script execution or transitive reachability.",
                "Cell/worldspace grouping links are omitted to avoid attributing descendants' references to their parents.",
                "Source-code signals are review candidates. Compiled bytecode and indirect function calls are not analyzed.",
                "Quest/dialogue conditions retain raw parameters; a GetLevel condition alone does not identify the player.",
                "List flag effects, zero caps, respawn behavior, and runtime stats still require focused engine validation." }
        }, Options);
    }

    private static object[] Inventory(IEnumerable<IItemEntryGetter> items) => items
        .Select(item => (object)new { Reference = item.Item.FormKey.ToString(), item.Count }).ToArray();

    private static object[] Inventory(IEnumerable<IContainerItemGetter> items) => items
        .Select(item => (object)new { Reference = item.Item.FormKey.ToString(), item.Count }).ToArray();

    private static object ListSnapshot(IMajorRecordGetter record, ListEntry[] entries, FormKey[] nested)
    {
        var flags = record is ILeveledItemGetter item ? item.Flags : ((ILeveledCreatureGetter)record).Flags;
        var chance = record is ILeveledItemGetter loot ? loot.ChanceNone : ((ILeveledCreatureGetter)record).ChanceNone;
        return new
        {
            Flags = flags?.ToString(), FlagsValue = (int?)flags, ChanceNone = chance?.ToString(),
            CalculateFromAllLevelsLessThanPlayers = flags?.HasFlag(LeveledFlag.CalculateFromAllLevelsLessThanPlayers),
            CalculateForEachItemInCount = flags?.HasFlag(LeveledFlag.CalculateForEachItemInCount),
            UseAll = flags?.HasFlag(LeveledFlag.UseAll), Entries = entries,
            DistinctEntryLevels = entries.Select(entry => entry.Level).Distinct().Order().ToArray(),
            HasEntriesAboveLevelOne = entries.Any(entry => entry.Level > 1), NestedLists = nested.Select(key => key.ToString()).ToArray()
        };
    }

    private static Dictionary<string, object?>? ScalarFields(object? data) => data?.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.GetIndexParameters().Length == 0 &&
            ((Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).IsPrimitive ||
             (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).IsEnum))
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .ToDictionary(property => property.Name, property =>
        {
            var value = property.GetValue(data);
            return value is Enum ? value.ToString() : value;
        }, StringComparer.Ordinal);

    private static (FormKey From, FormKey To)[] FindCycles(Dictionary<FormKey, FormKey[]> graph)
    {
        var visited = new HashSet<FormKey>();
        var active = new HashSet<FormKey>();
        var edges = new List<(FormKey, FormKey)>();
        foreach (var root in graph.Keys.OrderBy(key => key.ToString(), StringComparer.Ordinal))
        {
            var stack = new Stack<(FormKey Key, bool Exit)>();
            stack.Push((root, false));
            while (stack.TryPop(out var step))
            {
                if (step.Exit) { active.Remove(step.Key); continue; }
                if (!visited.Add(step.Key)) continue;
                active.Add(step.Key);
                stack.Push((step.Key, true));
                foreach (var child in graph[step.Key].Reverse())
                    if (active.Contains(child)) edges.Add((step.Key, child));
                    else if (!visited.Contains(child)) stack.Push((child, false));
            }
        }
        return edges.ToArray();
    }
}


