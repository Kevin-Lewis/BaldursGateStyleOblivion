using BaldursGateStyleOblivion.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BaldursGateStyleOblivion.Classification;

public static class ActorConfiguration
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static ClassificationSettings Load(string path)
    {
        var result = new ClassificationSettings();
        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Read(string file)
        {
            file = Path.GetFullPath(file);
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !seen.Add(file))
                throw new InvalidDataException($"Outside configuration directory or repeated include: {file}");
            var part = JsonSerializer.Deserialize<ClassificationSettings>(File.ReadAllText(file), JsonOptions)
                ?? throw new InvalidDataException($"Empty actor configuration: {file}");
            if (file.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) result.ResearchModel = part.ResearchModel;
            foreach (var group in part.Groups)
            {
                if (!result.Groups.TryAdd(group.Key, group.Value)) throw new InvalidDataException($"Duplicate group: {group.Key}");
                part.Rules.AddRange(group.Value);
            }
            foreach (var rule in part.Rules) rule.ConfigurationFile = file;
            result.Rules.AddRange(part.Rules);
            foreach (var pair in part.FormKeyOverrides)
            {
                pair.Value.ConfigurationFile = file;
                if (!result.FormKeyOverrides.TryAdd(pair.Key, pair.Value))
                    throw new InvalidDataException($"Duplicate override: {pair.Key}");
            }
            foreach (var include in part.Includes)
                Read(Path.Combine(Path.GetDirectoryName(file)!, include));
        }
        Read(path);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in result.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)) throw new InvalidDataException($"Missing or duplicate rule ID: {rule.Id}");
            if (rule.Priority is null || !Enum.IsDefined(rule.Priority.Value) || rule.Priority == RulePriority.ExplicitFormKeyOverride)
                throw new InvalidDataException($"Missing or invalid priority: {rule.Id}");
            ValidateCondition(rule.Evidence, rule.Match, rule.MatchMode);
            foreach (var condition in rule.All) ValidateCondition(condition.Evidence, condition.Match, condition.MatchMode);
            new ActorProfile().Apply(rule.Values, rule.Id, "Settings validation", rule.Priority.Value);
        }
        foreach (var pair in result.FormKeyOverrides)
        {
            ValidateFormKey(pair.Key);
            new ActorProfile().Apply(pair.Value, "FormKey override", "Settings validation");
        }
        return result;
    }

    public static void ValidateFormKey(string key)
    {
        if (!Regex.IsMatch(key, @"^[0-9A-Fa-f]{6}:[^:]+\.(esm|esp|esl)$", RegexOptions.IgnoreCase)
            || key.Split(':')[1].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Invalid FormKey: {key}");
    }

    private static void ValidateCondition(string evidence, string match, string mode)
    {
        if (evidence is not ("Faction" or "Class" or "Race" or "CreatureType" or "Plugin" or "EditorID" or "Name" or "FormKey" or "RecordType" or "Location" or "Archetype" or "Always"))
            throw new InvalidDataException($"Unknown evidence: {evidence}");
        if (mode is not ("Exact" or "Prefix" or "Contains") || (evidence != "Always" && string.IsNullOrWhiteSpace(match)))
            throw new InvalidDataException($"Invalid match: {evidence} / {match} / {mode}");
    }

    public static ActorProfile Classify(ClassificationSettings settings, string formKey, string plugin, Dictionary<string, string[]> evidence)
    {
        var signals = new Dictionary<string, string[]>(evidence, StringComparer.OrdinalIgnoreCase)
        { ["FormKey"] = [formKey], ["Plugin"] = [plugin] };
        bool Matches(string kind, string match, string mode) => kind == "Always" ||
            signals.TryGetValue(kind, out var values) && values.Any(value => mode switch
            {
                "Prefix" => value.StartsWith(match, StringComparison.OrdinalIgnoreCase),
                "Contains" => value.Contains(match, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(value, match, StringComparison.OrdinalIgnoreCase)
            });
        var profile = new ActorProfile();
        foreach (var rule in settings.Rules)
            if (rule.Enabled && (rule.SourcePlugin is null || string.Equals(rule.SourcePlugin, plugin, StringComparison.OrdinalIgnoreCase))
                && Matches(rule.Evidence, rule.Match, rule.MatchMode)
                && rule.All.All(condition => Matches(condition.Evidence, condition.Match, condition.MatchMode)))
                profile.Apply(rule.Values, rule.Id, string.IsNullOrWhiteSpace(rule.Reason) ? $"{rule.Evidence} {rule.MatchMode} matched {rule.Match}" : rule.Reason, rule.Priority!.Value);
        if (settings.FormKeyOverrides.TryGetValue(formKey, out var assignment))
            profile.Apply(assignment, "FormKey override", string.IsNullOrWhiteSpace(assignment.Reason) ? $"Explicit override for {formKey}" : assignment.Reason, RulePriority.ExplicitFormKeyOverride);
        return profile;
    }
}
