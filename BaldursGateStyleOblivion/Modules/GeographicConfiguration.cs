using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;

namespace BaldursGateStyleOblivion.Modules;

public sealed class GeographicSettings
{
    public Dictionary<LocationCategory, GeographicTierRange> CategoryDefaults { get; set; } = new()
    {
        [LocationCategory.Civilized] = new(0, 2), [LocationCategory.Safe] = new(0, 1),
        [LocationCategory.LowDanger] = new(1, 3), [LocationCategory.Moderate] = new(2, 4),
        [LocationCategory.Dangerous] = new(3, 5), [LocationCategory.Severe] = new(3, 6),
        [LocationCategory.Extreme] = new(5, 9)
    };
    public List<GeographicRule> Groups { get; set; } = [];
    public Dictionary<string, GeographicDefinition> FormKeyOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record GeographicTierRange(int MinimumTier, int MaximumTier);

public class GeographicDefinition
{
    public LocationCategory? Category { get; set; }
    public int? MinimumTier { get; set; }
    public int? MaximumTier { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class GeographicRule : GeographicDefinition
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? SourcePlugin { get; set; }
    public string? LocationMatch { get; set; }
    public string? WorldspaceMatch { get; set; }
    public string? RegionMatch { get; set; }
    public string? Signal { get; set; }
}

public static class GeographicConfiguration
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static GeographicSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<GeographicSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Geographic configuration is empty.");
        Validate(settings); return settings;
    }

    public static void Validate(GeographicSettings settings)
    {
        if (settings.CategoryDefaults is null) throw new ArgumentException("Category defaults cannot be null.");
        foreach (var pair in settings.CategoryDefaults)
            if (!Enum.IsDefined(pair.Key) || pair.Value is null || pair.Value.MinimumTier is < 0 or > 10 ||
                pair.Value.MaximumTier is < 0 or > 10 || pair.Value.MinimumTier > pair.Value.MaximumTier)
                throw new ArgumentException("Category defaults need valid tier ranges between 0 and 10.");
        settings.FormKeyOverrides = new(settings.FormKeyOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.FormKeyOverrides) { ActorConfiguration.ValidateFormKey(pair.Key); Validate(pair.Value); }
        foreach (var rule in settings.Groups)
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || new[] { rule.LocationMatch, rule.WorldspaceMatch, rule.RegionMatch, rule.Signal }.All(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Geographic rules need a name and at least one selector.");
            foreach (var pattern in new[] { rule.LocationMatch, rule.WorldspaceMatch, rule.RegionMatch })
                if (pattern is not null) _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            Validate(rule);
        }
    }

    private static void Validate(GeographicDefinition definition)
    {
        if (definition.MinimumTier is < 0 or > 10 || definition.MaximumTier is < 0 or > 10 ||
            definition.MinimumTier.HasValue != definition.MaximumTier.HasValue || definition.MinimumTier > definition.MaximumTier)
            throw new ArgumentException("Typical tier ranges need both limits, ordered between 0 and 10.");
        if (definition.Category is { } category && !Enum.IsDefined(category)) throw new ArgumentException("Unknown location category.");
        if (string.IsNullOrWhiteSpace(definition.Reason)) throw new ArgumentException("Location decisions need a concise reason.");
    }

    public static GeographicDefinition ResolveRange(GeographicSettings settings, GeographicDefinition definition)
    {
        var range = definition.Category is { } category ? settings.CategoryDefaults.GetValueOrDefault(category) : null;
        return new()
        {
            Category = definition.Category, Reason = definition.Reason,
            MinimumTier = definition.MinimumTier ?? range?.MinimumTier,
            MaximumTier = definition.MaximumTier ?? range?.MaximumTier
        };
    }

    public static (GeographicDefinition Definition, string Rule) Select(GeographicSettings settings,
        string key, string plugin, string location, string worldspace, string region, IEnumerable<string> signals)
    {
        if (settings.FormKeyOverrides.TryGetValue(key, out var definition)) return (ResolveRange(settings, definition), "Individual override");
        bool Match(string? pattern, string value) => pattern is null || Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        foreach (var rule in settings.Groups.Where(rule => rule.Enabled).OrderBy(rule => rule.SourcePlugin is null ? 1 : 0))
            if ((rule.SourcePlugin is null || rule.SourcePlugin.Equals(plugin, StringComparison.OrdinalIgnoreCase)) &&
                Match(rule.LocationMatch, location) && Match(rule.WorldspaceMatch, worldspace) && Match(rule.RegionMatch, region) &&
                (rule.Signal is null || signals.Contains(rule.Signal, StringComparer.OrdinalIgnoreCase))) return (ResolveRange(settings, rule), rule.Name);
        return (new() { Reason = "Insufficient evidence for a starting classification; review this location." }, "Unclassified");
    }
}
