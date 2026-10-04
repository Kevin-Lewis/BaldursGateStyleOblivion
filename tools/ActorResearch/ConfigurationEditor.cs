using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Classification;

namespace ActorResearch;

internal static class ConfigurationEditor
{
    private static readonly object Gate = new();

    public static bool AddActor(string path, string key, ActorOverride assignment)
    {
        ActorConfiguration.ValidateFormKey(key);
        lock (Gate)
        {
            if (ActorConfiguration.Load(path).FormKeyOverrides.ContainsKey(key)) return false;
            var original = File.ReadAllText(path);
            var root = JsonNode.Parse(original)!.AsObject();
            var actors = Actors(root);
            var node = JsonSerializer.SerializeToNode(assignment, ActorConfiguration.JsonOptions)!.AsObject();
            var ordered = new JsonObject();
            foreach (var name in new[] { "Name", "PowerTier", "Handling", "Description", "Reason", "Uncertainty", "Sources", "Model" })
                if (node.Remove(name, out var value)) ordered[name] = value;
            foreach (var pair in node) ordered[pair.Key] = pair.Value?.DeepClone();
            actors[key] = ordered;
            Save(path, root, original);
            return true;
        }
    }

    public static void Actor(string path, string key, string name, int? tier, string? handling)
    {
        ActorConfiguration.ValidateFormKey(key);
        if (tier is not null) _ = new PowerTier(tier.Value);
        if (handling is not null && (!Enum.TryParse<ActorHandling>(handling, out var parsed) || !Enum.IsDefined(parsed)))
            throw new ArgumentException("Invalid actor handling.");
        lock (Gate)
        {
            var original = File.ReadAllText(path);
            var root = JsonNode.Parse(original)!.AsObject();
            var actors = Actors(root);
            var stored = actors.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Key ?? key;
            var entry = actors[stored]?.AsObject() ?? new JsonObject { ["Name"] = name };
            if (tier is null) entry.Remove("PowerTier"); else entry["PowerTier"] = tier.Value;
            if (handling is null) entry.Remove("Handling"); else entry["Handling"] = handling;
            if (actors[stored] is null) actors[stored] = entry;
            Save(path, root, original);
        }
    }

    public static void Group(string path, string group, string id, int? tier, bool enabled)
    {
        if (tier is not null) _ = new PowerTier(tier.Value);
        lock (Gate)
        {
            var original = File.ReadAllText(path);
            var root = JsonNode.Parse(original)!.AsObject();
            var rule = root["Groups"]?[group]?.AsArray().FirstOrDefault(node => node?["Id"]?.GetValue<string>() == id)?.AsObject()
                ?? throw new ArgumentException("Group rule not found.");
            var values = rule["Values"]!.AsObject();
            if (tier is null) values.Remove("PowerTier"); else values["PowerTier"] = tier.Value;
            rule["Enabled"] = enabled;
            Save(path, root, original);
        }
    }

    private static JsonObject Actors(JsonObject root)
    {
        if (root["FormKeyOverrides"] is null) root["FormKeyOverrides"] = new JsonObject();
        return root["FormKeyOverrides"]!.AsObject();
    }

    private static void Save(string path, JsonObject root, string original)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(ActorConfiguration.JsonOptions) + Environment.NewLine);
            _ = ActorConfiguration.Load(temporary);
            if (File.ReadAllText(path) != original) throw new IOException("Configuration changed while saving; reload and try again.");
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
