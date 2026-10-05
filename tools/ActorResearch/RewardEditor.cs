using System.Security.Cryptography;
using System.Text.Json;
using BaldursGateStyleOblivion.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
namespace ActorResearch;
internal sealed record RewardEdit(string FormKey, int SelectionLevel, string? Variant, int? Count, bool Preserve);
internal sealed record RewardScriptEdit(string Record, string Context, int SelectionLevel, bool Preserve);
internal sealed record ArtifactEdit(string FormKey, int? ArtifactTier, bool ExcludedFromNormalization);
internal sealed record RewardConfigEdit(string Json, string Revision);
internal static class RewardEditor
{
    private static readonly object Gate = new();
    public static void Map(WebApplication app, string config, string reports, string token)
    {
        string Revision(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        object Data()
        {
            var text = File.ReadAllText(config);
            var files = Directory.GetFiles(reports, "*.quest-rewards.json");
            JsonElement? report = null;
            if (files.Length == 1) { using var document = JsonDocument.Parse(File.ReadAllText(files[0])); report = document.RootElement.Clone(); }
            return new { Settings = RewardConfiguration.Load(config), Report = report, Json = text, Revision = Revision(text), ConfigurationFile = config };
        }
        void Save(RewardSettings settings) => SaveText(JsonSerializer.Serialize(settings, RewardConfiguration.Options));
        void SaveText(string text)
        {
            var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, text + Environment.NewLine); _ = RewardConfiguration.Load(temporary); File.Move(temporary, config, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        IResult Handle(Func<IResult> action)
        {
            try { lock (Gate) return action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException)
            { return Results.BadRequest(new { error = error.Message }); }
        }
        app.MapGet("/rewards", () => Results.Content(EditorNavigation.ReadTemplate("rewards.html")
            .Replace("/*TOKEN*/\"\"", JsonSerializer.Serialize(token)), "text/html"));
        app.MapGet("/api/rewards", () => Handle(() => Results.Json(Data(), RewardConfiguration.Options)));
        app.MapPost("/api/rewards/list", (RewardEdit edit) => Handle(() =>
        {
            var settings = RewardConfiguration.Load(config);
            if (!settings.Lists.TryGetValue(edit.FormKey, out var choice)) throw new ArgumentException("Unknown reward family.");
            choice.SelectionLevel = edit.SelectionLevel; choice.Variant = string.IsNullOrWhiteSpace(edit.Variant) ? null : edit.Variant; choice.Count = edit.Count; choice.Preserve = edit.Preserve;
            RewardConfiguration.Validate(settings); Save(settings); return Results.Json(Data(), RewardConfiguration.Options);
        }));
        app.MapPost("/api/rewards/script", (RewardScriptEdit edit) => Handle(() =>
        {
            var settings = RewardConfiguration.Load(config);
            var choice = settings.Scripts.SingleOrDefault(s => s.Record == edit.Record && s.Context == edit.Context) ?? throw new ArgumentException("Unknown reviewed script context.");
            choice.SelectionLevel = edit.SelectionLevel; choice.Preserve = edit.Preserve;
            RewardConfiguration.Validate(settings); Save(settings); return Results.Json(Data(), RewardConfiguration.Options);
        }));
        app.MapPost("/api/rewards/artifact", (ArtifactEdit edit) => Handle(() =>
        {
            var settings = RewardConfiguration.Load(config);
            if (!settings.Artifacts.TryGetValue(edit.FormKey, out var metadata)) throw new ArgumentException("Unknown artifact.");
            metadata.ArtifactTier = edit.ArtifactTier; metadata.ExcludedFromNormalization = edit.ExcludedFromNormalization;
            RewardConfiguration.Validate(settings); Save(settings); return Results.Json(Data(), RewardConfiguration.Options);
        }));
        app.MapPost("/api/rewards/config", (RewardConfigEdit edit) => Handle(() =>
        {
            if (Revision(File.ReadAllText(config)) != edit.Revision) return Results.Conflict(new { error = "Configuration changed. Reload before saving." });
            SaveText(edit.Json); return Results.Json(Data(), RewardConfiguration.Options);
        }));
    }
}
