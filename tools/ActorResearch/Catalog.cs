using System.Text.Json;
using BaldursGateStyleOblivion.Classification;

namespace ActorResearch;

internal sealed record Actor(string FormKey, string? EditorID, string? Name, string RecordType, string SourcePlugin,
    string WinningOverridePlugin, JsonElement Original, Dictionary<string, string[]> Evidence,
    JsonElement? Scaling, JsonElement? Inventory, JsonElement? Usage);

internal static class Catalog
{
    private static JsonElement[] Rows(string directory, string suffix, string property, bool required = false)
    {
        var files = Directory.GetFiles(directory, "*." + suffix + ".json");
        if (files.Length == 0 && !required) return [];
        if (files.Length != 1) throw new InvalidDataException($"Expected one {suffix} report in {directory}; found {files.Length}.");
        using var doc = JsonDocument.Parse(File.ReadAllText(files[0]));
        return doc.RootElement.GetProperty(property).EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    public static Actor[] Read(string directory)
    {
        var profiles = Rows(directory, "actor-classifications", "Actors", true).ToDictionary(row => row.GetProperty("FormKey").GetString()!, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement> Index(string suffix) => Rows(directory, suffix, "Records")
            .ToDictionary(row => row.GetProperty("Record").GetProperty("FormKey").GetString()!, row => row.GetProperty("Original"), StringComparer.OrdinalIgnoreCase);
        var scaling = Index("scaling-audit");
        var inventory = Index("actor-inventories");
        var usage = Index("reference-index");
        return Rows(directory, "npcs", "Records", true).Concat(Rows(directory, "creatures", "Records", true)).Select(row =>
        {
            var key = row.GetProperty("FormKey").GetString()!;
            if (!profiles.TryGetValue(key, out var profile) || !profile.TryGetProperty("Evidence", out var evidence))
                throw new InvalidDataException("Rerun the updated patcher to generate actor evidence before using the catalog.");
            JsonElement? Find(Dictionary<string, JsonElement> index) => index.TryGetValue(key, out var found) ? found : null;
            return new Actor(key, row.GetProperty("EditorID").GetString(), row.GetProperty("Name").GetString(), row.GetProperty("RecordType").GetString()!,
                row.GetProperty("SourcePlugin").GetString()!, row.GetProperty("WinningOverridePlugin").GetString()!, row.GetProperty("Original").Clone(),
                JsonSerializer.Deserialize<Dictionary<string, string[]>>(evidence)!, Find(scaling), Find(inventory), Find(usage));
        }).OrderBy(actor => actor.FormKey, StringComparer.Ordinal).ToArray();
    }

    public static object[] Rows(Actor[] actors, ClassificationSettings settings, string config)
    {
        var rows = actors.Select(actor =>
        {
            var profile = ActorConfiguration.Classify(settings, actor.FormKey, actor.SourcePlugin, actor.Evidence);
            object? Value(string dimension) => profile.Dimensions.TryGetValue(dimension, out var field) ? field.Selected.Value : null;
            settings.FormKeyOverrides.TryGetValue(actor.FormKey, out var manual);
            var notes = manual;
            var assignment = profile.Dimensions.GetValueOrDefault("PowerTier")?.Selected;
            var usedBy = actor.Usage?.GetProperty("UsedBy").EnumerateArray().Where(item => item.GetProperty("RecordType").GetString() is "Quest" or "Script" or "DialogResponses").Select(item => item.GetProperty("EditorID").GetString() ?? item.GetProperty("FormKey").GetString()).ToArray() ?? [];
            return new { actor.FormKey, actor.Name, actor.EditorID, actor.SourcePlugin, actor.WinningOverridePlugin, actor.RecordType,
                Group = Value("ActorCategory")?.ToString() ?? "Unclassified", Tier = profile.Tier?.Value,
                FixedLevel = manual?.FixedLevel, Delevel = manual?.Delevel, DelevelingReview = manual?.DelevelingReview ?? "",
                MappedLevel = manual?.FixedLevel ?? (profile.Tier is null ? null : settings.LevelMapping.GetValueOrDefault(profile.Tier.Value.Value)),
                HasOverride = manual is not null, OverrideTier = manual?.PowerTier, OverrideHandling = manual?.Handling?.ToString(), Handling = Value("Handling")?.ToString() ?? "Unclassified",
                Model = notes?.Model ?? "", Description = notes?.Description ?? "", Reason = notes?.Reason ?? assignment?.Reason ?? "",
                Uncertainty = notes?.Uncertainty ?? "", Sources = notes?.Sources ?? [], Rule = assignment?.Rule ?? "",
                EditFile = manual?.ConfigurationFile ?? (assignment is not null ? settings.Rules.FirstOrDefault(rule => rule.Id == assignment.Rule)?.ConfigurationFile : null) ?? config,
                actor.Original, ReviewSignals = usedBy, Dimensions = profile.Dimensions };
        }).ToArray();
        return rows;
    }

    public static string Html(Actor[] actors, ClassificationSettings settings, string config, string token = "")
    {
        var options = new JsonSerializerOptions(ActorConfiguration.JsonOptions) { WriteIndented = false };
        return EditorNavigation.ReadTemplate("catalog.html")
            .Replace("/*ACTORS*/[]", JsonSerializer.Serialize(Rows(actors, settings, config), options))
            .Replace("/*GROUPS*/[]", JsonSerializer.Serialize(settings.Groups.SelectMany(group => group.Value.Select(rule => new
            { Group = group.Key, rule.Id, rule.Enabled, Tier = rule.Values.PowerTier, Match = rule.Evidence + ": " + rule.Match })), options))
            .Replace("/*TOKEN*/\"\"", JsonSerializer.Serialize(token))
            .Replace("/*CONFIG*/\"\"", JsonSerializer.Serialize(config));
    }

    public static void Write(Actor[] actors, ClassificationSettings settings, string config, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, Html(actors, settings, config));
    }
}
