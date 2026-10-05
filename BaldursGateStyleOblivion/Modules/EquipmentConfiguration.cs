using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;

namespace BaldursGateStyleOblivion.Modules;

public enum EquipmentQuality { Improvised, Poor, Common, Professional, Military, HighQuality, Elite, Rare, Artifact }

public sealed class EquipmentDistribution
{
    public Dictionary<EquipmentQuality, int> Weights { get; set; } = new();
    public bool PreferRaceMaterial { get; set; } = true;
    public int EnchantedPercent { get; set; } = 10;
    public int EbonyPerThousand { get; set; }
    public int DaedricPerThousand { get; set; }
}

public sealed class EquipmentRule
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    public string SourcePlugin { get; set; } = "";
    public string Profile { get; set; } = "Common";
    public string Reason { get; set; } = "";
}

public sealed class EquipmentOverride
{
    public string Name { get; set; } = "";
    public string? Profile { get; set; }
    public bool Preserve { get; set; }
}

public sealed class EquipmentSettings
{
    public Dictionary<string, EquipmentDistribution> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, string> TierProfiles { get; set; } = new();
    public int RaceMaterialWeight { get; set; } = 8;
    public Dictionary<string, string> RaceMaterials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EquipmentRule> Groups { get; set; } = new();
    public Dictionary<string, EquipmentOverride> ActorOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EquipmentOverride> ListOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EquipmentQuality> ItemOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class EquipmentConfiguration
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static EquipmentSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<EquipmentSettings>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Empty equipment configuration.");
        Validate(settings);
        return settings;
    }

    public static void Validate(EquipmentSettings settings)
    {
        settings.Profiles = new(settings.Profiles, StringComparer.OrdinalIgnoreCase);
        settings.RaceMaterials = new(settings.RaceMaterials, StringComparer.OrdinalIgnoreCase);
        if (settings.RaceMaterialWeight is < 1 or > 16) throw new ArgumentException("Race material weight must be 1�16.");
        if (settings.RaceMaterials.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
            throw new ArgumentException("Race material preferences must name a race and material.");
        settings.ActorOverrides = new(settings.ActorOverrides, StringComparer.OrdinalIgnoreCase);
        settings.ListOverrides = new(settings.ListOverrides, StringComparer.OrdinalIgnoreCase);
        settings.ItemOverrides = new(settings.ItemOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var profile in settings.Profiles.Values)
            if (profile.EnchantedPercent is < 0 or > 100 || profile.EbonyPerThousand is < 0 or > 10 ||
                profile.DaedricPerThousand is < 0 or > 1000 || profile.EbonyPerThousand + profile.DaedricPerThousand > 1000 || profile.Weights.Count == 0 ||
                profile.Weights.Values.Any(weight => weight < 0) || profile.Weights.Values.Sum() is <= 0 or > 10000)
                throw new ArgumentException("Equipment weights must total 1–10000; enchanted chance must be 0–100.");
        foreach (var profile in settings.TierProfiles.Values.Concat(settings.Groups.Select(rule => rule.Profile))
                     .Concat(settings.ActorOverrides.Values.Concat(settings.ListOverrides.Values).Select(value => value.Profile).OfType<string>()))
            if (!settings.Profiles.ContainsKey(profile)) throw new ArgumentException($"Unknown equipment profile: {profile}");
        foreach (var tier in settings.TierProfiles.Keys) _ = new PowerTier(tier);
        foreach (var rule in settings.Groups) _ = new Regex(rule.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        foreach (var key in settings.ActorOverrides.Keys.Concat(settings.ListOverrides.Keys).Concat(settings.ItemOverrides.Keys))
            ActorConfiguration.ValidateFormKey(key);
        foreach (var quality in settings.ItemOverrides.Values)
            if (!Enum.IsDefined(quality)) throw new ArgumentException("Invalid equipment quality.");
        if (!settings.Profiles.ContainsKey("Common")) throw new ArgumentException("A Common fallback profile is required.");
    }

    internal static (string Profile, string Rule, bool Preserve) Select(EquipmentSettings settings, string key,
        string plugin, string evidence, int? tier)
    {
        if (settings.ActorOverrides.TryGetValue(key, out var assignment))
            return (assignment.Profile ?? "Common", "Individual actor override", assignment.Preserve);
        foreach (var rule in settings.Groups.OrderByDescending(rule => rule.SourcePlugin.Length > 0))
            if ((rule.SourcePlugin.Length == 0 || plugin.Equals(rule.SourcePlugin, StringComparison.OrdinalIgnoreCase)) &&
                Regex.IsMatch(evidence, rule.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                return (rule.Profile, rule.Name + ": " + rule.Reason, false);
        return (tier is {} value && settings.TierProfiles.TryGetValue(value, out var profile) ? profile : "Common",
            "Equipment tier / actor tier fallback", false);
    }
}
