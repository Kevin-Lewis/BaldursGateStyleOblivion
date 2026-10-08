using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Creation;
using BaldursGateStyleOblivion.Economy;
using BaldursGateStyleOblivion.Enhancements;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
namespace ActorResearch;
internal sealed record EconomyPreview(EconomySettings Settings,EconomyScenario Scenario,double CraftedValueMultiplier=.18,int IngredientFloor=8);
internal sealed record EconomySave(string Json,string Revision,string AlchemyRevision,double CraftedValueMultiplier,int IngredientFloor);
internal static class EconomyEditor
{
    private static readonly object Gate=EnhancementEditor.Gate;
    private static string Revision(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void Map(WebApplication app,string folder,string reports,string token)
    {
        var path=Path.Combine(folder,"economy.json");var alchemyPath=Path.Combine(folder,"alchemy.json");
        DateTime stamp=default;EconomyCatalog? cached=null;JsonElement report=default;Dictionary<string,uint> ingredientValues=new();
        EconomyCatalog Catalog()
        {
            var files=Directory.GetFiles(reports,"*.economy.json");if(files.Length!=1)throw new InvalidDataException("Rebuild the candidate to generate the economy report.");
            var modified=File.GetLastWriteTimeUtc(files[0]);if(cached is not null&&modified==stamp)return cached;
            using var d=JsonDocument.Parse(File.ReadAllText(files[0]));report=d.RootElement.Clone();cached=report.GetProperty("Catalog").Deserialize<EconomyCatalog>(EconomyBalance.Options)!;stamp=modified;
            var enhancements=Directory.GetFiles(reports,"*.enhancements.json");if(enhancements.Length==1){using var e=JsonDocument.Parse(File.ReadAllText(enhancements[0]));ingredientValues=e.RootElement.GetProperty("Ingredients").EnumerateArray().ToDictionary(i=>i.GetProperty("FormKey").GetString()!,i=>i.GetProperty("BeforeValue").GetUInt32());}
            return cached;
        }
        IResult Handle(Func<IResult> action){try{lock(Gate)return action();}catch(Exception e)when(e is IOException or JsonException or ArgumentException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException){return Results.BadRequest(new{error=e.Message});}}
        void Validate(EconomySettings s,double crafted,int floor)
        {
            EconomyBalance.Validate(s);EnhancementConfiguration.Range(crafted,.01,1,"Crafted potion value multiplier");EnhancementConfiguration.Range(floor,1,100,"Ingredient floor");var c=Catalog();
            if(s.ItemOverrides.Keys.Except(c.Items.Select(i=>i.Key)).Any())throw new ArgumentException("Unknown price override item.");
            if(s.MerchantOverrides.Keys.Except(c.Merchants.Select(m=>m.Key)).Any())throw new ArgumentException("Unknown merchant override.");
        }
        EconomyCatalog WithIngredients(EconomyCatalog c,EconomySettings s,int floor)
        {
            var a=EnhancementConfiguration.Load<AlchemySettings>(alchemyPath);
            return new(){Items=c.Items.Select(i=>i.Kind=="Ingredient"&&!i.Preserve?i with{Value=a.IngredientValues.GetValueOrDefault(i.Key,Math.Max(ingredientValues.GetValueOrDefault(i.Key,i.Value),(uint)floor))}:i).ToArray(),Pools=c.Pools,Sources=c.Sources,Merchants=c.Merchants,Spells=c.Spells,SpellSettings=c.SpellSettings};
        }
        object Data()
        {
            var text=File.ReadAllText(path);var s=EconomyBalance.Parse(text);var atext=File.ReadAllText(alchemyPath);var a=EnhancementConfiguration.Parse<AlchemySettings>(atext);var c=WithIngredients(Catalog(),s,a.CommonIngredientValueFloor);
            var combat=CombatConfiguration.Load(Path.Combine(folder,"combat.json"));var creationClasses=CreationEditor.Baseline(reports).Classes.Select(c=>c.Name).ToHashSet();var mapping=ActorConfiguration.Load(Path.Combine(folder,"actor-classification.json")).LevelMapping;
            return new{Settings=s,Revision=Revision(text),AlchemyRevision=Revision(atext),a.CraftedValueMultiplier,IngredientFloor=a.CommonIngredientValueFloor,c.Items,c.Merchants,c.Spells,c.SpellSettings,Prices=c.Items.Select(i=>EconomyBalance.Price(i,s)),Categories=c.Sources.Select(i=>i.Category).Distinct().Order(),Classes=combat.Gameplay.ActorBuilds.Keys.Where(k=>creationClasses.Contains(combat.Gameplay.ActorBuilds[k].CreationClass??k)),LevelMapping=mapping,ConfigurationFile=path,Report=new{Applied=report.GetProperty("Applied"),Notes=report.GetProperty("Notes"),EngineSettings=report.GetProperty("EngineSettings"),Pools=c.Pools.Length,Sources=c.Sources.Length,ScriptedGold=report.TryGetProperty("ScriptedGold",out var goldAudit)?goldAudit:JsonSerializer.SerializeToElement(Array.Empty<object>()),CoinEntries=report.GetProperty("CoinChanges").GetArrayLength(),Built=stamp}};
        }
        object Analyze(EconomyPreview request)
        {
            Validate(request.Settings,request.CraftedValueMultiplier,request.IngredientFloor);var c=WithIngredients(Catalog(),request.Settings,request.IngredientFloor);var combat=CombatConfiguration.Load(Path.Combine(folder,"combat.json"));var creation=CreationBalance.Apply(CreationEditor.Baseline(reports),CreationBalance.Load(Path.Combine(folder,"creation.json")));var scenario=request.Scenario;
            if(!combat.Gameplay.ActorBuilds.ContainsKey(scenario.Class))throw new ArgumentException("Choose a character class.");
            var race=creation.Races.First(r=>r.Name=="Imperial");var build=PlayerBuilds.AtLevel(combat.Gameplay,creation,scenario.Class,race.Key,false,null,scenario.Level);var stats=build.Stats;
            var profile=combat.QuickTiers.GetValueOrDefault(scenario.Tier)??throw new ArgumentException("Choose a tier from 0 to 10.");
            var light=combat.Gameplay.ActorBuilds[scenario.Class].PlayerFocusedSkills.Contains("LightArmor");var mage=creation.Classes.First(c=>c.Name==(combat.Gameplay.ActorBuilds[scenario.Class].CreationClass??scenario.Class)).Specialization=="Magic";
            EconomyItem[] Kit(string weapon,string armor,bool robes=false)
            {
                var eligible=c.Items.Where(i=>!i.Preserve&&!i.Unique&&!i.Enchanted).ToArray();var kit=new List<EconomyItem>();
                var sword=eligible.Where(i=>i.Kind=="Weapon"&&i.Class==(mage?"Dagger":"Longsword")&&i.Material==weapon).OrderBy(i=>i.Key.EndsWith(":Synthesis.esp")).ThenBy(i=>i.Value).ThenBy(i=>i.Key).FirstOrDefault();if(sword is not null)kit.Add(sword);
                if(robes){var robe=eligible.Where(i=>i.Kind=="Clothing"&&i.Slots.Contains("UpperBody")).OrderBy(i=>i.Value).FirstOrDefault();if(robe is not null)kit.Add(robe);}
                else foreach(var slot in new[]{"Hair","UpperBody","LowerBody","Hand","Foot","Shield"}){var item=eligible.Where(i=>i.Kind is "Armor" or "Shield"&&i.Material==armor&&i.Slots.Split(',',StringSplitOptions.TrimEntries).Contains(slot)).OrderBy(i=>i.Key.EndsWith(":Synthesis.esp")).ThenBy(i=>i.Value).ThenBy(i=>i.Key).FirstOrDefault();if(item is not null&&!kit.Contains(item))kit.Add(item);}
                return kit.ToArray();
            }
            var gear=Kit(profile.WeaponMaterial,light?profile.LightArmorMaterial:profile.ArmorMaterial,mage);var target=c.Items.FirstOrDefault(i=>i.Key==scenario.Target);var targetMaterial=scenario.Target.Replace(" kit","");
            if(target is null&&!request.Settings.Materials.ContainsKey(targetMaterial))throw new ArgumentException("Choose a purchase target.");
            var targetKit=target is null?Kit(targetMaterial,targetMaterial):[target];
            double Value(EconomyItem i,EconomySettings s)=>s.Enabled?EconomyBalance.Price(i,s).Value:i.Value;
            var carry=scenario.CarryWeight??Math.Max(0,stats["Strength"]*5-gear.Sum(i=>i.Weight)-40);var mercantile=stats.GetValueOrDefault("Mercantile");
            var sell=scenario.SellFactor??(mercantile>=100?1:Math.Min(.55,.22+mercantile*.002));var buy=scenario.BuyFactor??(mercantile>=100?1:Math.Max(1.4,2.3-mercantile*.006));
            EconomyResult Run(EconomySettings s,EconomyScenario sc)=>EconomyAnalysis.Run(s.Enabled?c:Catalog(),s,sc,carry,sell,buy,gear.Sum(i=>Value(i,s)),targetKit.Sum(i=>Value(i,s)),playerMercantile:mercantile);
            var current=EconomyBalance.Parse(JsonSerializer.Serialize(request.Settings,EconomyBalance.Options));current.Enabled=false;
            foreach(var row in report.GetProperty("EngineSettings").EnumerateArray()){var key=row.GetProperty("EditorID").GetString()!;if(row.GetProperty("Before").ValueKind==JsonValueKind.Number)current.GameSettings[key]=row.GetProperty("Before").GetDouble();else if(key=="fRepairCostMult")current.GameSettings[key]=.9;}
            var result=Run(request.Settings,scenario);var original=Run(current,scenario);
            var crafting=new[]{25,50,75,100}.Select(skill=>
            {
                var value=EnhancementBalance.CraftedValue(skill,stats["Luck"],skill<50?25:skill<75?38:skill<100?52:65,request.CraftedValueMultiplier);var ingredient=c.Items.Where(i=>i.Kind=="Ingredient"&&!i.Preserve).OrderBy(i=>Value(i,request.Settings)).FirstOrDefault();var input=ingredient is null?0:Value(ingredient,request.Settings);var inputs=skill==100?1:2;
                return new{Skill=skill,Value=value,Inputs=inputs,InputValue=input*inputs,BuyCost=input*inputs*buy,Proceeds=value*sell,PurchasedProfit=value*sell-input*inputs*buy,GatheredProfit=value*sell-input*inputs*sell,MasterPurchasedProfit=value-input*inputs};
            }).ToArray();
            var curve=new[]{1,2,3,4,5,6,7,8}.Select(tier=>
            {
                var sc=new EconomyScenario{Tier=tier,Level=scenario.Level,Category=scenario.Category,Outing=scenario.Outing,Merchant=scenario.Merchant,Condition=scenario.Condition,QuestGold=scenario.QuestGold,OtherExpenses=scenario.OtherExpenses,HealingPotions=scenario.HealingPotions,Poisons=scenario.Poisons,RepairWear=scenario.RepairWear,ChargeSpent=scenario.ChargeSpent};var r=Run(request.Settings,sc);return new{Tier=tier,r.MedianProfit,r.LowProfit,r.WindfallProfit,r.OutingsToPurchase,r.UnknownBranches};
            }).ToArray();
            var prices=c.Items.Select(i=>request.Settings.Enabled?EconomyBalance.Price(i,request.Settings):new EconomyPrice(i.Key,i.Value,i.Value,0,1,true,"Economy disabled.")).ToArray();
            return new{Current=original,Proposed=result,Curve=curve,Crafting=crafting,Prices=prices,Defaults=new{CarryWeight=carry,SellFactor=sell,BuyFactor=buy,Mercantile=mercantile,Stats=stats,Gear=gear.Select(i=>new{i.Name,i.Weight,Value=Value(i,request.Settings)}),Target=targetKit.Select(i=>new{i.Name,Value=Value(i,request.Settings)}),Hours=request.Settings.Outings[scenario.Outing].Hours},Warnings=prices.Where(p=>p.Value>10000||p.Enchantment>p.Equipment*4).Select(p=>new{Item=c.Items.First(i=>i.Key==p.Key).Name,p.Value,p.Reason}).Take(80)};
        }
        app.MapGet("/economy",()=>Results.Content(EditorNavigation.ReadTemplate("economy.html").Replace("/*TOKEN*/\"\"",JsonSerializer.Serialize(token)),"text/html"));
        app.MapGet("/api/economy",()=>Handle(()=>Results.Json(Data(),EconomyBalance.Options)));
        app.MapPost("/api/economy/preview",(EconomyPreview request)=>Handle(()=>Results.Json(Analyze(request),EconomyBalance.Options)));
        app.MapPost("/api/economy/config",(EconomySave request)=>Handle(()=>
        {
            var original=File.ReadAllText(path);var oldAlchemy=File.ReadAllText(alchemyPath);
            if(Revision(original)!=request.Revision||Revision(oldAlchemy)!=request.AlchemyRevision)return Results.Conflict(new{error="Economy or alchemy settings changed. Reload before saving."});
            var s=EconomyBalance.Parse(request.Json);Validate(s,request.CraftedValueMultiplier,request.IngredientFloor);var a=EnhancementConfiguration.Parse<AlchemySettings>(oldAlchemy);a.CraftedValueMultiplier=request.CraftedValueMultiplier;a.CommonIngredientValueFloor=request.IngredientFloor;EnhancementConfiguration.Validate(a);
            var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";var atemp=alchemyPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,JsonSerializer.Serialize(s,EconomyBalance.Options)+Environment.NewLine);File.WriteAllText(atemp,JsonSerializer.Serialize(a,EnhancementConfiguration.Options)+Environment.NewLine);File.Move(atemp,alchemyPath,true);try{File.Move(temp,path,true);}catch{File.WriteAllText(alchemyPath,oldAlchemy);throw;}}
            finally{if(File.Exists(temp))File.Delete(temp);if(File.Exists(atemp))File.Delete(atemp);}
            return Results.Json(Data(),EconomyBalance.Options);
        }));
    }
}


