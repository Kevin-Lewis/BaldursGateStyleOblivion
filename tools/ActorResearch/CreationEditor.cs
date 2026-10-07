using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Creation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
namespace ActorResearch;
internal sealed record CreationSave(string Json,string Revision);
internal sealed record CreationPreview(CreationSettings Settings,string Race,string Class,string? Birthsign,bool Female=false);
internal static class CreationEditor
{
    private static readonly object Gate=new();
    public static CreationCatalog Baseline(string reports)
    {
        var files=Directory.GetFiles(reports,"*.character-creation.json");if(files.Length!=1)throw new InvalidDataException("Rebuild the patch to generate the character creation report.");
        using var d=JsonDocument.Parse(File.ReadAllText(files[0]));return d.RootElement.GetProperty("Baseline").Deserialize<CreationCatalog>(CreationBalance.Options)!;
    }
    public static JsonElement PatchCatalog(JsonElement physical,string folder,string reports)
    {
        if(!File.Exists(Path.Combine(folder,"creation.json"))||Directory.GetFiles(reports,"*.character-creation.json").Length!=1)return physical;
        var node=JsonNode.Parse(physical.GetRawText())!;node["CharacterCreation"]=JsonSerializer.SerializeToNode(CreationBalance.Apply(Baseline(reports),CreationBalance.Load(Path.Combine(folder,"creation.json"))),CreationBalance.Options);
        return JsonSerializer.SerializeToElement(node);
    }
    public static void Map(WebApplication app,string folder,string reports,string token)
    {
        var path=Path.Combine(folder,"creation.json");string Revision(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        IResult Handle(Func<IResult> action){try{lock(Gate)return action();}catch(Exception e)when(e is IOException or ArgumentException or InvalidDataException or JsonException or UnauthorizedAccessException){return Results.BadRequest(new{error=e.Message});}}
        app.MapGet("/creation",()=>Results.Content(EditorNavigation.ReadTemplate("creation.html").Replace("/*TOKEN*/\"\"",JsonSerializer.Serialize(token)),"text/html"));
        app.MapGet("/api/creation",()=>Handle(()=>{var text=File.ReadAllText(path);var settings=CreationBalance.Parse(text);var baseline=Baseline(reports);return Results.Json(new{Settings=settings,Revision=Revision(text),Baseline=baseline,Proposed=CreationBalance.Apply(baseline,settings)},CreationBalance.Options);}));
        app.MapPost("/api/creation/config",(CreationSave request)=>Handle(()=>
        {
            if(Revision(File.ReadAllText(path))!=request.Revision)return Results.Conflict(new{error="Character creation settings changed. Reload before saving."});
            var settings=CreationBalance.Parse(request.Json);CreationBalance.Validate(settings,Baseline(reports));var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,JsonSerializer.Serialize(settings,CreationBalance.Options)+Environment.NewLine);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
            return Results.Json(new{Revision=Revision(File.ReadAllText(path))},CreationBalance.Options);
        }));
        app.MapPost("/api/creation/preview",(CreationPreview request)=>Handle(()=>
        {
            var baseline=Baseline(reports);var proposed=CreationBalance.Apply(baseline,request.Settings);var combat=CombatConfiguration.Load(Path.Combine(folder,"combat.json"));
            var cls=proposed.Classes.SingleOrDefault(c=>c.Name==request.Class)??throw new ArgumentException("Choose a playable class.");
            var build=combat.Gameplay.ActorBuilds.FirstOrDefault(p=>(p.Value.CreationClass??p.Key)==cls.Name).Key??(cls.Specialization=="Magic"?"Mage":cls.Specialization=="Stealth"?"Thief":"Warrior");
            if(!combat.Gameplay.ActorBuilds.ContainsKey(build))build="Warrior";
            object Metrics(CreationCatalog catalog,int level)
            {
                var result=PlayerBuilds.AtLevel(combat.Gameplay,catalog,build,request.Race,request.Female,request.Birthsign,level,creationClass:request.Class);var stats=result.Stats;
                var gs=catalog.Settings.ToDictionary();foreach(var p in combat.Gameplay.GameSettings)gs[p.Key]=p.Value;
                var pool=stats["Intelligence"]*(1+gs.GetValueOrDefault("fPCBaseMagickaMult",1))+stats.GetValueOrDefault("Magicka");var regen=stats.GetValueOrDefault("StuntedMagicka")>0?0:pool*(gs.GetValueOrDefault("fMagickaReturnBase",.75)+stats["Willpower"]*gs.GetValueOrDefault("fMagickaReturnMult",.02))*.01;
                return new{Stats=stats,Magicka=pool,Regeneration=regen,FireTaken=CreationBalance.IncomingMagic(stats,"Fire"),FrostTaken=CreationBalance.IncomingMagic(stats,"Frost"),ShockTaken=CreationBalance.IncomingMagic(stats,"Shock")};
            }
            return Results.Json(new{Catalog=proposed,Build=build,Rows=new[]{1,10,20,30}.Select(level=>new{Level=level,Current=Metrics(baseline,level),Proposed=Metrics(proposed,level)}),Notes=new[]{"Current means loaded records before this creation pass; both columns use the current combat progression settings.","Incoming spell percentages include passive resistance, vulnerability and average absorption chance. Absorption is probabilistic; regained magicka, reflection, active powers, equipment and potion effects are not simulated.","Class skill estimates depend on the selected progression profile; actual skill use can differ."}},CreationBalance.Options);
        }));
    }
}