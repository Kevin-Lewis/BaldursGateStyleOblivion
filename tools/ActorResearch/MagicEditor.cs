using System.Security.Cryptography;
using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Magic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;
internal sealed record MagicSave(string Json,string Revision);
internal sealed record MagicPreview(MagicSettings Settings,MagicScenario Scenario);
internal static class MagicEditor
{
    private static readonly object Gate=new();
    public static void Map(WebApplication app,string reports,string config,string token,string actorConfig)
    {
        JsonElement? Report(string suffix)
        {
            var files=Directory.GetFiles(reports,"*."+suffix+".json");if(files.Length!=1)return null;
            using var document=JsonDocument.Parse(File.ReadAllText(files[0]));return document.RootElement.Clone();
        }
        string Revision(string text)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        object Data()
        {
            var text=File.ReadAllText(config);var physical=Report("physical-combat")??throw new InvalidDataException("Rebuild the physical combat report first.");
            return new{Settings=MagicConfiguration.Parse(text),Revision=Revision(text),ConfigurationFile=config,Creation=physical.GetProperty("CharacterCreation"),LevelMapping=BaldursGateStyleOblivion.Classification.ActorConfiguration.Load(actorConfig).LevelMapping,Distribution=Report("magic-gameplay")};
        }
        IResult Handle(Func<IResult> action)
        {
            try{lock(Gate)return action();}
            catch(Exception error) when(error is IOException or JsonException or ArgumentException or InvalidDataException or UnauthorizedAccessException){return Results.BadRequest(new{error=error.Message});}
        }
        app.MapGet("/magic",()=>Results.Content(EditorNavigation.ReadTemplate("magic.html").Replace("/*TOKEN*/\"\"",JsonSerializer.Serialize(token)),"text/html"));
        app.MapGet("/api/magic",()=>Handle(()=>Report("magic-analysis") is {} report?Results.Json(report):Results.NotFound(new{error="Run the patcher with Magic analysis enabled."})));
        app.MapGet("/api/magic/workbench",()=>Handle(()=>Results.Json(Data(),MagicConfiguration.Options)));
        app.MapPost("/api/magic/preview",(MagicPreview request)=>Handle(()=>
        {
            var physical=Report("physical-combat")??throw new InvalidDataException("Physical combat report missing.");
            var combatPath=Path.Combine(Path.GetDirectoryName(config)!,"combat.json");var combat=CombatConfiguration.Load(combatPath);
            var creation=physical.GetProperty("CharacterCreation").Deserialize<CreationCatalog>(CombatConfiguration.Options)!;
            var settings=physical.GetProperty("OriginalGameSettings").Deserialize<Dictionary<string,double>>()!;
            foreach(var pair in combat.Gameplay.GameSettings)settings[pair.Key]=pair.Value;
            return Results.Json(MagicScenarios.Run(request.Settings,combat,creation,settings,request.Scenario),MagicConfiguration.Options);
        }));
        app.MapPost("/api/magic/config",(MagicSave request)=>Handle(()=>
        {
            if(Revision(File.ReadAllText(config))!=request.Revision)return Results.Conflict(new{error="Magic settings changed. Reload before saving."});
            var settings=MagicConfiguration.Parse(request.Json);var combat=CombatConfiguration.Load(Path.Combine(Path.GetDirectoryName(config)!,"combat.json"));
            if(settings.Profiles.Values.Any(p=>!combat.Gameplay.ActorBuilds.ContainsKey(p.Build)))throw new ArgumentException("A caster profile references an unknown combat progression build.");
            var temporary=config+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,JsonSerializer.Serialize(settings,MagicConfiguration.Options)+Environment.NewLine);File.Move(temporary,config,true);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return Results.Json(Data(),MagicConfiguration.Options);
        }));
    }
}