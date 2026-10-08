using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaldursGateStyleOblivion.Enhancements;
using BaldursGateStyleOblivion.Combat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ActorResearch;
internal sealed record EnhancementSave(string Kind,string Json,string Revision);
internal sealed record EnhancementPreview(EnchantmentSettings Enchantments,AlchemySettings Alchemy);
internal sealed record RecipePreview(AlchemySettings Settings,string First,string? Second,double Skill=50,double Luck=50,double MortarQuality=38,double BuyMultiplier=1.4,double SellMultiplier=.4,double? ObservedValue=null);
internal static class EnhancementEditor
{
    internal static readonly object Gate=new();
    public static void Map(WebApplication app,string folder,string reports,string token)
    {
        string PathFor(string kind)=>Path.Combine(folder,kind+".json");
        string Revision(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        JsonElement Report(string name){var files=Directory.GetFiles(reports,"*."+name+".json");if(files.Length!=1)throw new InvalidDataException("Rebuild the candidate to create the "+name+" report.");using var d=JsonDocument.Parse(File.ReadAllText(files[0]));return d.RootElement.Clone();}
        IResult Handle(Func<IResult> action){try{lock(Gate)return action();}catch(Exception e)when(e is IOException or JsonException or ArgumentException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException){return Results.BadRequest(new{error=e.Message});}}
        object Data()
        {
            var e=File.ReadAllText(PathFor("enchantments"));var a=File.ReadAllText(PathFor("alchemy"));var physical=Report("physical-combat");var combat=CombatConfiguration.Load(Path.Combine(folder,"combat.json"));
            var standard=physical.GetProperty("Items").Deserialize<PhysicalItem[]>(CombatConfiguration.Options)!.Where(i=>!i.Enchanted&&!i.Protected&&i.Material!="Unknown").Select(i=>PhysicalBalance.Propose(i,combat)).ToArray();
            return new{Enchantments=EnhancementConfiguration.Parse<EnchantmentSettings>(e),Alchemy=EnhancementConfiguration.Parse<AlchemySettings>(a),EnchantmentRevision=Revision(e),AlchemyRevision=Revision(a),Report=Report("enhancements"),Standards=standard.Concat(Report("enhancements").GetProperty("StandardClothing").Deserialize<PhysicalItem[]>()!)};
        }
        foreach(var route in new[]{"/artifacts","/enchantments","/alchemy"})app.MapGet(route,()=>Results.Content(EditorNavigation.ReadTemplate("enhancements.html").Replace("/*TOKEN*/\"\"",JsonSerializer.Serialize(token)),"text/html"));
        app.MapGet("/api/enhancements",()=>Handle(()=>Results.Json(Data(),EnhancementConfiguration.Options)));
        void Validate(EnchantmentSettings e,AlchemySettings a)
        {
            EnhancementConfiguration.Validate(e);EnhancementConfiguration.Validate(a);var report=Report("enhancements");var codes=report.GetProperty("EffectDefinitions").EnumerateArray().Select(r=>r.GetProperty("Code").GetString()!).ToHashSet();
            if(e.ExcludedCustomEffects.Concat(e.CustomConstantCaps.Keys).Concat(a.IngredientReplacements.Keys).Concat(a.IngredientReplacements.Values).Any(c=>!codes.Contains(c)))throw new ArgumentException("Unknown native magic effect code.");
            var rows=report.GetProperty("Items").EnumerateArray().ToDictionary(r=>r.GetProperty("FormKey").GetString()!);var combat=CombatConfiguration.Load(Path.Combine(folder,"combat.json"));
            foreach(var pair in e.Artifacts)
            {
                if(!rows.TryGetValue(pair.Key,out var row)){if(pair.Value.Preserve)continue;throw new ArgumentException("Artifact not found in equipment catalogue: "+pair.Key);}
                if(!row.GetProperty("Artifact").GetBoolean())throw new ArgumentException("Artifact not found in catalogue: "+pair.Key);
                if(pair.Value.Class is not null&&!combat.Gameplay.WeaponBaselines.ContainsKey(pair.Value.Class))throw new ArgumentException("Unknown artifact weapon class.");
                if(pair.Value.NormalizePhysical&&!combat.Materials.ContainsKey(pair.Value.Material))throw new ArgumentException("Unknown artifact comparison material.");
                if(pair.Value.Effects is not {} effects)continue;var before=row.GetProperty("Before").Deserialize<EnhancementEffect[]>()!;
                if(before.Length!=effects.Length||before.Zip(effects).Any(p=>p.First.Code!=p.Second.Code||p.First.ActorValue!=p.Second.ActorValue||p.First.Scripted&&p.First!=p.Second))throw new ArgumentException("Preserve native artifact effect identities and scripts.");
            }
        }
        app.MapPost("/api/enhancements/config",(EnhancementSave request)=>Handle(()=>
        {
            if(request.Kind is not "enchantments" and not "alchemy")throw new ArgumentException("Unknown configuration.");var path=PathFor(request.Kind);if(Revision(File.ReadAllText(path))!=request.Revision)return Results.Conflict(new{error="Configuration changed. Reload before saving."});
            var e=request.Kind=="enchantments"?EnhancementConfiguration.Parse<EnchantmentSettings>(request.Json):EnhancementConfiguration.Load<EnchantmentSettings>(PathFor("enchantments"));var a=request.Kind=="alchemy"?EnhancementConfiguration.Parse<AlchemySettings>(request.Json):EnhancementConfiguration.Load<AlchemySettings>(PathFor("alchemy"));Validate(e,a);
            var text=request.Kind=="enchantments"?JsonSerializer.Serialize(e,EnhancementConfiguration.Options):JsonSerializer.Serialize(a,EnhancementConfiguration.Options);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temporary,text+Environment.NewLine);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}return Results.Json(Data(),EnhancementConfiguration.Options);
        }));
        app.MapPost("/api/enhancements/preview",(EnhancementPreview request)=>Handle(()=>
        {
            Validate(request.Enchantments,request.Alchemy);var report=Report("enhancements");var combat=CombatConfiguration.Load(Path.Combine(folder,"combat.json"));
            var items=report.GetProperty("Items").EnumerateArray().Select(row=>
            {
                var key=row.GetProperty("FormKey").GetString()!;request.Enchantments.Artifacts.TryGetValue(key,out var rule);var original=row.GetProperty("Before").Deserialize<EnhancementEffect[]>()!;var registered=row.GetProperty("Artifact").GetBoolean();var scripted=row.GetProperty("Scripted").GetBoolean();
                var preserve=rule?.Preserve==true||request.Enchantments.PreserveItems.Contains(key)||registered&&rule is null||scripted&&rule is null||original.Any(e=>e.Scripted)&&rule?.BalanceScriptedEquipment!=true;
                var tier=rule?.Tier??request.Enchantments.ItemTiers.GetValueOrDefault(key,row.GetProperty("Tier").GetInt32());var constant=row.GetProperty("Activation").GetString()=="Apparel";
                var effects=preserve?original:EnhancementBalance.Enchant(rule?.Effects??original,request.Enchantments,tier,row.GetProperty("Slots").GetString()!,constant,rule?.EnchantmentPower??1,row.GetProperty("Activation").GetString()=="Staff",rule?.BalanceScriptedEquipment==true);
                var physical=!row.TryGetProperty("BeforePhysical",out var physicalElement)||physicalElement.ValueKind==JsonValueKind.Null?null:physicalElement.Deserialize<PhysicalItem>();
                if(!preserve&&rule is not null&&physical is not null)physical=EnhancementBalance.ArtifactPhysical(physical,combat,rule);
                var desired=rule?.ChargedHits??request.Enchantments.ChargedHits;var capacity=rule?.ChargeCapacity??(row.TryGetProperty("Charge",out var charge)?charge.GetUInt32():700u);
                var cost=preserve&&row.TryGetProperty("BeforeCost",out var beforeCost)?beforeCost.GetUInt32():(uint)Math.Max(1,Math.Ceiling(Math.Max(100u,capacity)/(double)desired));
                return new{FormKey=key,Effects=effects,Physical=physical,Tier=tier,Preserved=preserve,Damage=EnhancementBalance.Damage(effects),ChargedHits=constant||row.GetProperty("Activation").GetString()=="None"?0:(int)(Math.Max(100u,capacity)/Math.Max(1u,cost)),Warnings=EnhancementBalance.Warnings(effects,constant)};
            }).ToArray();
            var potions=report.GetProperty("Potions").EnumerateArray().Select(row=>
            {
                var key=row.GetProperty("FormKey").GetString()!;var rank=request.Alchemy.PotionRanks.GetValueOrDefault(key,row.GetProperty("Rank").GetInt32());var preserve=row.GetProperty("Preserved").GetBoolean()||request.Alchemy.PreserveItems.Contains(key);var before=row.GetProperty("Before").Deserialize<EnhancementEffect[]>()!;var effects=preserve?before:EnhancementBalance.Potion(before,request.Alchemy,rank);
                return new{FormKey=key,Effects=effects,Rank=rank,Damage=EnhancementBalance.Damage(effects),Healing=EnhancementBalance.Recovery(effects),Value=preserve?row.GetProperty("BeforeValue").GetUInt32():request.Alchemy.PotionValues.GetValueOrDefault(key,EnhancementBalance.PotionValue(effects,rank))};
            }).ToArray();
            var sigils=report.GetProperty("SigilStones").EnumerateArray().Select(row=>
            {
                var key=row.GetProperty("FormKey").GetString()!;var tier=request.Enchantments.ItemTiers.GetValueOrDefault(key,row.GetProperty("Tier").GetInt32());var before=row.GetProperty("Before").Deserialize<EnhancementEffect[]>()!;
                return new{FormKey=key,Tier=tier,Effects=row.GetProperty("Preserved").GetBoolean()?before:EnhancementBalance.Sigil(before,request.Enchantments,tier,row.GetProperty("ArmorIndices").Deserialize<int[]>()!)};
            }).ToArray();return Results.Json(new{Items=items,Potions=potions,SigilStones=sigils},EnhancementConfiguration.Options);
        }));
        app.MapPost("/api/alchemy/recipe",(RecipePreview request)=>Handle(()=>
        {
            EnhancementConfiguration.Validate(request.Settings);EnhancementConfiguration.Range(request.BuyMultiplier,.1,5,"Purchase factor");EnhancementConfiguration.Range(request.SellMultiplier,.01,1,"Sale factor");if(request.ObservedValue.HasValue)EnhancementConfiguration.Range(request.ObservedValue.Value,0,10000,"Observed crafted value");
            var ingredients=Report("enhancements").GetProperty("Ingredients").EnumerateArray().ToDictionary(i=>i.GetProperty("FormKey").GetString()!);var first=ingredients[request.First];JsonElement? second=request.Second is null?null:ingredients[request.Second];if(second is null&&request.Skill<100)throw new ArgumentException("One-ingredient recipes require mastery.");if(request.First==request.Second)throw new ArgumentException("Choose different ingredients.");
            var count=request.Skill<25?1:request.Skill<50?2:request.Skill<75?3:4;
            EnhancementEffect[] Effects(JsonElement row)=>row.GetProperty("Before").Deserialize<EnhancementEffect[]>()!.Select(e=>row.GetProperty("Preserved").GetBoolean()?e:request.Settings.IngredientReplacements.TryGetValue(e.Code,out var code)?e with{Code=code}:e).Take(count).ToArray();
            var visible=Effects(first);var matching=second is null?visible.Take(1).ToArray():visible.Where(e=>Effects(second.Value).Any(s=>s.Code==e.Code&&s.ActorValue==e.ActorValue)).ToArray();
            double Value(JsonElement row)=>request.Settings.IngredientValues.GetValueOrDefault(row.GetProperty("FormKey").GetString()!,row.GetProperty("Preserved").GetBoolean()?row.GetProperty("BeforeValue").GetUInt32():Math.Max(row.GetProperty("BeforeValue").GetUInt32(),(uint)request.Settings.CommonIngredientValueFloor));
            var value=EnhancementBalance.CraftedValue(request.Skill,request.Luck,request.MortarQuality,request.Settings.CraftedValueMultiplier);var original=EnhancementBalance.CraftedValue(request.Skill,request.Luck,request.MortarQuality,.45);var cost=(Value(first)+(second is null?0:Value(second.Value)))*request.BuyMultiplier;var proceeds=(request.ObservedValue??value)*request.SellMultiplier;
            return Results.Json(new{Effects=matching.Select(e=>new{e.Code,e.ActorValue}),Craftable=matching.Length>0,CraftedValue=value,VanillaValue=original,PurchaseCost=cost,SaleProceeds=matching.Length>0?proceeds:0,PurchasedProfit=matching.Length>0?proceeds-cost:0,GatheredProceeds=matching.Length>0?proceeds:0,Notes=new[]{"Value is a native-formula estimate; use observed value to calibrate rounding or cached crafted records.","Effect access uses base skill mastery. Crafted effect magnitude/duration require native calibration and are not inferred from ingredient magnitudes.","Barter factors are explicit assumptions; merchant gold and stock limit actual turnover."}},EnhancementConfiguration.Options);
        }));
    }
}
