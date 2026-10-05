using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;

internal sealed record ListEdit(string FormKey, CreatureListDefinition Definition);

internal static class ListEditor
{
    private static readonly object Gate = new();

    public static void Map(WebApplication app, Actor[] actors, string config, string reports, string token)
    {
        JsonElement[] ReadLists()
        {
            var files = Directory.GetFiles(reports, "*.creature-list-deleveling.json");
            if (files.Length != 1) throw new InvalidDataException("Run the updated patcher once to generate its creature-list report.");
            using var document = JsonDocument.Parse(File.ReadAllText(files[0]));
            return document.RootElement.GetProperty("Lists").EnumerateArray().Select(row => row.Clone()).ToArray();
        }
        object Data()
        {
            var settings = CreatureListConfiguration.Load(config);
            var names = actors.ToDictionary(actor => actor.FormKey, actor => actor.Name ?? actor.EditorID ?? actor.FormKey, StringComparer.OrdinalIgnoreCase);
            var lists = ReadLists();
            foreach (var row in lists) names[row.GetProperty("FormKey").GetString()!] = row.GetProperty("EditorID").GetString() ?? row.GetProperty("FormKey").GetString()!;
            return new
            {
                settings.Weights, settings.Groups, Configuration = config,
                Lists = lists.Select(row =>
                {
                    var key = row.GetProperty("FormKey").GetString()!;
                    var id = row.GetProperty("EditorID").GetString();
                    var plugin = row.GetProperty("SourcePlugin").GetString()!;
                    var (definition, rule) = CreatureListConfiguration.Select(settings, key, id, plugin);
                    settings.FormKeyOverrides.TryGetValue(key, out var stored);
                    var entries = row.GetProperty("Original").GetProperty("Entries").EnumerateArray()
                        .GroupBy(entry => (Reference: entry.GetProperty("Reference").GetString()!, Count: entry.GetProperty("Count").ValueKind == JsonValueKind.Null ? (short)1 : entry.GetProperty("Count").GetInt16()))
                        .Select(group => new PoolEntry(group.Key.Reference, group.Count(), group.Key.Count)).ToArray();
                    return new
                    {
                        FormKey = key, EditorID = id, SourcePlugin = plugin, Purpose = row.GetProperty("Purpose").GetString(),
                        HasOverride = stored is not null, Policy = definition.Policy, Rule = rule, definition.Reason, definition.AllowSpecial, definition.Weights,
                        Entries = (stored?.Policy == CreatureListPolicy.CuratedPool ? stored.Entries.ToArray() : entries)
                            .Select(entry => new { entry.Reference, Name = names.GetValueOrDefault(entry.Reference, entry.Reference), entry.Weight, entry.Count }),
                        LastStatus = row.GetProperty("Status").GetString(), LastReason = row.GetProperty("Reason").GetString(),
                        OriginalDependsOnPlayerLevel = row.GetProperty("OriginalDependsOnPlayerLevel").GetBoolean(),
                        PlannedDependsOnPlayerLevel = row.GetProperty("PlannedDependsOnPlayerLevel").GetBoolean(),
                        Original = row.GetProperty("Original"), Planned = row.GetProperty("Planned"),
                        Cells = row.GetProperty("DirectCells"), Uses = row.GetProperty("DirectUses")
                    };
                }).ToArray()
            };
        }
        IResult Error(Exception exception) => Results.BadRequest(new { error = exception.Message });
        app.MapGet("/lists", () =>
        {
            try
            {
                var html = EditorNavigation.ReadTemplate("lists.html")
                    .Replace("__LIST_DATA__", JsonSerializer.Serialize(Data(), CreatureListConfiguration.JsonOptions))
                    .Replace("__EDITOR_TOKEN__", token);
                return Results.Content(html, "text/html");
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException) { return Error(exception); }
        });
        app.MapGet("/api/lists", () =>
        {
            try { return Results.Json(Data(), CreatureListConfiguration.JsonOptions); }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException) { return Error(exception); }
        });
        app.MapPost("/api/list", (ListEdit edit) =>
        {
            try
            {
                var rows = ReadLists();
                var row = rows.FirstOrDefault(row => row.GetProperty("FormKey").GetString()!.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase));
                if (row.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("List is absent from loaded reports.");
                var key = row.GetProperty("FormKey").GetString()!;
                var allowed = actors.Select(actor => actor.FormKey).Concat(rows.Select(row => row.GetProperty("FormKey").GetString()!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (edit.Definition.Entries.Any(entry => !allowed.Contains(entry.Reference))) throw new ArgumentException("Entry reference must be an actor or list in the loaded reports.");
                Save(config, root =>
                {
                    root["FormKeyOverrides"] ??= new JsonObject();
                    var overrides = root["FormKeyOverrides"]!.AsObject();
                    var storedKey = overrides.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Key ?? key;
                    edit.Definition.Name = row.GetProperty("EditorID").GetString() ?? key;
                    overrides[storedKey] = JsonSerializer.SerializeToNode(edit.Definition, CreatureListConfiguration.JsonOptions);
                });
                return Results.Json(Data(), CreatureListConfiguration.JsonOptions);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { return Error(exception); }
        });
        app.MapPost("/api/list/delete", (ActorDelete edit) =>
        {
            try
            {
                Save(config, root =>
                {
                    var overrides = root["FormKeyOverrides"]!.AsObject();
                    var key = overrides.FirstOrDefault(pair => pair.Key.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase)).Key;
                    if (key is not null) overrides.Remove(key);
                });
                return Results.Json(Data(), CreatureListConfiguration.JsonOptions);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { return Error(exception); }
        });
        app.MapPost("/api/list/weights", (PoolWeights weights) =>
        {
            try
            {
                Save(config, root => root["Weights"] = JsonSerializer.SerializeToNode(weights, CreatureListConfiguration.JsonOptions));
                return Results.Json(Data(), CreatureListConfiguration.JsonOptions);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { return Error(exception); }
        });
    }

    private static void Save(string path, Action<JsonObject> edit)
    {
        lock (Gate)
        {
            var original = File.ReadAllText(path);
            var root = JsonNode.Parse(original)!.AsObject(); edit(root);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, root.ToJsonString(CreatureListConfiguration.JsonOptions) + Environment.NewLine);
                _ = CreatureListConfiguration.Load(temporary);
                if (File.ReadAllText(path) != original) throw new IOException("Configuration changed while saving; reload and try again.");
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
