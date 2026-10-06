using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;

internal sealed record CombatSave(string Json, string Revision);
internal sealed record CombatPreview(CombatSettings Settings, CombatScenario Scenario);
internal sealed record CombatBuildRequest(CombatSettings Settings, string Build, int Tier, double Level, bool Player = false, string? Race = null, bool Female = false, string? Birthsign = null);

internal static class CombatEditor
{
    private static readonly object Gate = new();
    public static void Map(WebApplication app, string config, string reports, string token, string actorConfig)
    {
        JsonElement Catalog()
        {
            var files = Directory.GetFiles(reports, "*.physical-combat.json");
            if (files.Length != 1) throw new InvalidDataException("Generate a physical combat report with EnablePhysicalCombatAnalysis enabled. Select a report folder containing one run.");
            using var document = JsonDocument.Parse(File.ReadAllText(files[0])); return document.RootElement.Clone();
        }
        string Revision(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        object Data()
        {
            var text = File.ReadAllText(config);
            return new { Settings = CombatConfiguration.Parse(text), Catalog = Catalog(), Revision = Revision(text), ConfigurationFile = config, LevelMapping = BaldursGateStyleOblivion.Classification.ActorConfiguration.Load(actorConfig).LevelMapping };
        }
        IResult Error(Exception error) => Results.BadRequest(new { error = error.Message });
        app.MapGet("/combat", () => Results.Content(EditorNavigation.ReadTemplate("combat.html")
            .Replace("/*TOKEN*/\"\"", JsonSerializer.Serialize(token)), "text/html"));
        app.MapGet("/api/combat", () =>
        {
            try { return Results.Json(Data(), CombatConfiguration.Options); }
            catch (Exception error) when (error is ArgumentException or IOException or JsonException or InvalidDataException) { return Error(error); }
        });
        app.MapPost("/api/combat/build", (CombatBuildRequest request) =>
        {
            try { CombatConfiguration.Validate(request.Settings); CombatConfiguration.Range(request.Tier,0,10,"Tier"); CombatConfiguration.Range(request.Level,1,100,"Level");
                if(!request.Settings.Gameplay.ActorBuilds.ContainsKey(request.Build)) throw new ArgumentException("Unknown actor build.");
                if(request.Player)
                {
                    var catalog=Catalog();
                    if(!catalog.TryGetProperty("CharacterCreation",out var creation))throw new InvalidDataException("Rebuild the combat report to load character creation records.");
                    return Results.Json(PlayerBuilds.AtLevel(request.Settings.Gameplay,creation.Deserialize<CreationCatalog>()!,request.Build,request.Race??"",request.Female,request.Birthsign,request.Level),CombatConfiguration.Options);
                }
                return Results.Json(new PlayerBuildResult(CombatBuilds.AtLevel(request.Settings.Gameplay,request.Build,request.Tier,request.Level,BaldursGateStyleOblivion.Classification.ActorConfiguration.Load(actorConfig).LevelMapping),["NPC tier training budget; class specialties, supporting skills and untrained skills."]),CombatConfiguration.Options); }
            catch(Exception error) when(error is ArgumentException or JsonException or InvalidDataException or IOException){return Error(error);}
        });
        app.MapPost("/api/combat/preview", (CombatPreview request) =>
        {
            try
            {
                if (request.Settings is null || request.Scenario is null) throw new ArgumentException("Configuration and scenario are required.");
                CombatConfiguration.Validate(request.Settings);
                var catalog = Catalog();
                var items = catalog.GetProperty("Items").Deserialize<PhysicalItem[]>(CombatConfiguration.Options)!.ToDictionary(i => i.FormKey);
                var gameSettings = (catalog.TryGetProperty("OriginalGameSettings",out var originals) ? originals : catalog.GetProperty("GameSettings")).Deserialize<Dictionary<string, double>>(CombatConfiguration.Options)!;
                // A scenario references catalog actors only for provenance; observed stats remain explicit inputs.
                foreach (var fighter in new[] { request.Scenario.Player, request.Scenario.Enemy })
                    if (fighter?.ActorKey is not null && !catalog.GetProperty("Actors").EnumerateArray().Any(a => a.GetProperty("FormKey").GetString() == fighter.ActorKey))
                        throw new ArgumentException("Unknown actor reference.");
                return Results.Json(new
                {
                    Current = CombatAnalysis.Run(request.Scenario, request.Settings, items, gameSettings, false),
                    Proposed = CombatAnalysis.Run(request.Scenario, request.Settings, items, gameSettings, true),
                    Items = items.Values.Select(source => new { Before = source, After = PhysicalBalance.Propose(source, request.Settings) }),
                    Curves = Enumerable.Range(0, 11).Select(tier => new { Tier = tier, Offense = request.Settings.OffenseTarget.At(tier), Health = request.Settings.HealthTarget.At(tier) })
                }, CombatConfiguration.Options);
            }
            catch (Exception error) when (error is ArgumentException or IOException or JsonException or InvalidDataException) { return Error(error); }
        });
        app.MapPost("/api/combat/config", (CombatSave edit) =>
        {
            try
            {
                lock (Gate)
                {
                    if (Revision(File.ReadAllText(config)) != edit.Revision) return Results.Conflict(new { error = "Combat settings changed. Reload before saving." });
                    var settings = CombatConfiguration.Parse(edit.Json);
                    var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, CombatConfiguration.Options) + Environment.NewLine); File.Move(temporary, config, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    return Results.Json(Data(), CombatConfiguration.Options);
                }
            }
            catch (Exception error) when (error is ArgumentException or IOException or JsonException or InvalidDataException) { return Error(error); }
        });
    }
}
