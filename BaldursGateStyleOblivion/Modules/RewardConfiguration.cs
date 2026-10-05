using System.Text.Json;
using BaldursGateStyleOblivion.Classification;

namespace BaldursGateStyleOblivion.Modules;

public sealed class RewardChoice
{
    public string Name { get; set; } = "";
    public int SelectionLevel { get; set; } = 10;
    public string? Variant { get; set; }
    public int? Count { get; set; }
    public bool Preserve { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Reason { get; set; } = "";
}
public sealed class ScriptRewardChoice
{
    public string Record { get; set; } = "";
    public string Context { get; set; } = "Standalone";
    public string Name { get; set; } = "";
    public int SelectionLevel { get; set; } = 10;
    public bool Preserve { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Reason { get; set; } = "";
}
public sealed class ArtifactMetadata
{
    public string Name { get; set; } = "";
    public int? ArtifactTier { get; set; }
    public bool Unique { get; set; } = true;
    public bool QuestRelated { get; set; } = true;
    public bool Daedric { get; set; }
    public bool HandTuned { get; set; }
    public bool ExcludedFromNormalization { get; set; } = true;
    public string Reason { get; set; } = "Preserve existing stats; unique reward.";
}
public sealed class RewardSettings
{
    public Dictionary<string, RewardChoice> Lists { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ScriptRewardChoice> Scripts { get; set; } = [];
    public Dictionary<string, ArtifactMetadata> Artifacts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public static class RewardConfiguration
{
    public static JsonSerializerOptions Options => EquipmentConfiguration.Options;
    public static RewardSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<RewardSettings>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty reward configuration.");
        Validate(settings); return settings;
    }
    public static void Validate(RewardSettings settings)
    {
        settings.Lists = new(settings.Lists, StringComparer.OrdinalIgnoreCase);
        settings.Artifacts = new(settings.Artifacts, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.Lists)
        {
            ActorConfiguration.ValidateFormKey(pair.Key);
            if (pair.Value.Variant is {} key) ActorConfiguration.ValidateFormKey(key);
            if (pair.Value.SelectionLevel is < 1 or > 100 || pair.Value.Count is <= 0 or > short.MaxValue)
                throw new ArgumentException("Reward selection level must be 1–100; count must be 1–32767.");
        }
        foreach (var script in settings.Scripts)
        {
            ActorConfiguration.ValidateFormKey(script.Record);
            if (script.SelectionLevel is < 1 or > 100 || script.Fingerprint.Length != 64)
                throw new ArgumentException("Script rewards require a 1–100 selection level and a reviewed fingerprint.");
        }
        if (settings.Scripts.GroupBy(s => (s.Record.ToLowerInvariant(), s.Context)).Any(g => g.Count() > 1))
            throw new ArgumentException("Duplicate script reward context.");
        foreach (var pair in settings.Artifacts)
        {
            ActorConfiguration.ValidateFormKey(pair.Key);
            if (pair.Value.ArtifactTier is {} tier) _ = new PowerTier(tier);
        }
    }
}
