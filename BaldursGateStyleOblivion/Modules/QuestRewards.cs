using BaldursGateStyleOblivion.Core;
using BaldursGateStyleOblivion.Discovery;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Modules;

internal static class QuestRewards
{
    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run)
    {
        var path = RewardRecords.PathFor(run); var settings = RewardConfiguration.Load(path);
        var records = new Dictionary<FormKey, IMajorRecordGetter>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(l => l.Enabled && l.Mod is not null && run.IsInputPlugin(l.ModKey)))
            foreach (var record in listing.Mod!.EnumerateMajorRecords()) records.TryAdd(record.FormKey, record);
        records = records.Where(p => !p.Value.IsDeleted).ToDictionary();
        var changes = new Dictionary<FormKey, string[]>();
        var rows = new List<object>(); var scripts = new List<object>();
        var pools = new RewardPoolBuilder(records, state.PatchMod.ModKey, state.PatchMod.ModHeader.Stats.NextFormID, run.Includes);
        foreach (var pair in settings.Lists.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var key = FormKey.Factory(pair.Key); var choice = pair.Value;
            var status = "Preserved"; var reason = choice.Reason; LeveledItem? planned = null;
            try
            {
                if (!run.Includes(key.ModKey)) throw new InvalidDataException("Source plugin excluded.");
                if (records.GetValueOrDefault(key) is not ILeveledItemGetter source) throw new InvalidDataException("Reward list not loaded.");
                if (!choice.Preserve)
                {
                    planned = pools.Build(source, choice); status = run.Settings.ReportOnly ? "Planned" : "Modified";
                    if (!run.Settings.ReportOnly) { state.PatchMod.LeveledItems.Set(planned); changes[key] = ["Entries", "Flags"]; }
                }
            }
            catch (InvalidDataException error) { reason = error.Message; status = "Retained"; }
            rows.Add(new { FormKey = key.ToString(), choice.Name, choice.SelectionLevel, choice.Variant, choice.Count, Status = status, Reason = reason,
                Original = records.GetValueOrDefault(key) is ILeveledItemGetter list ? Entries(list) : null,
                Planned = planned is null ? null : Entries(planned) });
        }
        var contexts = ScriptContexts(records.Values).ToArray();
        foreach (var context in contexts.OrderBy(c => c.Owner.FormKey.ToString(), StringComparer.Ordinal).ThenBy(c => c.Context))
        {
            var grants = ScriptDiscovery.Scan(context.Fields.SourceCode).Where(s => s.Kind == "GrantCandidate").ToArray();
            var reads = ScriptDiscovery.Scan(context.Fields.SourceCode).Count(s => s.Kind == "PlayerLevelRead");
            var choice = settings.Scripts.FirstOrDefault(s => s.Record.Equals(context.Owner.FormKey.ToString(), StringComparison.OrdinalIgnoreCase) && s.Context == context.Context);
            if (grants.Length == 0 && choice is null) continue;
            var status = "Audited"; var reason = reads > 0 ? "Player-level read present; no reviewed reward-only edit. Quest-entry/encounter checks may be unrelated to reward selection." : "No direct player-level read; linked lists are audited separately.";
            if (choice is not null)
            {
                reason = choice.Reason;
                try
                {
                    if (!run.Includes(context.Owner.FormKey.ModKey)) throw new InvalidDataException("Script source plugin excluded.");
                    if (choice.Preserve) status = "Preserved";
                    else
                    {
                        var fixedScript = RewardScriptEditor.FixLevel(context.Fields, choice.SelectionLevel, choice.Fingerprint);
                        if (!run.Settings.ReportOnly)
                        {
                            var target = context.Owner switch
                            {
                                IScriptGetter script => state.PatchMod.Scripts.GetOrAddAsOverride(script).Fields,
                                IQuestGetter quest => state.PatchMod.Quests.GetOrAddAsOverride(quest).Stages[context.StageIndex].LogEntries[context.EntryIndex].ResultScript,
                                _ => throw new InvalidDataException("Unsupported script owner.")
                            };
                            if (target is null) throw new InvalidDataException("Script fields unavailable.");
                            target.CompiledScript = fixedScript.Bytes; target.SourceCode = fixedScript.Source;
                            changes[context.Owner.FormKey] = changes.GetValueOrDefault(context.Owner.FormKey, []).Append(context.Context + ".PlayerLevelRead").Distinct().ToArray();
                        }
                        status = run.Settings.ReportOnly ? "Planned" : "Modified";
                    }
                }
                catch (InvalidDataException error) { status = "Retained"; reason = error.Message; }
            }
            scripts.Add(new { Record = context.Owner.FormKey.ToString(), context.Owner.EditorID, context.Context, Status = status, Reason = reason,
                SelectionLevel = choice?.SelectionLevel, PlayerLevelReads = reads, Grants = grants,
                References = context.Fields.EnumerateFormLinks().Select(l => l.FormKey).Distinct().Where(records.ContainsKey).Select(key => new
                { FormKey = key.ToString(), records[key].EditorID, Kind = records[key] is ILeveledItemGetter ? "Leveled reward/list" : records[key] is ISpellGetter ? "Spell" : records[key].GetType().Name,
                    ProtectedArtifact = settings.Artifacts.ContainsKey(key.ToString()) }).ToArray() });
        }
        if (!run.Settings.ReportOnly)
        {
            foreach (var list in pools.Patch.LeveledItems) { state.PatchMod.LeveledItems.Add(list); changes[list.FormKey] = ["NewRecord"]; }
            state.PatchMod.ModHeader.Stats.NextFormID = pools.Patch.ModHeader.Stats.NextFormID;
        }
        var missingScripts = settings.Scripts.Where(s => !contexts.Any(c => c.Owner.FormKey.ToString().Equals(s.Record, StringComparison.OrdinalIgnoreCase) && c.Context == s.Context)).ToArray();
        run.WriteReport(".quest-rewards.json", new { run.Settings.ReportOnly, ConfigurationFile = path, Rewards = rows, ScriptAudit = scripts, MissingScripts = missingScripts,
            Artifacts = settings.Artifacts.Select(pair => new { FormKey = pair.Key, Metadata = pair.Value, Loaded = records.ContainsKey(FormKey.Factory(pair.Key)), Status = "Existing item stats preserved" }),
            Limitations = new[] { "Existing acquired rewards are not replaced. Test newly completed quests or fresh/reset reward containers.",
                "Only fingerprint-matched curated script expressions are changed; quest-entry requirements and encounter-selection scripts remain intact.",
                "Artifact metadata protects equipment and loot redistribution; no artifact damage, armor, enchantment or value formulas are applied." } }, RewardConfiguration.Options);
        run.Log($"Quest rewards: {rows.Count} configured families, {settings.Scripts.Count} reviewed script contexts, {settings.Artifacts.Count} protected unique/artifact records.");
        return changes;
    }
    private static object[] Entries(ILeveledItemGetter list) => list.Entries.Select(e => (object)new { e.Level, Reference = e.Reference.FormKey.ToString(), e.Count }).ToArray();
    internal sealed record ScriptContext(IMajorRecordGetter Owner, string Context, IScriptFieldsGetter Fields, int StageIndex = -1, int EntryIndex = -1);
    internal static IEnumerable<ScriptContext> ScriptContexts(IEnumerable<IMajorRecordGetter> records)
    {
        foreach (var record in records)
        {
            if (record is IScriptGetter script && script.Fields is {} fields) yield return new(record, "Standalone", fields);
            if (record is IDialogItemGetter dialog && dialog.Script is {} result) yield return new(record, "Dialogue result", result);
            if (record is not IQuestGetter quest) continue;
            for (var stage = 0; stage < quest.Stages.Count; stage++)
                for (var entry = 0; entry < quest.Stages[stage].LogEntries.Count; entry++)
                    if (quest.Stages[stage].LogEntries[entry].ResultScript is {} code)
                        yield return new(record, $"Stage {quest.Stages[stage].Stage}, entry {entry}", code, stage, entry);
        }
    }
}


