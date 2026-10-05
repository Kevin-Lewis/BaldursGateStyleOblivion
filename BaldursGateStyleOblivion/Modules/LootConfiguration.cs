using System.Text.Json;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;
namespace BaldursGateStyleOblivion.Modules;

public sealed class LootProfile
{
    public int PremiumPerThousand { get; set; } = 20;
    public int GlassPerThousand { get; set; }
    public int EbonyPerThousand { get; set; }
    public int DaedricPerThousand { get; set; }
}
public sealed class LootOverride
{
    public string? Profile { get; set; }
    public bool Preserve { get; set; }
}
public sealed class LootRule
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    public string? SourcePlugin { get; set; }
    public string Category { get; set; } = "Special";
    public string? Profile { get; set; }
    public bool Preserve { get; set; }
}
public sealed class LootSettings
{
    public Dictionary<string, LootProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, string> TierProfiles { get; set; } = new();
    public List<LootRule> Groups { get; set; } = [];
    public Dictionary<string, LootOverride> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LootOverride> ListOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ItemTiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ReviewedItemScripts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TreasureTierBonus { get; set; } = 1;
}
public static class LootConfiguration
{
    public static JsonSerializerOptions Options => EquipmentConfiguration.Options;
    public static LootSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<LootSettings>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty loot configuration.");
        Validate(settings); return settings;
    }
    public static void Validate(LootSettings settings)
    {
        settings.Profiles = new(settings.Profiles, StringComparer.OrdinalIgnoreCase);
        settings.Overrides = new(settings.Overrides, StringComparer.OrdinalIgnoreCase);
        settings.ListOverrides = new(settings.ListOverrides, StringComparer.OrdinalIgnoreCase);
        settings.ItemTiers = new(settings.ItemTiers, StringComparer.OrdinalIgnoreCase);
        settings.ReviewedItemScripts = new(settings.ReviewedItemScripts, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.ReviewedItemScripts)
        {
            ActorConfiguration.ValidateFormKey(pair.Key);
            if (pair.Value.Length != 64) throw new ArgumentException("Reviewed item scripts require a fingerprint.");
        }
        if (!settings.Profiles.ContainsKey("Modest")) throw new ArgumentException("A Modest fallback profile is required.");
        if (settings.TreasureTierBonus is < 0 or > 2) throw new ArgumentException("Treasure tier bonus must be 0–2.");
        foreach (var profile in settings.Profiles.Values)
            if (profile.PremiumPerThousand is < 0 or > 1000 || profile.GlassPerThousand is < 0 or > 1000 || profile.EbonyPerThousand is < 0 or > 1000 || profile.DaedricPerThousand is < 0 or > 1000 ||
                profile.PremiumPerThousand + profile.GlassPerThousand + profile.EbonyPerThousand + profile.DaedricPerThousand > 1000)
                throw new ArgumentException("Loot rates must each be 0–1000 and total at most 1000.");
        foreach (var name in settings.TierProfiles.Values.Concat(settings.Groups.Select(rule => rule.Profile).OfType<string>())
                     .Concat(settings.Overrides.Values.Concat(settings.ListOverrides.Values).Select(value => value.Profile).OfType<string>()))
            if (!settings.Profiles.ContainsKey(name)) throw new ArgumentException($"Unknown loot profile: {name}");
        foreach (var tier in settings.TierProfiles.Keys.Concat(settings.ItemTiers.Values)) _ = new PowerTier(tier);
        foreach (var key in settings.Overrides.Keys.Concat(settings.ListOverrides.Keys).Concat(settings.ItemTiers.Keys)) ActorConfiguration.ValidateFormKey(key);
        foreach (var group in settings.Groups) _ = new Regex(group.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
    public static (string Profile, string Category, string Rule, bool Preserve) Select(LootSettings settings, string key, string plugin, string evidence, int tier, bool daedric)
    {
        var group = settings.Groups.OrderByDescending(group => group.SourcePlugin is not null).FirstOrDefault(group =>
            (group.SourcePlugin is null || group.SourcePlugin.Equals(plugin, StringComparison.OrdinalIgnoreCase)) &&
            Regex.IsMatch(evidence, group.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        var category = group?.Category ?? "Special";
        if (category == "Boss") tier = Math.Min(10, tier + settings.TreasureTierBonus);
        var profile = group?.Profile ?? settings.TierProfiles.GetValueOrDefault(tier, "Modest");
        if (daedric && group?.Profile is null && settings.Profiles.ContainsKey("Daedric" + tier)) profile = "Daedric" + tier;
        if (settings.Overrides.TryGetValue(key, out var manual)) return (manual.Profile ?? profile, category, "Individual override", manual.Preserve);
        return (profile, category, group?.Name ?? "Danger tier fallback", group?.Preserve ?? false);
    }
}
