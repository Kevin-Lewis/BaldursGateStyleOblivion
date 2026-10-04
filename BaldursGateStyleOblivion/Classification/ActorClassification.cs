using BaldursGateStyleOblivion.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Classification;

internal static class ActorClassification
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static Dictionary<FormKey, ActorProfile> Write(IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run)
    {
        var settingsPath = Path.Combine(run.DataDirectory, "actor-classification.json");
        if (!File.Exists(settingsPath)) settingsPath = Path.Combine(AppContext.BaseDirectory, "actor-classification.json");
        if (!string.IsNullOrWhiteSpace(run.Settings.ActorConfigurationFile)) settingsPath = Path.GetFullPath(run.Settings.ActorConfigurationFile, run.DataDirectory);
        run.Log($"Actor classification settings: {settingsPath}");
        var settings = ActorConfiguration.Load(settingsPath);
        var factions = state.LoadOrder.PriorityOrder.Faction().WinningOverrides()
            .ToDictionary(record => record.FormKey, record => record.EditorID);
        var classes = state.LoadOrder.PriorityOrder.Class().WinningOverrides()
            .ToDictionary(record => record.FormKey, record => record.EditorID);
        var races = state.LoadOrder.PriorityOrder.Race().WinningOverrides()
            .ToDictionary(record => record.FormKey, record => record.EditorID);
        var rows = new List<object>();
        var profiles = new Dictionary<FormKey, ActorProfile>();

        ActorProfile Classify(string formKey, string plugin, Dictionary<string, string[]> evidence)
        {
            var profile = ActorConfiguration.Classify(settings, formKey, plugin, evidence);
            profiles.Add(FormKey.Factory(formKey), profile);
            return profile;
        }

        foreach (var context in state.LoadOrder.PriorityOrder.Npc().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey)).OrderBy(c => c.Record.FormKey.ToString(), StringComparer.Ordinal))
        {
            var actor = context.Record;
            var evidence = new Dictionary<string, string[]>
            {
                ["EditorID"] = actor.EditorID is null ? [] : [actor.EditorID],
                ["Name"] = actor.Name is null ? [] : [actor.Name],
                ["RecordType"] = ["NPC"],
                ["Faction"] = actor.Factions.Select(f => factions.GetValueOrDefault(f.Faction.FormKey)).OfType<string>().ToArray(),
                ["Class"] = classes.TryGetValue(actor.Class.FormKey, out var cls) && cls is not null ? [cls] : [],
                ["Race"] = races.TryGetValue(actor.Race.FormKey, out var race) && race is not null ? [race] : []
            };
            rows.Add(new { FormKey = actor.FormKey.ToString(), actor.EditorID, actor.Name, RecordType = "NPC",
                SourcePlugin = actor.FormKey.ModKey.ToString(), WinningOverridePlugin = context.ModKey.ToString(),
                Evidence = evidence, Profile = Classify(actor.FormKey.ToString(), actor.FormKey.ModKey.ToString(), evidence) });
        }
        foreach (var context in state.LoadOrder.PriorityOrder.Creature().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey)).OrderBy(c => c.Record.FormKey.ToString(), StringComparer.Ordinal))
        {
            var actor = context.Record;
            var evidence = new Dictionary<string, string[]>
            {
                ["EditorID"] = actor.EditorID is null ? [] : [actor.EditorID],
                ["Name"] = actor.Name is null ? [] : [actor.Name],
                ["RecordType"] = ["Creature"],
                ["Faction"] = actor.Factions.Select(f => factions.GetValueOrDefault(f.Faction.FormKey)).OfType<string>().ToArray(),
                ["CreatureType"] = actor.Data is null ? [] : [actor.Data.Type.ToString()]
            };
            rows.Add(new { FormKey = actor.FormKey.ToString(), actor.EditorID, actor.Name, RecordType = "Creature",
                SourcePlugin = actor.FormKey.ModKey.ToString(), WinningOverridePlugin = context.ModKey.ToString(),
                Evidence = evidence, Profile = Classify(actor.FormKey.ToString(), actor.FormKey.ModKey.ToString(), evidence) });
        }
        if (run.Settings.EnableDiagnostics)
            run.WriteReport(".actor-classifications.json", new { Actors = rows }, JsonOptions);
        run.Log($"Classified {rows.Count} actors.");
        return profiles;
    }
}

