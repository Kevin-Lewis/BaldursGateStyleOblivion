using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaldursGateStyleOblivion.Classification;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ActorResearch;

internal sealed record ActorEdit(string FormKey, int? Tier, string? Handling, int? FixedLevel = null, bool? Delevel = null);
internal sealed record ActorDelete(string FormKey);
internal sealed record GroupEdit(string Group, string Id, int? Tier, bool Enabled);

internal static class Editor
{
    // Row updates must match catalog property names and include nulls to clear old overrides.
    private static readonly JsonSerializerOptions ResponseJson = new(ActorConfiguration.JsonOptions)
    { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static async Task Run(Actor[] actors, string config, int port, bool open, string reports, string listConfig, string geographicConfig)
    {
        if (port is < 1024 or > 65535) throw new ArgumentException("Choose a port between 1024 and 65535.");
        var url = $"http://127.0.0.1:{port}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateBuilder();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls(url);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Host != "127.0.0.1" || context.Request.Method == "POST" &&
                (context.Request.Headers.Origin != url || context.Request.Headers["X-Actor-Editor-Token"] != token))
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.MapGet("/", () => Results.Content(Catalog.Html(actors, ActorConfiguration.Load(config), config, token), "text/html"));
        app.MapPost("/api/actor", (ActorEdit edit) =>
        {
            var actor = actors.FirstOrDefault(actor => actor.FormKey.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase));
            if (actor is null) return Results.BadRequest(new { error = "Actor not found in the loaded reports." });
            try
            {
                ConfigurationEditor.Actor(config, actor.FormKey, actor.Name ?? actor.EditorID ?? actor.FormKey, edit.Tier, edit.Handling, edit.FixedLevel, edit.Delevel);
                return Results.Json(Catalog.Rows([actor], ActorConfiguration.Load(config), config)[0], ResponseJson);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { return Results.BadRequest(new { error = exception.Message }); }
        });
        app.MapPost("/api/actor/delete", (ActorDelete edit) =>
        {
            var actor = actors.FirstOrDefault(actor => actor.FormKey.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase));
            if (actor is null) return Results.BadRequest(new { error = "Actor not found in the loaded reports." });
            try
            {
                ConfigurationEditor.DeleteActor(config, actor.FormKey);
                return Results.Json(Catalog.Rows([actor], ActorConfiguration.Load(config), config)[0], ResponseJson);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { return Results.BadRequest(new { error = exception.Message }); }
        });
        app.MapPost("/api/group", (GroupEdit edit) =>
        {
            try
            {
                ConfigurationEditor.Group(config, edit.Group, edit.Id, edit.Tier, edit.Enabled);
                return Results.Json(Catalog.Rows(actors, ActorConfiguration.Load(config), config), ResponseJson);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { return Results.BadRequest(new { error = exception.Message }); }
        });
        ListEditor.Map(app, actors, listConfig, reports, token);
        GeographicEditor.Map(app, geographicConfig, reports, token);
        DungeonEditor.Map(app, Path.Combine(Path.GetDirectoryName(geographicConfig)!, "dungeons.json"), reports);
        EquipmentEditor.Map(app, Path.Combine(Path.GetDirectoryName(config)!, "equipment.json"), reports, token);
        LootEditor.Map(app, Path.Combine(Path.GetDirectoryName(config)!, "loot.json"), reports, token);
        MerchantEditor.Map(app, Path.Combine(Path.GetDirectoryName(config)!, "merchants.json"), reports, token);
        RewardEditor.Map(app, Path.Combine(Path.GetDirectoryName(config)!, "rewards.json"), reports, token);
        CombatEditor.Map(app, Path.Combine(Path.GetDirectoryName(config)!, "combat.json"), reports, token, config, actors);
        MagicEditor.Map(app, reports, Path.Combine(Path.GetDirectoryName(config)!, "magic.json"), token, config);
        await app.StartAsync();
        Console.WriteLine($"Actor editor: {url}\nConfiguration: {config}\nKeep this window open while editing.");
        if (open) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        await app.WaitForShutdownAsync();
    }
}
