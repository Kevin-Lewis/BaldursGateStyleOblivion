using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;

namespace BaldursGateStyleOblivion.Modules;

public enum CreatureListPolicy { Preserve, StaticPool, WeightedPool, CuratedPool }

public sealed class CreatureListSettings
{
    public CreatureListPolicy Fallback { get; set; } = CreatureListPolicy.Preserve;
    public Dictionary<string, string> ReviewedScripts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PoolWeights Weights { get; set; } = new();
    public List<CreatureListRule> Groups { get; set; } = [];
    public List<CreatureListRule> PluginRules { get; set; } = [];
    public Dictionary<string, CreatureListDefinition> FormKeyOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PoolWeights
{
    public int CommonMaxTier { get; set; } = 3;
    public int StrongMaxTier { get; set; } = 5;
    public int Common { get; set; } = 70;
    public int Strong { get; set; } = 25;
    public int Rare { get; set; } = 5;
}

public class CreatureListDefinition
{
    public string Name { get; set; } = "";
    public CreatureListPolicy Policy { get; set; } = CreatureListPolicy.Preserve;
    public string Reason { get; set; } = "";
    public bool AllowSpecial { get; set; }
    public PoolWeights? Weights { get; set; }
    public List<PoolEntry> Entries { get; set; } = [];
}

public sealed class CreatureListRule : CreatureListDefinition
{
    public bool Enabled { get; set; } = true;
    public string Match { get; set; } = "";
    public string? SourcePlugin { get; set; }
}

public sealed record PoolEntry(string Reference, int Weight = 1, short Count = 1);

public static class CreatureListConfiguration
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static CreatureListSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<CreatureListSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Creature list configuration is empty.");
        Validate(settings);
        return settings;
    }

    public static void Validate(CreatureListSettings settings)
    {
        settings.ReviewedScripts = new(settings.ReviewedScripts, StringComparer.OrdinalIgnoreCase);
        foreach (var review in settings.ReviewedScripts)
        {
            ActorConfiguration.ValidateFormKey(review.Key);
            if (!Regex.IsMatch(review.Value, "^[A-Fa-f0-9]{64}$"))
                throw new ArgumentException($"Reviewed script {review.Key} needs a SHA256 fingerprint.");
        }
        ValidateWeights(settings.Weights);
        if (settings.Fallback == CreatureListPolicy.CuratedPool)
            throw new ArgumentException("CuratedPool requires an individual list definition.");
        foreach (var rule in settings.Groups.Concat(settings.PluginRules))
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.Match))
                throw new ArgumentException("List rules need a Name and Match regular expression.");
            _ = new Regex(rule.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            ValidateDefinition(rule);
        }
        settings.FormKeyOverrides = new(settings.FormKeyOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.FormKeyOverrides)
        {
            ActorConfiguration.ValidateFormKey(pair.Key);
            ValidateDefinition(pair.Value);
        }
    }

    private static void ValidateDefinition(CreatureListDefinition definition)
    {
        if (definition.Weights is not null) ValidateWeights(definition.Weights);
        if (definition.Policy == CreatureListPolicy.CuratedPool && definition.Entries.Count == 0)
            throw new ArgumentException("A curated pool needs at least one entry.");
        if (definition.Entries.Sum(entry => (long)entry.Weight) > 255)
            throw new ArgumentException("Curated pools support at most 255 weighted tickets.");
        foreach (var entry in definition.Entries)
        {
            ActorConfiguration.ValidateFormKey(entry.Reference);
            if (entry.Weight < 1 || entry.Count < 1) throw new ArgumentException("Entry Weight and Count must be positive.");
        }
    }

    private static void ValidateWeights(PoolWeights weights)
    {
        if (weights.CommonMaxTier is < 0 or > 10 || weights.StrongMaxTier < weights.CommonMaxTier || weights.StrongMaxTier > 10
            || weights.Common < 1 || weights.Strong < 1 || weights.Rare < 1
            || weights.Common > 10000 || weights.Strong > 10000 || weights.Rare > 10000)
            throw new ArgumentException("Invalid tier bands or weights (weights must be 1–10000).");
    }

    public static (CreatureListDefinition Definition, string Rule) Select(CreatureListSettings settings, string key, string? editorId, string plugin)
    {
        if (settings.FormKeyOverrides.TryGetValue(key, out var explicitDefinition)) return (explicitDefinition, "FormKey override");
        foreach (var rule in settings.PluginRules.Concat(settings.Groups))
            if (rule.Enabled && (rule.SourcePlugin is null || rule.SourcePlugin.Equals(plugin, StringComparison.OrdinalIgnoreCase))
                && Regex.IsMatch(editorId ?? "", rule.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                return (rule, rule.Name);
        return (new() { Policy = settings.Fallback, Reason = "No configured encounter family matched." }, "Fallback");
    }
}
