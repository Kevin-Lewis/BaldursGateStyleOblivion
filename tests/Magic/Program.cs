using BaldursGateStyleOblivion.Magic;
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
Console.WriteLine("12 magic analysis checks passed.");
if(args.Length==2&&args[0]=="--candidate")
{
    using var candidate=OblivionMod.CreateFromBinaryOverlay(args[1],OblivionRelease.Oblivion);
    Check(candidate.Spells.Count==0&&candidate.MagicEffects.Count==0,"Analysis must not emit spell or magic-effect overrides");
    Console.WriteLine("Candidate verified: zero spell and magic-effect overrides.");
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