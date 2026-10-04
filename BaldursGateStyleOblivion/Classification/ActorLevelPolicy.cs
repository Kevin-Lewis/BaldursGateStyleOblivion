using System.Text.RegularExpressions;

namespace BaldursGateStyleOblivion.Classification;

internal sealed record ActorLevelDecision(ActorHandling Handling, int? Tier, int? TargetLevel, string Rule, string Reason);

internal static class ActorLevelPolicy
{
    public static ActorLevelDecision Decide(ClassificationSettings settings, string key, string? editorId, string? name,
        bool creature, bool playerOffset, int? originalLevel, ActorProfile? profile, string? scriptRisk = null)
    {
        settings.FormKeyOverrides.TryGetValue(key, out var manual);
        var handling = profile?.Dimensions.TryGetValue("Handling", out var field) == true
            ? (ActorHandling)field.Selected.Value : creature ? ActorHandling.Generic : ActorHandling.Named;
        var tier = profile?.Tier?.Value;
        ActorLevelDecision Skip(string reason) => new(handling, tier, null, "Safeguard", reason);
        if (key.Equals("000007:Oblivion.esm", StringComparison.OrdinalIgnoreCase) || editorId == "Player") return Skip("Player base actor is protected.");
        if (manual?.Delevel == false) return Skip("Delevel=false individual exemption.");
        if (scriptRisk is not null) return Skip(scriptRisk);
        if (originalLevel is null) return Skip("Actor configuration is missing.");
        if (manual?.Delevel != true && (handling == ActorHandling.ProtectedSpecial || IsHelper(editorId, name, manual?.Description)))
            return Skip("Protected, technical, dead, or player-mirror actor; explicit Delevel=true is required.");
        if (manual?.FixedLevel is not null)
            return new(handling, tier, manual.FixedLevel, "Explicit FixedLevel override", "Individual fixed level takes precedence over tier mapping.");
        if (tier is null) return Skip("No usable PowerTier; original behavior preserved.");
        if (tier == 10)
            return playerOffset ? Skip("Scaled Apex actor requires an individual FixedLevel override.")
                : new(handling, tier, originalLevel, "Existing fixed Apex level", "Already-fixed Apex encounter preserved without a shared level formula.");
        if (!settings.LevelMapping.TryGetValue(tier.Value, out var target) || target is null) return Skip("No level configured for this tier.");
        var curated = manual?.PowerTier is not null || manual?.Delevel == true;
        if (!curated && handling is ActorHandling.QuestRelated or ActorHandling.FactionLeader or ActorHandling.MinorBoss or ActorHandling.MajorBoss)
            return Skip("Quest-sensitive or boss actor needs a curated tier, FixedLevel, or explicit Delevel=true.");
        if (!curated && !playerOffset && handling == ActorHandling.Named)
            return Skip("Already-fixed named actor has no curated tier; existing level preserved.");
        return new(handling, tier, target, $"Tier {tier} -> level {target}",
            curated ? "Curated actor tier mapped to a fixed level." : "Automatic tier fallback mapped to a fixed level.");
    }

    private static bool IsHelper(string? editorId, string? name, string? description) =>
        string.IsNullOrWhiteSpace(name) || Regex.IsMatch(editorId ?? "", @"^(Test|Demo|XXX|ConvSys|NDEmil)|Voice(?!sInTheAir)|SpeakNPC|Corpse(?!rot)|^Dead|(?<!Un)Dead(?:[0-9]|$)|Clone|DASkull", RegexOptions.IgnoreCase)
        || Regex.IsMatch(name, @"^(Dead |Murdered |Fallen Knight|ND Conversation|Template Creature)|\bVoice\b|Recording|Corrupted Clone", RegexOptions.IgnoreCase)
        || Regex.IsMatch(description ?? "", @"\b(unused|cut-content)\b|never appears|does not appear|represented.*corpse|encounter represents the corpse", RegexOptions.IgnoreCase);
}
