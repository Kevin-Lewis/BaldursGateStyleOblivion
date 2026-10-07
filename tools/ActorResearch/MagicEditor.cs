using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;

internal static class MagicEditor
{
    public static void Map(WebApplication app,string reports)
    {
        app.MapGet("/magic",()=>Results.Content(EditorNavigation.ReadTemplate("magic.html"),"text/html"));
        app.MapGet("/api/magic",()=>
        {
            var files=Directory.GetFiles(reports,"*.magic-analysis.json");
            if(files.Length!=1)return Results.NotFound(new{error="Run the patcher with Magic analysis enabled to generate the report."});
            using var document=JsonDocument.Parse(File.ReadAllText(files[0]));
            return Results.Json(document.RootElement.Clone());
        });
    }
}