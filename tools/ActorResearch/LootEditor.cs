using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;

internal sealed record LootEdit(string FormKey, string? Profile, bool Preserve, bool Delete = false);
internal sealed record LootConfigEdit(string Json, string Revision);

internal static class LootEditor
{
    private static readonly object Gate = new();
    public static void Map(WebApplication app, string config, string reports, string token)
    {
        JsonElement[] Records()
        {
            var files = Directory.GetFiles(reports, "*.world-loot.json");
            if (files.Length != 1) throw new InvalidDataException("Run the patcher to generate the loot report.");
            using var document = JsonDocument.Parse(File.ReadAllText(files[0]));
            return document.RootElement.GetProperty("Records").EnumerateArray().Select(row => row.Clone()).ToArray();
        }
        string Revision(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        object Data()
        {
            var text = File.ReadAllText(config);
            var settings = LootConfiguration.Load(config);
            var rows = Records().Select(row =>
            {
                var selected = LootConfiguration.Select(settings, row.GetProperty("FormKey").GetString()!,
                    row.GetProperty("SourcePlugin").GetString()!, row.GetProperty("Evidence").GetString()!,
                    row.GetProperty("Tier").GetInt32(), row.GetProperty("DaedricContext").GetBoolean());
                if (!settings.Overrides.ContainsKey(row.GetProperty("FormKey").GetString()!) &&
                    settings.Overrides.TryGetValue(row.GetProperty("Source").GetString()!, out var baseOverride))
                    selected = (baseOverride.Profile ?? selected.Profile, selected.Category, "Container base override", baseOverride.Preserve);
                var node = JsonNode.Parse(row.GetRawText())!; node["Profile"] = selected.Profile; node["Rule"] = selected.Rule;
                return node;
            }).ToArray();
            return new { Records = rows, Settings = settings, Json = text, Revision = Revision(text), ConfigurationFile = config };
        }
        void Save(string text)
        {
            var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text + Environment.NewLine);
                _ = LootConfiguration.Load(temporary);
                File.Move(temporary, config, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        IResult Error(Exception error) => Results.BadRequest(new { error = error.Message });
        app.MapGet("/loot", () => Results.Content(EditorNavigation.ReadTemplate("loot.html")
            .Replace("/*TOKEN*/\"\"", JsonSerializer.Serialize(token)), "text/html"));
        app.MapGet("/api/loot", () =>
        {
            try { return Results.Json(Data(), LootConfiguration.Options); }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
        app.MapPost("/api/loot/record", (LootEdit edit) =>
        {
            try
            {
                lock (Gate)
                {
                    var record = Records().FirstOrDefault(row => row.GetProperty("FormKey").GetString()!.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase));
                    if (record.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Unknown loot record.");
                    var settings = LootConfiguration.Load(config);
                    if (edit.Delete) settings.Overrides.Remove(edit.FormKey);
                    else settings.Overrides[edit.FormKey] = new() { Profile = edit.Profile, Preserve = edit.Preserve };
                    LootConfiguration.Validate(settings);
                    Save(JsonSerializer.Serialize(settings, LootConfiguration.Options));
                    return Results.Json(Data(), LootConfiguration.Options);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
        app.MapPost("/api/loot/config", (LootConfigEdit edit) =>
        {
            try
            {
                lock (Gate)
                {
                    if (Revision(File.ReadAllText(config)) != edit.Revision) return Results.Conflict(new { error = "Loot configuration changed. Reload before saving." });
                    Save(edit.Json);
                    return Results.Json(Data(), LootConfiguration.Options);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
    }
}
