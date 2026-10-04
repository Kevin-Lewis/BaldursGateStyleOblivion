using System.Diagnostics;
using System.Security.Cryptography;
using BaldursGateStyleOblivion.Classification;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace ActorResearch;

internal sealed record ActorEdit(string FormKey, int? Tier, string? Handling);
internal sealed record GroupEdit(string Group, string Id, int? Tier, bool Enabled);

internal static class Editor
{
    public static async Task Run(Actor[] actors, string config, int port, bool open)
    {
        if (port is < 1024 or > 65535) throw new ArgumentException("Choose a port between 1024 and 65535.");
        var url = $"http://127.0.0.1:{port}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls(url);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Host != "127.0.0.1" || context.Request.Method == "POST" &&
                (context.Request.Headers.Origin != url || context.Request.Headers["X-Actor-Editor-Token"] != token))
            { context.Response.StatusCode = 403; return; }
            await next(context);
        });
        app.MapGet("/", () => Results.Content(Catalog.Html(actors, ActorConfiguration.Load(config), config, token), "text/html"));
        app.MapPost("/api/actor", (ActorEdit edit) =>
        {
            var actor = actors.FirstOrDefault(actor => actor.FormKey.Equals(edit.FormKey, StringComparison.OrdinalIgnoreCase));
            if (actor is null) return Results.BadRequest(new { error = "Actor not found in the loaded reports." });
            try
            {
                ConfigurationEditor.Actor(config, actor.FormKey, actor.Name ?? actor.EditorID ?? actor.FormKey, edit.Tier, edit.Handling);
                return Results.Ok(Catalog.Rows([actor], ActorConfiguration.Load(config), config)[0]);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or System.Text.Json.JsonException)
            { return Results.BadRequest(new { error = exception.Message }); }
        });
        app.MapPost("/api/group", (GroupEdit edit) =>
        {
            try
            {
                ConfigurationEditor.Group(config, edit.Group, edit.Id, edit.Tier, edit.Enabled);
                return Results.Ok(Catalog.Rows(actors, ActorConfiguration.Load(config), config));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or System.Text.Json.JsonException)
            { return Results.BadRequest(new { error = exception.Message }); }
        });
        await app.StartAsync();
        Console.WriteLine($"Actor editor: {url}\nConfiguration: {config}\nKeep this window open while editing.");
        if (open) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        await app.WaitForShutdownAsync();
    }
}
