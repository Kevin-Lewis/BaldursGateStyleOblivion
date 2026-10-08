using System.Text.Json;

namespace ActorResearch;

internal sealed record Actor(string FormKey, string? EditorID, string? Name, string RecordType, string SourcePlugin,
    string WinningOverridePlugin, JsonElement Original, Dictionary<string, string[]> Evidence,
    JsonElement? Scaling, JsonElement? Inventory, JsonElement? Usage, JsonElement? Stats = null, string? StatsStatus = null, bool AutoCalculated = false);

