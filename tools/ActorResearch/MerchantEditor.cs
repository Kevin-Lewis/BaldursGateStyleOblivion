using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;

internal sealed record MerchantEdit(string FormKey, string? Profile, string? PreferredMaterial, bool Preserve, bool Delete = false);
internal sealed record MerchantConfigEdit(string Json, string Revision);
internal static class MerchantEditor
{
    private static readonly object Gate = new();
    public static void Map(WebApplication app, string config, string reports, string token)
    {
        JsonElement[] Records()
        {
            var files = Directory.GetFiles(reports, "*.merchant-stock.json");
            if (files.Length != 1) throw new InvalidDataException("Run the patcher with merchant stock enabled to generate its report.");
            using var document = JsonDocument.Parse(File.ReadAllText(files[0]));
            return document.RootElement.GetProperty("Merchants").EnumerateArray().Select(row => row.Clone()).ToArray();
        }
        string Revision(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        object Data()
        {
            var text = File.ReadAllText(config); var settings = MerchantConfiguration.Load(config);
            var rows = Records().Select(row =>
            {
                var selected = MerchantConfiguration.Select(settings, row.GetProperty("FormKey").GetString()!,
                    row.GetProperty("SourcePlugin").GetString()!, row.GetProperty("Evidence").GetString()!);
                var node = JsonNode.Parse(row.GetRawText())!; node["Profile"] = selected.Profile; node["Rule"] = selected.Rule;
                node["Material"] = selected.Material ?? (row.TryGetProperty("Race", out var race)
                    ? settings.RaceMaterials.GetValueOrDefault(race.GetString() ?? "", "") : row.GetProperty("RaceMaterial").GetString());
                return node;
            }).ToArray();
            return new { Records = rows, Settings = settings, Json = text, Revision = Revision(text), ConfigurationFile = config };
        }
        void Save(string text)
        {
            var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text + Environment.NewLine); _ = MerchantConfiguration.Load(temporary);
                File.Move(temporary, config, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        IResult Error(Exception error) => Results.BadRequest(new { error = error.Message });
        app.MapGet("/merchants", () => Results.Content(EditorNavigation.ReadTemplate("merchants.html")
            .Replace("/*TOKEN*/\"\"", JsonSerializer.Serialize(token)), "text/html"));
        app.MapGet("/api/merchants", () =>
        {
            try { return Results.Json(Data(), MerchantConfiguration.Options); }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
        app.MapPost("/api/merchants/record", (MerchantEdit edit) =>
        {
            try
            {
                lock (Gate)
                {
                    var record = Records().FirstOrDefault(row => row.GetProperty("FormKey").GetString()!.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase));
                    if (record.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Unknown merchant.");
                    var settings = MerchantConfiguration.Load(config);
                    if (edit.Delete) settings.Overrides.Remove(edit.FormKey);
                    else settings.Overrides[edit.FormKey] = new() { Name = record.GetProperty("Name").GetString() ?? edit.FormKey,
                        GlassItems = settings.Overrides.GetValueOrDefault(edit.FormKey)?.GlassItems ?? [],
                        Profile = edit.Profile, PreferredMaterial = edit.PreferredMaterial, Preserve = edit.Preserve, Reason = "Individual editor assignment." };
                    MerchantConfiguration.Validate(settings); Save(JsonSerializer.Serialize(settings, MerchantConfiguration.Options));
                    return Results.Json(Data(), MerchantConfiguration.Options);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
        app.MapPost("/api/merchants/config", (MerchantConfigEdit edit) =>
        {
            try
            {
                lock (Gate)
                {
                    if (Revision(File.ReadAllText(config)) != edit.Revision) return Results.Conflict(new { error = "Merchant configuration changed. Reload before saving." });
                    Save(edit.Json); return Results.Json(Data(), MerchantConfiguration.Options);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException) { return Error(error); }
        });
    }
}
