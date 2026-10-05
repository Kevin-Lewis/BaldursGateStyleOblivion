using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;

internal sealed record EquipmentEdit(string FormKey, string? Profile, bool Preserve, bool Delete = false);
internal sealed record EquipmentConfigEdit(string Json, string Revision);

internal static class EquipmentEditor
{
    private static readonly object Gate = new();
    public static void Map(WebApplication app, string config, string reports, string token)
    {
        JsonElement[] Actors()
        {
            var files = Directory.GetFiles(reports, "*.equipment-distribution.json");
            if (files.Length != 1) throw new InvalidDataException("Run the patcher to generate the equipment report.");
            using var document = JsonDocument.Parse(File.ReadAllText(files[0]));
            return document.RootElement.GetProperty("Actors").EnumerateArray().Select(row => row.Clone()).ToArray();
        }
        string Revision(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        object Data()
        {
            var text = File.ReadAllText(config);
            var settings = EquipmentConfiguration.Load(config);
            var actors = Actors().Select(row =>
            {
                var selected = EquipmentConfiguration.Select(settings, row.GetProperty("FormKey").GetString()!,
                    row.GetProperty("SourcePlugin").GetString()!, row.GetProperty("Evidence").GetString()!,
                    row.GetProperty("EquipmentTier").ValueKind == JsonValueKind.Number ? row.GetProperty("EquipmentTier").GetInt32() : null);
                var node = JsonNode.Parse(row.GetRawText())!; node["Profile"] = selected.Profile; node["Rule"] = selected.Rule;
                return node;
            }).ToArray();
            return new { Actors = actors, Settings = settings, Json = text, Revision = Revision(text), ConfigurationFile = config };
        }
        void Save(string text)
        {
            var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text + Environment.NewLine);
                _ = EquipmentConfiguration.Load(temporary);
                File.Move(temporary, config, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        IResult Error(Exception error) => Results.BadRequest(new { error = error.Message });
        app.MapGet("/equipment", () => Results.Content(EditorNavigation.ReadTemplate("equipment.html")
            .Replace("/*TOKEN*/\"\"", JsonSerializer.Serialize(token)), "text/html"));
        app.MapGet("/api/equipment", () =>
        {
            try { return Results.Json(Data(), EquipmentConfiguration.Options); }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
        app.MapPost("/api/equipment/actor", (EquipmentEdit edit) =>
        {
            try
            {
                lock (Gate)
                {
                    var actor = Actors().FirstOrDefault(row => row.GetProperty("FormKey").GetString()!.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase));
                    if (actor.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Unknown actor.");
                    var settings = EquipmentConfiguration.Load(config);
                    if (edit.Delete) settings.ActorOverrides.Remove(edit.FormKey);
                    else settings.ActorOverrides[edit.FormKey] = new() { Name = actor.GetProperty("Name").GetString() ?? edit.FormKey, Profile = edit.Profile, Preserve = edit.Preserve };
                    EquipmentConfiguration.Validate(settings);
                    Save(JsonSerializer.Serialize(settings, EquipmentConfiguration.Options));
                    return Results.Json(Data(), EquipmentConfiguration.Options);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
        app.MapPost("/api/equipment/config", (EquipmentConfigEdit edit) =>
        {
            try
            {
                lock (Gate)
                {
                    if (Revision(File.ReadAllText(config)) != edit.Revision) return Results.Conflict(new { error = "Equipment configuration changed. Reload before saving." });
                    Save(edit.Json);
                    return Results.Json(Data(), EquipmentConfiguration.Options);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
    }
}
