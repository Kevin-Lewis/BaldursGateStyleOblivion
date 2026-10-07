using BaldursGateStyleOblivion.Magic;
using BaldursGateStyleOblivion.Combat;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

var settings=new Dictionary<string,double>();
SpellEffectInfo Effect(string code,double magnitude,double duration=0,string range="Touch",double area=0)=>new(code,code,"Destruction",MagicAnalysis.Family(code),magnitude,duration,area,range,"None",7.5,code is "PARA" or "ZCLA",false,false,true,null);
SpellAssessment Spell(string key,SpellEffectInfo effect,bool manual=false,double cost=0)=>MagicAnalysis.Assess(new(key,key,key,"test","Spell","Novice",0,manual,cost,"00",[effect],[],[],false),settings);
void Check(bool condition,string message){if(!condition)throw new Exception(message);}
var instant=Spell("instant",Effect("FIDG",10));
Check(instant.Damage==10,"Instant damage");
Check(Spell("dot",Effect("FIDG",10,3)).Damage==30,"Damage over time");
Check(Spell("heal",Effect("REHE",8)).Healing==8,"Instant healing");
Check(Spell("drain",Effect("DRHE",100,2)).Damage==0,"Drain is not permanent damage");
Check(Spell("summon",Effect("ZCLA",0,20)).SummonSeconds==20,"Summon duration");
Check(Spell("self",Effect("FIDG",10,0,"Self")).Damage==0,"Self damage excluded from offense");
Check(Math.Abs(Spell("target",Effect("FIDG",10,0,"Target")).BaseCost!.Value/instant.BaseCost!.Value-1.5)<.0001,"Target cost multiplier");
Check(Spell("area",Effect("FIDG",10,0,"Touch",20)).BaseCost==instant.BaseCost*3,"Area cost");
Check(MagicAnalysis.Cast(instant,100,100,2,settings).Cost<MagicAnalysis.Cast(instant,25,100,2,settings).Cost,"Skill reduces cost");
Check(MagicAnalysis.Cast(Spell("free",Effect("FIDG",10),true,0),100,100,2,settings).CastsFromPool is null,"Zero cost does not produce infinite numeric estimates");
var weak=Spell("weak",Effect("FIDG",5),true,100);var strong=Spell("strong",Effect("FIDG",10),true,50);
MagicAnalysis.FindOutliers([weak,strong]);Check(weak.Outliers.Any(s=>s.Contains("dominated")),"Comparable dominated spell detection");
var scripted=Spell("script",Effect("SEFF",0));Check(scripted.CalculatedBaseCost is null,"Script costs remain unknown");
var magic=MagicConfiguration.Load("BaldursGateStyleOblivion/magic.json");
var allSkills=CasterKits.Schools.ToDictionary(s=>s,s=>100d);
var normalKit=CasterKits.Select(magic,"Conjurer",allSkills,200,9,settings);
Check(normalKit.All(k=>k.Spell.MinimumTier==0),"Exceptional spells require explicit access");
Check(CasterKits.Select(magic,"Conjurer",allSkills,200,9,settings,true).Any(k=>k.Spell.MinimumTier==8),"Exceptional access selects rare rank");
Check(CasterKits.Select(magic,"Conjurer",allSkills,200,7,settings,true).All(k=>k.Spell.MinimumTier==0),"Rare tier access still enforced");
Check(CasterKits.Select(magic,"Conjurer",allSkills,1,9,settings).Length==0,"Unaffordable kits excluded");
Check(CasterKits.Profile(magic,"Mage","NecromancerClass")=="Necromancer","Necromancer identity beats generic Mage");
Check(CasterKits.Profile(magic,"Warrior","Warrior")==null,"Ordinary fighters do not gain caster kits");
var combat=CombatConfiguration.Load("BaldursGateStyleOblivion/combat.json");
using var physicalDocument=JsonDocument.Parse(File.ReadAllText("artifacts/phase0-data/Reports/BaldursGateStyleOblivion.physical-combat.json"));
var creation=physicalDocument.RootElement.GetProperty("CharacterCreation").Deserialize<CreationCatalog>()!;
var imperial=creation.Races.First(r=>r.Name=="Imperial");
foreach(var name in new[]{"Conjurer","Necromancer","Illusionist","DefensiveCaster"})
{
    var player=PlayerBuilds.AtLevel(combat.Gameplay,creation,name,imperial.Key,false,null,20).Stats;
    var npc=CombatBuilds.ActorAtLevel(combat.Gameplay,creation,name,imperial.Key,false,5,20,new Dictionary<int,int?>());
    var primary=combat.Gameplay.ActorBuilds[name].PrimarySkill!;
    Check(player[primary]==100&&npc[primary]==100,"Primary school mastery and player/NPC parity: "+name);
}
Check(CasterKits.Profile(magic,"Illusionist","Mage")=="Illusionist","Assigned controller profile stays consistent with actor stats");
var atronach=creation.Birthsigns.First(s=>s.Name.Contains("Atronach"));
var signed=MagicScenarios.Run(magic,combat,creation,settings,new(){Profile="Destruction specialist",Race=imperial.Key,Birthsign=atronach.Key,Level=1,Tier=0});
Check(signed.MagickaRegen==0&&signed.MagickaPool==240,"Atronach extra magicka and stunted regeneration");
var sorcerer=PlayerBuilds.AtLevel(combat.Gameplay,creation,"Conjurer",imperial.Key,false,null,20,creationClass:"Sorcerer").Stats;
var actorSorcerer=CombatBuilds.ActorAtLevel(combat.Gameplay,creation,"Conjurer",imperial.Key,false,5,20,new Dictionary<int,int?>(),"Sorcerer");
Check(sorcerer["Conjuration"]==actorSorcerer["Conjuration"]&&sorcerer["HeavyArmor"]==actorSorcerer["HeavyArmor"],"Playable native Sorcerer class skill distribution retained");
var preview=MagicScenarios.Run(magic,combat,creation,settings,new(){Profile="Destruction specialist",Race=imperial.Key,Level=20,Tier=5,OpponentLevel=20,OpponentTier=5});
Check(preview.Timeline.All(p=>p.Magicka>=0&&p.CasterHealth>=0&&p.WarriorHealth>=0),"Scenario resources stay bounded");
Check(preview.Kit.Any(k=>k.Spell.Metric=="Damage"&&k.Spell.RequiredSkill==100),"Level 20 specialist has mastery attack");
Check(preview.AttackBurstCasts>=4&&preview.AttackBurstCasts<=8,"Master reserve supports finite useful burst");
Check(preview.PoolRecoverySeconds>30&&preview.PoolRecoverySeconds<80,"Recovery remains measured in tens of seconds");
Check(preview.SustainedDamageCeiling<preview.SpellDamage/magic.CastInterval*.75,"Recovery cannot sustain full-rate master casting");
var early=MagicScenarios.Run(magic,combat,creation,settings,new(){Profile="Destruction specialist",Race=imperial.Key,Level=1,Tier=0,OpponentLevel=1,OpponentTier=0});
Check(early.PoolDamage>=early.WarriorHealth&&early.AttackBurstCasts>=5,"Early reserve can cover a reasonable fight before defense or misses");
using(var vanilla=OblivionMod.CreateFromBinaryOverlay("F:/SteamLibrary/steamapps/common/Oblivion/Data/Oblivion.esm",OblivionRelease.Oblivion))
{
    var template=vanilla.Spells.First(s=>s.Data?.Type.ToString()=="Spell"&&s.Effects.Any(e=>e.Data?.MagicEffect.ToString()=="FIDG"&&e.ScriptEffect is null));
    var originalCost=template.Data!.Cost;var originalEffects=template.Effects.Count;
    var patch=new OblivionMod(ModKey.FromNameAndExtension("MagicTests.esp"),OblivionRelease.Oblivion);
    var entry=CasterKits.Spells(magic).First(s=>s.Slot=="Fire attack"&&s.Rank=="Master");
    var generated=MagicBalanceModule.Create(patch,template,entry);
    Check(generated.FormKey.ID>=0xF00000,"Representative spell ID uses reserved stable range");
    var alternatePatch=new OblivionMod(ModKey.FromNameAndExtension("MagicTests.esp"),OblivionRelease.Oblivion);
    Check(MagicBalanceModule.Create(alternatePatch,template,entry).FormKey==generated.FormKey,"Representative spell ID is stable across runs");
    Check(generated.Effects.Count==1&&generated.Effects[0].Data!.Magnitude==100&&generated.Data!.Cost==160,"Native representative spell generation");
    Check(generated.Data!.Level==Mutagen.Bethesda.Oblivion.Spell.SpellLevel.Master&&generated.Data.Flag==Mutagen.Bethesda.Oblivion.Spell.SpellFlag.ManualSpellCost,"Native mastery and flags");
    Check(template.Data.Cost==originalCost&&template.Effects.Count==originalEffects,"Native template unchanged");
    var native=vanilla.EnumerateMajorRecords().ToDictionary(r=>r.FormKey);
    var ability=vanilla.Spells.First(s=>s.Data?.Type.ToString()=="Ability");
    Check(!MagicBalanceModule.Replaceable(ability.FormKey,native),"Racial/other abilities retained");
    var script=vanilla.Spells.First(s=>s.Effects.Any(e=>e.ScriptEffect is not null));
    Check(!MagicBalanceModule.Replaceable(script.FormKey,native),"Scripted effects retained");
    patch.WriteToBinary("artifacts/magic-first-pass/MagicTests.esp");
    using var reread=OblivionMod.CreateFromBinaryOverlay("artifacts/magic-first-pass/MagicTests.esp",OblivionRelease.Oblivion);
    Check(reread.Spells.Single().Effects[0].Data!.Magnitude==100,"Generated spell binary roundtrip");
}
Console.WriteLine("Magic checks passed: formulas, access, affordability, class parity, scenarios, native generation and preservation.");
if(args.Length==1&&args[0]=="--bench")
{
    foreach(var level in new[]{1,5,10,15,20,30,35})
    {
        var tier=level>=35?8:level>=30?7:level>=20?5:level>=15?4:level>=10?3:level>=5?2:0;
        var result=MagicScenarios.Run(magic,combat,creation,settings,new(){Profile="Destruction specialist",Race=imperial.Key,Tier=tier,Level=level,OpponentTier=tier,OpponentLevel=level});
        Console.WriteLine(JsonSerializer.Serialize(new{Level=level,result.MagickaPool,result.MagickaRegen,result.SpellDamage,result.SpellCost,result.AttackBurstCasts,result.PoolRecoverySeconds,End=result.Timeline.Last()}));
    }
    return;
}
if(args.Length==2&&args[0]=="--candidate")
{
    using var candidate=OblivionMod.CreateFromBinaryOverlay(args[1],OblivionRelease.Oblivion);
    Check(candidate.Spells.Count>0&&candidate.Spells.All(s=>s.EditorID?.StartsWith("BGSOMagic_")==true)&&candidate.MagicEffects.Count==0,"Only new representative spells; no original spell or MGEF overrides");
    using var gameplay=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1])!,"Reports","BaldursGateStyleOblivion.magic-gameplay.json")));
    var generated=gameplay.RootElement.GetProperty("GeneratedSpells").EnumerateObject().ToDictionary(p=>p.Name,p=>FormKey.Factory(p.Value.GetString()!));
    var actors=candidate.Npcs.ToDictionary(n=>n.FormKey);
    foreach(var plan in gameplay.RootElement.GetProperty("Plans").EnumerateArray())
    {
        var kit=plan.GetProperty("Kit").EnumerateArray().ToArray();if(kit.Length==0)continue;
        var npc=actors[FormKey.Factory(plan.GetProperty("FormKey").GetString()!)];
        Check(npc.Configuration!.BaseSpellPoints==plan.GetProperty("MagickaPool").GetDouble(),"Stored NPC reserve matches report: "+npc.EditorID);
        foreach(var entry in kit)
        {
            var spell=entry.GetProperty("Spell");var key=new FormKey(candidate.ModKey,generated[spell.GetProperty("Key").GetString()!].ID);
            Check(npc.Spells.Any(s=>s.FormKey==key),"Kit spell link exists: "+npc.EditorID);
            Check(entry.GetProperty("Skill").GetDouble()>=spell.GetProperty("RequiredSkill").GetDouble(),"Kit mastery eligibility: "+npc.EditorID);
            Check(entry.GetProperty("EstimatedCost").GetDouble()<=plan.GetProperty("MagickaPool").GetDouble()*magic.MaximumPoolFraction+.00001,"Kit reserve eligibility: "+npc.EditorID);
        }
        foreach(var retained in plan.GetProperty("Retained").EnumerateArray())Check(npc.Spells.Any(s=>s.FormKey==FormKey.Factory(retained.GetString()!)),"Preserved spell link: "+npc.EditorID);
    }
    Check(candidate.Spells.All(s=>s.FormKey.ID>=0xF00000),"Candidate generated spell IDs use stable range");
    Console.WriteLine("Candidate verified: representative spells, NPC reserves, kit links, mastery, affordability and preserved inventories.");
    return;
}
string? physical=null;
if(args.Length>2&&args[0]=="--physical"){physical=args[1];args=args.Skip(2).ToArray();}
if(args.Length>0)
{
    var records=new Dictionary<FormKey,IMajorRecordGetter>();
    var mods=new List<IOblivionModDisposableGetter>();
    try
    {
        foreach(var path in args.Skip(1))
        {
            var mod=OblivionMod.CreateFromBinaryOverlay(path,OblivionRelease.Oblivion);mods.Add(mod);
            foreach(var record in mod.EnumerateMajorRecords())records[record.FormKey]=record;
        }
        MagicAnalysisModule.Write(records,_=>true,args[0],physical);
        Console.WriteLine($"Native report: {args[0]}; {records.Values.OfType<ISpellGetter>().Count()} spell records.");
    }
    finally{foreach(var mod in mods)mod.Dispose();}
}