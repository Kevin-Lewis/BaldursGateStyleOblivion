using System.Text.Json;
using System.Text.RegularExpressions;

namespace BaldursGateStyleOblivion.Modules;

public sealed class DungeonSettings
{
    public List<DungeonRule> Groups { get; set; } = [];
    public Dictionary<string, DungeonDefinition> FormKeyOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public class DungeonDefinition
{
    public bool Enabled { get; set; } = true;
    public string? Name { get; set; }
    public List<string> Cells { get; set; } = [];
    public int BasePowerTier { get; set; } = 3;
    public GeographicTierRange? EnemyTierRange { get; set; }
    public int BossTierModifier { get; set; }
    public GeographicTierRange? LootTierRange { get; set; }
    public string? Faction { get; set; }
    public string? CreatureFamily { get; set; }
    public double? SpecialEncounterChance { get; set; }
    public string Reason { get; set; } = "Dungeon cap; common encounters should sit below it.";
}
public sealed class DungeonRule : DungeonDefinition
{
    public string Match { get; set; } = "";
    public string? SourcePlugin { get; set; }
}

public static class DungeonConfiguration
{
    public static DungeonSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<DungeonSettings>(File.ReadAllText(path), GeographicConfiguration.JsonOptions)
            ?? throw new InvalidDataException("Empty dungeon configuration.");
        Validate(settings); return settings;
    }
    public static void Validate(DungeonSettings settings)
    {
        settings.FormKeyOverrides = new(settings.FormKeyOverrides, StringComparer.OrdinalIgnoreCase);
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.FormKeyOverrides)
        {
            Classification.ActorConfiguration.ValidateFormKey(pair.Key);
            if (pair.Value.Cells.Count > 0 && !pair.Value.Cells.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("A manual dungeon must include its identifying cell in Cells.");
            foreach (var cell in pair.Value.Cells)
            {
                Classification.ActorConfiguration.ValidateFormKey(cell);
                if (!members.Add(cell)) throw new ArgumentException("A cell cannot belong to two manual dungeon sites.");
            }
        }
        foreach (var definition in settings.Groups.Cast<DungeonDefinition>().Concat(settings.FormKeyOverrides.Values))
        {
            if (definition.BasePowerTier is < 0 or > 10 || definition.BossTierModifier is < 0 or > 2 ||
                definition.BasePowerTier + definition.BossTierModifier > 10 ||
                definition.SpecialEncounterChance is { } chance && (!double.IsFinite(chance) || chance is < 0 or > 0.25))
                throw new ArgumentException("Dungeon cap must be 0–10; boss modifier 0–2; optional special chance 0–0.25.");
            foreach (var range in new[] { definition.EnemyTierRange, definition.LootTierRange })
                if (range is not null && (range.MinimumTier < 0 || range.MaximumTier > 10 || range.MinimumTier > range.MaximumTier))
                    throw new ArgumentException("Dungeon ranges must be ordered between 0 and 10.");
            if (definition.EnemyTierRange?.MaximumTier > definition.BasePowerTier)
                throw new ArgumentException("Ordinary enemy range cannot exceed the dungeon cap.");
            if (string.IsNullOrWhiteSpace(definition.Reason)) throw new ArgumentException("Dungeon decisions need a reason.");
        }
        foreach (var rule in settings.Groups)
        {
            if (string.IsNullOrWhiteSpace(rule.Match)) throw new ArgumentException("Dungeon groups need a match expression.");
            _ = new Regex(rule.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
    }

    public static (DungeonDefinition Profile, string Rule) Select(DungeonSettings settings, string key, string text)
    {
        if (settings.FormKeyOverrides.TryGetValue(key, out var manual)) return (manual, "Individual dungeon override");
        foreach (var rule in settings.Groups.OrderBy(rule => rule.SourcePlugin is null ? 1 : 0))
            if (rule.Enabled && (rule.SourcePlugin is null || key[(key.IndexOf(':') + 1)..].Equals(rule.SourcePlugin, StringComparison.OrdinalIgnoreCase)) &&
                Regex.IsMatch(text, rule.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) return (rule, rule.Name ?? rule.Match);
        return (new(), "Dungeon fallback");
    }

}
