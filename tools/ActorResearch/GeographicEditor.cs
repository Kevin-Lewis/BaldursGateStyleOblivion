using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;

internal sealed record LocationEdit(string FormKey, GeographicDefinition Definition);
internal sealed record GeographicDefaultsEdit(Dictionary<BaldursGateStyleOblivion.Classification.LocationCategory, GeographicTierRange> Defaults);
internal sealed record GeographicGroupEdit(int Index, GeographicRule Rule);

internal static class GeographicEditor
{
    private static readonly object Gate = new();

    public static void Map(WebApplication app, string config, string reports, string token)
    {
        string? path = null;
        DateTime reportStamp = DateTime.MinValue;
        Dictionary<string, JsonElement> locations = new(StringComparer.OrdinalIgnoreCase);
        void ReadReport()
        {
            var files = Directory.GetFiles(reports, "*.geography.json");
            if (files.Length != 1) throw new InvalidDataException("Expected one geographic report in the selected report directory.");
            path = files[0];
            if (!File.Exists(path)) throw new InvalidDataException("Run geographic discovery once to create its location report.");
            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == reportStamp) return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            locations = document.RootElement.GetProperty("Locations").EnumerateArray()
                .ToDictionary(row => row.GetProperty("FormKey").GetString()!, row => row.Clone(), StringComparer.OrdinalIgnoreCase);
            reportStamp = stamp;
        }
        (GeographicDefinition Definition, string Rule) Resolve(JsonElement row, GeographicSettings settings)
        {
            string Text(string field) => row.GetProperty(field).ValueKind == JsonValueKind.Null ? "" : row.GetProperty(field).ToString();
            var world = row.GetProperty("Worldspace");
            var worldText = world.ValueKind == JsonValueKind.Null ? "" : world.GetProperty("EditorID") + " " + world.GetProperty("Name");
            var regions = string.Join(" ", row.GetProperty("Regions").EnumerateArray().Select(region => region.GetProperty("EditorID").ToString()));
            return GeographicConfiguration.Select(settings, Text("FormKey"), Text("SourcePlugin"), Text("EditorID") + " " + Text("Name"), worldText, regions,
                row.GetProperty("Signals").EnumerateArray().Select(signal => signal.GetString()!));
        }
        object Data(HttpRequest request)
        {
            ReadReport(); var settings = GeographicConfiguration.Load(config);
            var search = request.Query["search"].ToString(); var category = request.Query["category"].ToString(); var overrides = request.Query["overrides"].ToString();
            var page = int.TryParse(request.Query["page"], out var index) ? Math.Max(0, index) : 0;
            var size = int.TryParse(request.Query["size"], out var limit) ? Math.Clamp(limit, 10, 100) : 50;
            var rows = locations.Values.Select(row =>
            {
                var (definition, rule) = Resolve(row, settings);
                var raw = settings.FormKeyOverrides.GetValueOrDefault(row.GetProperty("FormKey").GetString()!)
                    ?? settings.Groups.FirstOrDefault(group => group.Name == rule);
                var customRange = raw?.MinimumTier is not null;
                return new
                {
                    FormKey = row.GetProperty("FormKey").GetString()!, EditorID = row.GetProperty("EditorID").GetString(), Name = row.GetProperty("Name").GetString(),
                    SourcePlugin = row.GetProperty("SourcePlugin").GetString(), Worldspace = row.GetProperty("Worldspace"), Interior = row.GetProperty("Interior").GetBoolean(),
                    HasOverride = settings.FormKeyOverrides.ContainsKey(row.GetProperty("FormKey").GetString()!),
                    CustomRange = customRange, definition.Category, definition.MinimumTier, definition.MaximumTier, definition.Reason, Rule = rule,
                    Confidence = rule == "Individual override" ? "Manual" : rule == "Unclassified" || rule.Contains("wilderness", StringComparison.OrdinalIgnoreCase) ? "Low" : "Medium",
                    Observed = row.GetProperty("ObservedThreatTierRange"), PossibleActors = row.GetProperty("PossibleActorCount").GetInt32(),
                    ReviewSignals = row.GetProperty("ReviewSignals")
                };
            }).Where(row => (search.Length == 0 || $"{row.Name} {row.EditorID} {row.FormKey} {row.Worldspace} {row.SourcePlugin}".Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (category.Length == 0 || (row.Category?.ToString() ?? "Unclassified") == category) &&
                (overrides.Length == 0 || row.HasOverride == (overrides == "only")) &&
                (request.Query["encounters"] != "only" || row.PossibleActors > 0)).ToArray();
            var sorted = request.Query["sort"] == "danger" ? rows.OrderByDescending(row => (int?)row.Category ?? -1).ThenBy(row => row.Name ?? row.EditorID).ThenBy(row => row.FormKey)
                : rows.OrderBy(row => row.Name ?? row.EditorID ?? row.FormKey, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.FormKey);
            return new { Configuration = config, ProposalOnly = true, Total = rows.Length, Page = page, Size = size,
                Locations = sorted.Skip(page * size).Take(size).ToArray(), settings.Groups, settings.CategoryDefaults };
        }
        IResult Error(Exception exception) => Results.BadRequest(new { error = exception.Message });
        void Update(Action<GeographicSettings> change)
        {
            lock (Gate)
            {
                var original = File.ReadAllText(config); var settings = GeographicConfiguration.Load(config);
                var defaultsBefore = JsonSerializer.Serialize(settings.CategoryDefaults, GeographicConfiguration.JsonOptions);
                var groupsBefore = JsonSerializer.Serialize(settings.Groups, GeographicConfiguration.JsonOptions);
                change(settings); GeographicConfiguration.Validate(settings);
                var root = JsonNode.Parse(original)!.AsObject();
                if (defaultsBefore != JsonSerializer.Serialize(settings.CategoryDefaults, GeographicConfiguration.JsonOptions))
                    root["CategoryDefaults"] = JsonSerializer.SerializeToNode(settings.CategoryDefaults, GeographicConfiguration.JsonOptions);
                root["FormKeyOverrides"] = JsonSerializer.SerializeToNode(settings.FormKeyOverrides, GeographicConfiguration.JsonOptions);
                if (groupsBefore != JsonSerializer.Serialize(settings.Groups, GeographicConfiguration.JsonOptions))
                    root["Groups"] = JsonSerializer.SerializeToNode(settings.Groups, GeographicConfiguration.JsonOptions);
                var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, root.ToJsonString(GeographicConfiguration.JsonOptions) + Environment.NewLine);
                    if (File.ReadAllText(config) != original) throw new IOException("Configuration changed during this edit; reload and try again.");
                    File.Move(temporary, config, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        app.MapGet("/geography", () => Results.Content(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "geography.html"))
            .Replace("__EDITOR_TOKEN__", token), "text/html"));
        app.MapGet("/api/geography", (HttpRequest request) =>
        {
            try { return Results.Json(Data(request), GeographicConfiguration.JsonOptions); }
            catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or InvalidDataException) { return Error(exception); }
        });
        app.MapGet("/api/geography/details", (string formkey) =>
        {
            try { ReadReport(); return locations.TryGetValue(formkey, out var row) ? Results.Json(row) : Results.NotFound(); }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException) { return Error(exception); }
        });
        app.MapPost("/api/geography", (LocationEdit edit) =>
        {
            try { ReadReport(); if (!locations.ContainsKey(edit.FormKey)) return Results.BadRequest(new { error = "Unknown location." });
                Update(settings => settings.FormKeyOverrides[edit.FormKey] = edit.Definition); return Results.Json(new { Saved = true }, GeographicConfiguration.JsonOptions); }
            catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or InvalidDataException) { return Error(exception); }
        });
        app.MapPost("/api/geography/delete", (ActorDelete edit) =>
        {
            try { ReadReport(); if (!locations.ContainsKey(edit.FormKey)) return Results.BadRequest(new { error = "Unknown location." });
                Update(settings => settings.FormKeyOverrides.Remove(edit.FormKey)); return Results.Json(new { Saved = true }, GeographicConfiguration.JsonOptions); }
            catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or InvalidDataException) { return Error(exception); }
        });
        app.MapPost("/api/geography/defaults", (GeographicDefaultsEdit edit) =>
        {
            try { Update(settings => settings.CategoryDefaults = edit.Defaults); return Results.Json(new { Saved = true }, GeographicConfiguration.JsonOptions); }
            catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or InvalidDataException) { return Error(exception); }
        });
        app.MapPost("/api/geography/group", (GeographicGroupEdit edit) =>
        {
            try { Update(settings => { if (edit.Index < 0 || edit.Index >= settings.Groups.Count) throw new ArgumentException("Unknown group."); settings.Groups[edit.Index] = edit.Rule; }); return Results.Json(new { Saved = true }, GeographicConfiguration.JsonOptions); }
            catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or InvalidDataException) { return Error(exception); }
        });
    }
}
