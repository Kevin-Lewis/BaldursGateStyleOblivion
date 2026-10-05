using System.Text.Json;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Modules;

internal static class CreatureListDeleveling
{
    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, PatcherRun run)
    {
        var settings = LoadSettings(run);
        var winners = ReadRecords(state, run);
        var records = winners.ToDictionary(pair => pair.Key, pair => pair.Value.Record);
        var selected = records.Values.OfType<ILeveledCreatureGetter>().Where(list => run.Includes(list.FormKey.ModKey)).ToArray();
        var builder = new CreaturePoolBuilder(records, profiles, settings, state.PatchMod.ModKey,
            state.PatchMod.ModHeader.Stats.NextFormID, selected.Select(list => list.FormKey).ToHashSet());
        builder.Plan();
        var changes = ApplyPlans(state, run, builder, records);
        WriteReports(run, settings, winners, selected, builder, changes);
        return changes;
    }

    private static CreatureListSettings LoadSettings(PatcherRun run)
    {
        var path = Path.Combine(run.DataDirectory, "creature-lists.json");
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "creature-lists.json");
        if (!string.IsNullOrWhiteSpace(run.Settings.CreatureListConfigurationFile))
            path = Path.GetFullPath(run.Settings.CreatureListConfigurationFile, run.DataDirectory);
        run.Log($"Creature list configuration: {path}");
        return CreatureListConfiguration.Load(path);
    }

    private static Dictionary<FormKey, (IMajorRecordGetter Record, ModKey Plugin)> ReadRecords(
        IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run)
    {
        var winners = new Dictionary<FormKey, (IMajorRecordGetter, ModKey)>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)))
            if (listing.Enabled && listing.Mod is not null)
                foreach (var record in listing.Mod.EnumerateMajorRecords()) winners.TryAdd(record.FormKey, (record, listing.ModKey));
        return winners.Where(pair => !pair.Value.Item1.IsDeleted).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static Dictionary<FormKey, string[]> ApplyPlans(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        PatcherRun run, CreaturePoolBuilder builder, IReadOnlyDictionary<FormKey, IMajorRecordGetter> records)
    {
        var changes = new Dictionary<FormKey, string[]>();
        if (run.Settings.ReportOnly) return changes;
        foreach (var key in builder.ReachableChanges.OrderBy(key => key.ToString(), StringComparer.Ordinal))
        {
            var list = builder.PlannedPatch.LeveledCreatures[key];
            state.PatchMod.LeveledCreatures.Add(list);
            changes[key] = ChangedFields(records.GetValueOrDefault(key) as ILeveledCreatureGetter, list);
        }
        state.PatchMod.ModHeader.Stats.NextFormID = builder.PlannedPatch.ModHeader.Stats.NextFormID;
        return changes;
    }

    internal static string[] ChangedFields(ILeveledCreatureGetter? original, ILeveledCreatureGetter target)
    {
        if (original is null) return ["NewRecord"];
        var fields = new List<string>();
        if (!CreaturePoolBuilder.SameEntries(original.Entries ?? [], target.Entries ?? [])) fields.Add("Entries");
        if (original.Flags != target.Flags) fields.Add("Flags.CalculateFromAllLevelsLessThanPlayers");
        return fields.ToArray();
    }

    private static void WriteReports(PatcherRun run, CreatureListSettings settings,
        Dictionary<FormKey, (IMajorRecordGetter Record, ModKey Plugin)> winners, ILeveledCreatureGetter[] selected,
        CreaturePoolBuilder builder, Dictionary<FormKey, string[]> changes)
    {
        ILeveledCreatureGetter? Original(FormKey key) => winners.GetValueOrDefault(key).Record as ILeveledCreatureGetter;
        ILeveledCreatureGetter? Planned(FormKey key) => builder.ReachableChanges.Contains(key) ? builder.GetList(key) : Original(key);
        var usage = selected.ToDictionary(list => list.FormKey, _ => new List<object>());
        var locations = selected.ToDictionary(list => list.FormKey, _ => new HashSet<string>());
        foreach (var record in winners.Values.Select(value => value.Record))
        {
            if (record is ICellGetter or IWorldspaceGetter) continue;
            foreach (var link in record.EnumerateFormLinks().Select(link => link.FormKey).Distinct())
                if (usage.TryGetValue(link, out var users)) users.Add(new { FormKey = record.FormKey.ToString(), record.EditorID, RecordType = record.GetType().Name.Replace("BinaryOverlay", "") });
        }
        foreach (var cell in winners.Values.Select(value => value.Record).OfType<ICellGetter>())
            foreach (var placed in cell.Persistent.Concat(cell.Temporary).Concat(cell.VisibleWhenDistant))
            {
                if (!winners.TryGetValue(placed.FormKey, out var current)) continue;
                foreach (var link in current.Record.EnumerateFormLinks())
                    if (locations.TryGetValue(link.FormKey, out var cells)) cells.Add($"{cell.FormKey} | {cell.EditorID} | {cell.Name}");
            }
        var rows = selected.OrderBy(list => list.FormKey.ToString(), StringComparer.Ordinal).Select(list =>
        {
            var (definition, rule) = CreatureListConfiguration.Select(settings, list.FormKey.ToString(), list.EditorID, list.FormKey.ModKey.ToString());
            var planned = Planned(list.FormKey)!;
            var plannedFields = ChangedFields(list, planned);
            var accepted = builder.Accepted.Contains(list.FormKey);
            var purpose = Purpose(list.EditorID, definition.Name);
            return new
            {
                FormKey = list.FormKey.ToString(), list.EditorID, SourcePlugin = list.FormKey.ModKey.ToString(),
                WinningOverridePlugin = winners[list.FormKey].Plugin.ToString(), Purpose = purpose,
                Policy = definition.Policy, Rule = rule, Reason = builder.Reasons.GetValueOrDefault(list.FormKey, definition.Reason),
                Status = accepted ? plannedFields.Length == 0 ? "AlreadyStatic" : run.Settings.ReportOnly ? "WouldModify" : "Modified" : "Preserved",
                Original = Snapshot(list), Planned = Snapshot(planned),
                PlannedFields = plannedFields, ModifiedFields = changes.GetValueOrDefault(list.FormKey, []),
                DirectUses = usage[list.FormKey], DirectCells = locations[list.FormKey].Order(StringComparer.Ordinal).ToArray(),
                OriginalDependsOnPlayerLevel = DependsOnLevel(list.FormKey, Original, []),
                PlannedDependsOnPlayerLevel = DependsOnLevel(list.FormKey, Planned, []),
                SelectionChecks = accepted ? new[] { 1, 10, 25, 40 }.Select(level => new
                { PlayerLevel = level, Outcomes = Probabilities(list.FormKey, level, Planned, []) }).ToArray() : null
            };
        }).ToArray();
        var generated = builder.ReachableChanges.Where(key => !winners.ContainsKey(key)).OrderBy(key => key.ToString(), StringComparer.Ordinal)
            .Select(key => new { FormKey = key.ToString(), builder.GetList(key)!.EditorID, Planned = Snapshot(builder.GetList(key)!) }).ToArray();
        var summary = new
        {
            InspectedLists = rows.Length, PlannedOverrides = rows.Count(row => row.PlannedFields.Length > 0),
            ModifiedOverrides = changes.Keys.Count(winners.ContainsKey), GeneratedPools = generated.Length,
            AlreadyStatic = rows.Count(row => row.Status == "AlreadyStatic"), Preserved = rows.Count(row => row.Status == "Preserved"),
            OriginalLevelDependent = rows.Count(row => row.OriginalDependsOnPlayerLevel),
            PlannedLevelDependent = rows.Count(row => row.PlannedDependsOnPlayerLevel),
            RemainingLevelDependent = run.Settings.ReportOnly ? rows.Count(row => row.OriginalDependsOnPlayerLevel) : rows.Count(row => row.PlannedDependsOnPlayerLevel)
        };
        run.WriteReport(".creature-list-deleveling.json", new
        {
            run.Settings.ReportOnly, Summary = summary, Lists = rows, GeneratedPools = generated,
            Limitations = new[] { "Outcome probabilities describe one selection, including nested ChanceNone; native counts and per-count behavior are preserved separately.",
                "Cell associations are observed direct uses, not geographic difficulty assignments. Parent list links are reported separately.",
                "Scripted, template-bearing, quest-specific and unknown families remain guarded unless individually reviewed. Already-spawned encounters require fresh-cell in-game testing." }
        }, CreatureListConfiguration.JsonOptions);
        run.Log($"Creature lists: {summary.ModifiedOverrides} overrides, {summary.GeneratedPools} generated pools; {summary.RemainingLevelDependent} level-dependent lists remain.");
    }

    internal static object Snapshot(ILeveledCreatureGetter list) => new
    {
        Flags = list.Flags?.ToString(), FlagsValue = (int?)list.Flags, ChanceNone = list.ChanceNone?.ToString(),
        Script = list.Script.FormKeyNullable?.ToString(), Template = list.Template.FormKeyNullable?.ToString(),
        Entries = list.Entries?.Select(entry => new { entry.Level, Reference = entry.Reference.FormKey.ToString(), entry.Count, entry.Unknown, entry.Unknown2 }).ToArray()
    };

    private static string Purpose(string? editorId, string group)
    {
        var id = editorId ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(id, @"^(?:MQ|MG|FG|TG|DB|DA|SQ|MS|SE\d|ND)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "QuestSpecific";
        if (id.Contains("Boss", StringComparison.OrdinalIgnoreCase)) return "BossEncounter";
        if (id.Contains("Road", StringComparison.OrdinalIgnoreCase) || id.Contains("Wilderness", StringComparison.OrdinalIgnoreCase) || id.Contains("Lair", StringComparison.OrdinalIgnoreCase)) return "GeographicEncounter";
        if (group.Contains("Bandit", StringComparison.OrdinalIgnoreCase)) return "FactionEncounter";
        return group.Length > 0 ? group : "Other";
    }

    internal static bool DependsOnLevel(FormKey key, Func<FormKey, ILeveledCreatureGetter?> lists, HashSet<FormKey> path)
    {
        if (!path.Add(key)) return true;
        var list = lists(key);
        var dependent = list is not null && (list.Entries ?? []).Any(entry => entry.Level > 1 || DependsOnLevel(entry.Reference.FormKey, lists, path));
        path.Remove(key);
        return dependent;
    }

    internal static Dictionary<string, double>? Probabilities(FormKey key, int level,
        Func<FormKey, ILeveledCreatureGetter?> lists, HashSet<FormKey> path)
    {
        var list = lists(key);
        if (list is null) return new() { [key.ToString()] = 1 };
        if (!path.Add(key) || list.Flags?.HasFlag(LeveledFlag.UseAll) == true) return null;
        try
        {
            var entries = (list.Entries ?? []).Where(entry => entry.Level <= level).ToArray();
            if (entries.Length == 0) return new() { ["None"] = 1 };
            if (list.Flags?.HasFlag(LeveledFlag.CalculateFromAllLevelsLessThanPlayers) != true)
                entries = entries.Where(entry => entry.Level == entries.Max(value => value.Level)).ToArray();
            var chanceNone = list.ChanceNone?.Value ?? 0;
            var result = new Dictionary<string, double> { ["None"] = chanceNone };
            foreach (var entry in entries)
            {
                var nested = Probabilities(entry.Reference.FormKey, level, lists, path);
                if (nested is null) return null;
                foreach (var outcome in nested)
                    result[outcome.Key] = result.GetValueOrDefault(outcome.Key) + (1 - chanceNone) * outcome.Value / entries.Length;
            }
            return result.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        finally { path.Remove(key); }
    }
}
