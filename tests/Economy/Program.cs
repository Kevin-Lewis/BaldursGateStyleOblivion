using System.Text.Json;
using BaldursGateStyleOblivion.Economy;
using BaldursGateStyleOblivion.Enhancements;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
void Check(bool ok,string why){if(!ok)throw new Exception(why);}
var settings=new EconomySettings();EconomyBalance.Validate(settings);
EconomyItem Item(string key,string material="Iron",string kind="Weapon",double weight=10)=>new(key,key,key,kind,material,"Longsword","UpperBody",2,45,weight,13,kind=="Armor"?10:0,300,false,false,false,"",[]);
var iron=Item("iron");var steel=Item("steel","Steel");var daedric=Item("daedric","Daedric");
Check(EconomyBalance.Price(iron,settings).Value<EconomyBalance.Price(steel,settings).Value&&EconomyBalance.Price(steel,settings).Value<EconomyBalance.Price(daedric,settings).Value,"Scarcity price progression");
Check(EconomyBalance.Price(daedric with{Tier=10},settings).Value==EconomyBalance.Price(daedric with{Tier=0},settings).Value,"Ordinary gear does not gain exponential tier pricing");
Check(EconomyBalance.Price(iron with{Unique=true,Tier=10},settings).Value<=EconomyBalance.Price(iron,settings).Value*1.7,"Bounded unique prestige");
Check(EconomyBalance.Price(iron with{Preserve=true},settings).Value==45,"Protected item preserved");
Check(EconomyBalance.Price(iron with{Kind="Currency",Value=1},settings).Value==1,"Currency value remains one");
try{EconomyBalance.Parse("{\"EnchantmentDamageValue\":1000}");throw new Exception("Invalid price accepted");}catch(ArgumentException){}
try{EconomyBalance.Parse("{\"UnknownSetting\":5}");throw new Exception("Typo accepted");}catch(JsonException){}
var coin=Item("coin",kind:"Currency",weight:0) with{Value=1};var catalog=new EconomyCatalog{Items=[iron,steel,coin],Pools=[new("pool",0,true,false,[new("iron",1),new("coin",20,ScaleCoins:true)]),new("empty",1,false,false,[new("steel",1)])],Sources=[new("source","Test source",2,"Bandit","ActorLoot",[new("pool",1),new("empty",1)])],Merchants=[new("shop","Shop","General Store",600,35,["Weapon"])]};
var scenario=new EconomyScenario{Merchant="shop",Condition=1,HealingPotions=0,Poisons=0,ChargeSpent=0,RepairWear=0};settings.Outings["Short outing"].Enemies=1;settings.Outings["Short outing"].Containers=0;
var result=EconomyAnalysis.Run(catalog,settings,scenario,100,.25,2,0,100);
Check(result.MedianProfit==EconomyBalance.Price(iron,settings).Value*.25+15,"Counts, UseAll, guaranteed empty chance and generated coin scaling");
Check(result.Samples.SequenceEqual(EconomyAnalysis.Run(catalog,settings,scenario,100,.25,2,0,100).Samples),"Reproducible sampling");
Check(EconomyAnalysis.Run(catalog,settings,scenario,0,.25,2,0,100).MedianProfit==15,"Carry capacity excludes physical goods but not coins");
catalog.Items[0]=iron with{Lootable=false};Check(EconomyAnalysis.Run(catalog,settings,scenario,100,.25,2,0,100).MedianProfit==15,"Nonplayable gear is not saleable loot");catalog.Items[0]=iron;
settings.MerchantOverrides["shop"]=new(){Gold=1,Mercantile=35};Check(EconomyAnalysis.Run(catalog,settings,scenario,100,.25,2,0,100).MedianProfit==16,"Merchant limit applies to each item");settings.MerchantOverrides.Clear();
Check(EconomyAnalysis.Run(catalog,settings,new(){Merchant="shop",HealingPotions=0,Poisons=0,RepairWear=0,ChargeSpent=100},100,.25,2,0,100).MedianExpenses==10,"Recharge costs use native setting");
var alchemy=EnhancementConfiguration.Load<AlchemySettings>("BaldursGateStyleOblivion/alchemy.json");var crafted=EnhancementBalance.CraftedValue(100,50,65,alchemy.CraftedValueMultiplier);
Check(crafted*.25<alchemy.CommonIngredientValueFloor*2,"Purchased single-ingredient master recipe loses money under ordinary bargaining");Check(crafted>alchemy.CommonIngredientValueFloor*2,"Gathered expert/master crafting can add value");
if(args.Length==2&&args[0]=="--candidate")
{
    using var patch=OblivionMod.CreateFromBinaryOverlay(args[1],OblivionRelease.Oblivion);using var d=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1])!,"Reports","BaldursGateStyleOblivion.economy.json")));var root=d.RootElement;var c=root.GetProperty("Catalog").Deserialize<EconomyCatalog>()!;var s=root.GetProperty("Settings").Deserialize<EconomySettings>()!;
    var native=patch.EnumerateMajorRecords().ToDictionary(i=>i.FormKey);var values=root.GetProperty("Prices").EnumerateArray().Select(p=>p.Deserialize<EconomyPrice>()!).ToDictionary(p=>p.Key);FormKey Key(string key){var k=FormKey.Factory(key);return k.ModKey.Name=="Synthesis"?new(patch.ModKey,k.ID):k;}
    uint? Value(IMajorRecordGetter r)=>r switch{IWeaponGetter i=>i.Data?.Value,IArmorGetter i=>i.Data?.Value,IClothingGetter i=>i.Data?.Value,IPotionGetter i=>i.Data?.Value,IIngredientGetter i=>i.Data?.Value,IAlchemicalApparatusGetter i=>i.Data?.Value,ISoulGemGetter i=>i.Data?.Value,ISigilStoneGetter i=>i.Data?.Value,IAmmunitionGetter i=>i.Data?.Value,IBookGetter i=>(uint?)i.Data?.Value,IMiscellaneousGetter i=>(uint?)i.Data?.Value,_=>null};
    var count=0;
    foreach(var item in c.Items)
    {
        var price=values[item.Key];if(native.TryGetValue(Key(item.Key),out var record)){Check(Value(record)==price.Value,"Native price: "+item.Key);count++;}
        else Check(price.Value==item.Value,"Missing price write: "+item.Key);
        if(price.Preserved)Check(price.Value==item.Value,"Preserved value: "+item.Key);
        if(record is IWeaponGetter w){Check(w.Data!.Damage==item.Damage&&w.Data.Health==item.Durability&&Math.Abs(w.Data.Weight-item.Weight)<.0001,"Economy leaves physical weapon power intact: "+item.Key);}
        if(record is IArmorGetter a)Check(Math.Abs(a.Data!.ArmorValue-item.Armor)<.001&&a.Data.Health==item.Durability,"Economy leaves armor power intact: "+item.Key+" native="+a.Data!.ArmorValue+" expected="+item.Armor+" health="+a.Data.Health+"/"+item.Durability);
        if(record is IPotionGetter p&&!price.Preserved)Check(p.Data!.Flags.HasFlag(IngredientFlag.ManualValue),"Native potion manual price");
    }
    foreach(var row in root.GetProperty("CoinChanges").EnumerateArray()){var owner=native[Key(row.GetProperty("Owner").GetString()!)];var index=row.GetProperty("Index").GetInt32();var actual=owner is ILeveledItemGetter l?(int)(l.Entries[index].Count??1):(int)((IContainerGetter)owner).Items[index].Count;Check(actual==row.GetProperty("After").GetInt32(),"Native generated coin counts match each original entry once");}
    foreach(var merchant in c.Merchants){var policy=s.MerchantOverrides.GetValueOrDefault(merchant.Key)??s.Merchants.GetValueOrDefault(merchant.Profile);if(policy is null)continue;var npc=(INpcGetter)native[Key(merchant.Key)];Check(npc.Configuration!.BarterGold==policy.Gold,"Merchant gold profile");Check(npc.Stats!.Mercantile==Math.Max(merchant.Mercantile,policy.Mercantile),"Merchant skill/trainer floor preserved");}
    foreach(var row in root.GetProperty("EngineSettings").EnumerateArray()){var id=row.GetProperty("EditorID").GetString();var g=patch.GameSettings.FirstOrDefault(g=>g.EditorID==id);if(g is null){Check(row.GetProperty("Before").ValueKind==JsonValueKind.Number&&Math.Abs(row.GetProperty("Before").GetDouble()-row.GetProperty("After").GetDouble())<.0001,"Unchanged setting omitted only when identical");continue;}var value=g is IGameSettingFloatGetter f?f.Data:((IGameSettingIntGetter)g).Data;Check(Math.Abs(value.GetValueOrDefault()-row.GetProperty("After").GetDouble())<.0001,"Native engine settings");}
    Check(!c.Sources.Any(i=>i.Name.StartsWith("Test",StringComparison.OrdinalIgnoreCase)),"Test sources excluded");
    Console.WriteLine($"Candidate economy checks passed: {count} native prices, {c.Merchants.Length} merchants, {root.GetProperty("CoinChanges").GetArrayLength()} coin entries, engine settings, unchanged equipment power.");
}
Console.WriteLine("Economy checks passed: scarcity, bounded prestige, protections, repeatable loot, capacity, merchant limits, service costs and crafting margins.");







