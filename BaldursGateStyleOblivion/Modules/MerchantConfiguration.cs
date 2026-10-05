using System.Text.Json;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;

namespace BaldursGateStyleOblivion.Modules;

public sealed class MerchantProfile
{
    public int SelectionLevel { get; set; } = 10;
    public EquipmentQuality MaxEquipmentQuality { get; set; } = EquipmentQuality.Common;
    public int GlassPercent { get; set; }
}
public sealed class MerchantOverride
{
    public string Name { get; set; } = "";
    public string? Profile { get; set; }
    public string? PreferredMaterial { get; set; }
    public List<string> GlassItems { get; set; } = [];
    public bool Preserve { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class MerchantRule
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    public string Profile { get; set; } = "General Store";
    public string? SourcePlugin { get; set; }
}
public sealed class MerchantSettings
{
    public Dictionary<string, MerchantProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MerchantRule> Groups { get; set; } = [];
    public Dictionary<string, MerchantOverride> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PreservedLists { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RaceMaterials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int MaterialWeight { get; set; } = 8;
}
public static class MerchantConfiguration
{
    public static JsonSerializerOptions Options => EquipmentConfiguration.Options;
    public static MerchantSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<MerchantSettings>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Empty merchant configuration.");
        Validate(settings);
        return settings;
    }
    public static void Validate(MerchantSettings settings)
    {
        if (settings.Profiles is null || settings.Groups is null || settings.Overrides is null || settings.PreservedLists is null || settings.RaceMaterials is null ||
            settings.Profiles.Values.Any(p => p is null) || settings.Groups.Any(g => g is null) || settings.Overrides.Values.Any(v => v is null || v.GlassItems is null))
            throw new ArgumentException("Merchant configuration collections and entries must not be null.");
        settings.Profiles = new(settings.Profiles, StringComparer.OrdinalIgnoreCase);
        settings.Overrides = new(settings.Overrides, StringComparer.OrdinalIgnoreCase);
        settings.RaceMaterials = new(settings.RaceMaterials, StringComparer.OrdinalIgnoreCase);
        settings.PreservedLists = new(settings.PreservedLists, StringComparer.OrdinalIgnoreCase);
        if (!settings.Profiles.ContainsKey("General Store")) throw new ArgumentException("A General Store fallback profile is required.");
        if (settings.MaterialWeight is < 1 or > 16) throw new ArgumentException("Material weight must be 1–16.");
        foreach (var profile in settings.Profiles.Values)
            if (profile.SelectionLevel is < 1 or > 100 || profile.GlassPercent is < 0 or > 5 ||
                !Enum.IsDefined(profile.MaxEquipmentQuality) || profile.MaxEquipmentQuality > EquipmentQuality.HighQuality)
                throw new ArgumentException("Merchant selection level must be 1–100, Glass percent 0–5, and ordinary quality at most HighQuality.");
        foreach (var name in settings.Groups.Select(g => g.Profile).Concat(settings.Overrides.Values.Select(v => v.Profile).OfType<string>()))
            if (!settings.Profiles.ContainsKey(name)) throw new ArgumentException($"Unknown merchant profile: {name}");
        foreach (var rule in settings.Groups) _ = new Regex(rule.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        foreach (var key in settings.Overrides.Keys.Concat(settings.PreservedLists)) ActorConfiguration.ValidateFormKey(key);
        foreach (var key in settings.Overrides.Values.SelectMany(v => v.GlassItems)) ActorConfiguration.ValidateFormKey(key);
        if (settings.Overrides.Values.Any(v => v.GlassItems.Count > 20)) throw new ArgumentException("A curated Glass offer may contain at most 20 alternatives.");
        foreach (var material in settings.RaceMaterials.Values.Concat(settings.Overrides.Values.Select(v => v.PreferredMaterial).OfType<string>()))
            if (material is not ("" or "Orcish" or "Elven")) throw new ArgumentException("Material preference must be Orcish or Elven, or empty to disable.");
    }
    public static (string Profile, string Rule, bool Preserve, string? Material) Select(MerchantSettings settings,
        string key, string plugin, string evidence)
    {
        var rule = settings.Groups.OrderByDescending(g => g.SourcePlugin is not null).FirstOrDefault(g =>
            (g.SourcePlugin is null || g.SourcePlugin.Equals(plugin, StringComparison.OrdinalIgnoreCase)) &&
            Regex.IsMatch(evidence, g.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        var profile = rule?.Profile ?? "General Store";
        if (settings.Overrides.TryGetValue(key, out var manual))
            return (manual.Profile ?? profile, "Individual override: " + manual.Reason, manual.Preserve, manual.PreferredMaterial);
        return (profile, rule?.Name ?? "Conservative general stock fallback", false, null);
    }
}
