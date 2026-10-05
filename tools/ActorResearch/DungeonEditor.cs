using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;
internal sealed record DungeonEdit(string FormKey, DungeonDefinition Definition);
internal sealed record DungeonGroupsEdit(List<DungeonRule> Groups);
internal static class DungeonEditor
{
    private static readonly object Gate = new();
    public static void Map(WebApplication app, string config, string reports)
    {
        JsonElement Site(string key)
        {
            var file = Directory.GetFiles(reports, "*.dungeons.json").Single();
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var site = document.RootElement.GetProperty("Sites").EnumerateArray()
                .FirstOrDefault(site => site.GetProperty("FormKey").GetString() == key);
            if (site.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Unknown dungeon site; rerun the patcher after changing membership.");
            return site.Clone();
        }
        IResult Error(Exception error) => Results.BadRequest(new { error = error.Message });
        void Update(Action<DungeonSettings> edit)
        {
            lock (Gate)
            {
                var original = File.ReadAllText(config); var settings = DungeonConfiguration.Load(config);
                var groupsBefore = JsonSerializer.Serialize(settings.Groups, GeographicConfiguration.JsonOptions);
                edit(settings); DungeonConfiguration.Validate(settings);
                var root = JsonNode.Parse(original)!.AsObject();
                if (groupsBefore != JsonSerializer.Serialize(settings.Groups, GeographicConfiguration.JsonOptions))
                    root["Groups"] = JsonSerializer.SerializeToNode(settings.Groups, GeographicConfiguration.JsonOptions);
                root["FormKeyOverrides"] = JsonSerializer.SerializeToNode(settings.FormKeyOverrides, GeographicConfiguration.JsonOptions);
                var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, root.ToJsonString(GeographicConfiguration.JsonOptions));
                    if (File.ReadAllText(config) != original) throw new IOException("Dungeon configuration changed; reload before saving.");
                    File.Move(temporary, config, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        app.MapGet("/api/dungeons", (string? formkey) =>
        {
            try
            {
                var settings = DungeonConfiguration.Load(config);
                if (formkey is null) return Results.Json(new { settings.Groups, Configuration = config }, GeographicConfiguration.JsonOptions);
                var site = Site(formkey);
                var profile = DungeonConfiguration.Select(settings, formkey, site.GetProperty("Evidence").GetString()!).Profile;
                return Results.Json(new { Site = site, Profile = profile, HasOverride = settings.FormKeyOverrides.ContainsKey(formkey) }, GeographicConfiguration.JsonOptions);
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidDataException or InvalidOperationException) { return Error(error); }
        });
        app.MapPost("/api/dungeons", (DungeonEdit edit) =>
        {
            try { _ = Site(edit.FormKey);
                using var report = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(reports, "*.dungeons.json").Single()));
                var interiors = report.RootElement.GetProperty("InteriorCells").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (edit.Definition.Cells.Any(cell => !interiors.Contains(cell))) throw new ArgumentException("Dungeon membership must reference known interior cells.");
                Update(settings => settings.FormKeyOverrides[edit.FormKey] = edit.Definition); return Results.Json(new { Saved = true }, GeographicConfiguration.JsonOptions); }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidDataException or InvalidOperationException) { return Error(error); }
        });
        app.MapPost("/api/dungeons/delete", (ActorDelete edit) =>
        {
            try { _ = Site(edit.FormKey); Update(settings => settings.FormKeyOverrides.Remove(edit.FormKey)); return Results.Json(new { Saved = true }, GeographicConfiguration.JsonOptions); }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidDataException or InvalidOperationException) { return Error(error); }
        });
        app.MapPost("/api/dungeons/groups", (DungeonGroupsEdit edit) =>
        {
            try { Update(settings => settings.Groups = edit.Groups); return Results.Json(new { Saved = true }, GeographicConfiguration.JsonOptions); }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidDataException) { return Error(error); }
        });
    }
}
