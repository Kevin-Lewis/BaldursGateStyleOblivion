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
        run.Log($"Actor classification settings: {settingsPath}");
        var settings = JsonSerializer.Deserialize<ClassificationSettings>(File.ReadAllText(settingsPath), JsonOptions)
            ?? throw new InvalidDataException("Classification settings are empty.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in settings.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
                throw new InvalidDataException($"Missing or duplicate rule ID: {rule.Id}");
            if (rule.Priority is null || !Enum.IsDefined(rule.Priority.Value) || rule.Priority == RulePriority.ExplicitFormKeyOverride)
                throw new InvalidDataException($"Missing or invalid priority: {rule.Id}");
            if (rule.Evidence != "Always" && string.IsNullOrWhiteSpace(rule.Match))
                throw new InvalidDataException($"Missing match: {rule.Id}");
            if (rule.Evidence is not ("Faction" or "Class" or "Race" or "CreatureType" or "Plugin" or "EditorID" or "Name" or "FormKey" or "RecordType" or "Location" or "Archetype" or "Always"))
                throw new InvalidDataException($"Unknown evidence: {rule.Evidence}");
            new ActorProfile().Apply(rule.Values, rule.Id, "Settings validation", rule.Priority.Value);
        }
        foreach (var values in settings.FormKeyOverrides.Values)
            new ActorProfile().Apply(values, "FormKey override", "Settings validation");

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
            evidence["FormKey"] = [formKey];
            var profile = new ActorProfile();
            foreach (var rule in settings.Rules)
            {
                if (rule.SourcePlugin is not null && !string.Equals(rule.SourcePlugin, plugin, StringComparison.OrdinalIgnoreCase)) continue;
                var matches = rule.Evidence == "Always" || (rule.Evidence == "Plugin"
                    ? string.Equals(rule.Match, plugin, StringComparison.OrdinalIgnoreCase)
                    : evidence.TryGetValue(rule.Evidence, out var signals) && signals.Contains(rule.Match, StringComparer.OrdinalIgnoreCase));
                if (matches) profile.Apply(rule.Values, rule.Id,
                    string.IsNullOrWhiteSpace(rule.Reason) ? $"{rule.Evidence} matched {rule.Match}" : rule.Reason, rule.Priority!.Value);
            }
            var explicitOverride = settings.FormKeyOverrides.FirstOrDefault(pair =>
                string.Equals(pair.Key, formKey, StringComparison.OrdinalIgnoreCase));
            if (explicitOverride.Value is not null)
                profile.Apply(explicitOverride.Value, "FormKey override", $"Explicit override for {formKey}", RulePriority.ExplicitFormKeyOverride);
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
                Profile = Classify(actor.FormKey.ToString(), actor.FormKey.ModKey.ToString(), evidence) });
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
                Profile = Classify(actor.FormKey.ToString(), actor.FormKey.ModKey.ToString(), evidence) });
        }
        if (run.Settings.EnableDiagnostics)
            run.WriteReport(".actor-classifications.json", new { Actors = rows }, JsonOptions);
        run.Log($"Classified {rows.Count} actors.");
        return profiles;
    }
}

