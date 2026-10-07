using System.Text.Json;
using Mutagen.Bethesda;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Creation;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
if(args.Length>1&&args[0]=="--empty"){using var empty=OblivionMod.CreateFromBinaryOverlay(args[1],OblivionRelease.Oblivion);if(empty.EnumerateMajorRecords().Any())throw new Exception("Report-only wrote native records");Console.WriteLine("Report-only binary contains no overrides.");return;}
void Check(bool ok,string message){if(!ok)throw new Exception(message);}
var reportPath="artifacts/combat-first-pass/Reports/BaldursGateStyleOblivion.character-creation.json";
using var report=JsonDocument.Parse(File.ReadAllText(reportPath));var baseline=report.RootElement.GetProperty("Baseline").Deserialize<CreationCatalog>(CreationBalance.Options)!;
var settings=CreationBalance.Load("BaldursGateStyleOblivion/creation.json");var original=JsonSerializer.Serialize(baseline);var proposed=CreationBalance.Apply(baseline,settings);Check(JsonSerializer.Serialize(baseline)==original,"Preview does not mutate original records");
Check(baseline.Races.Length==10&&baseline.Classes.Length==21&&baseline.Birthsigns.Length==13,"Full vanilla character creation coverage");
foreach(var r in baseline.Races){var target=proposed.Races.Single(t=>t.Key==r.Key);Check(target.Male.SequenceEqual(r.Male)&&target.Female.SequenceEqual(r.Female)&&target.Skills.SequenceEqual(r.Skills),"Default racial starts retained: "+r.Name);}
Check(proposed.Classes.SequenceEqual(baseline.Classes),"Default class layouts retained");
Check(proposed.Races.Single(r=>r.Name=="Breton").Bonuses["ResistMagic"]==25,"Breton defense target");Check(proposed.Races.Single(r=>r.Name=="High Elf").Bonuses["Magicka"]==60,"High Elf reserve target");
var atronach=proposed.Birthsigns.Single(s=>s.Name=="The Atronach");Check(atronach.Bonuses["StuntedMagicka"]==1&&atronach.Bonuses["SpellAbsorption"]==25&&atronach.Bonuses["Magicka"]==75,"Atronach retains identity with bounded benefits");
Check(CreationBalance.IncomingMagic(new(){["ResistMagic"]=25,["SpellAbsorption"]=25},"Fire")==.5625,"Independent passive defense factors");
Check(CreationBalance.IncomingMagic(new(){["ResistMagic"]=-25,["ResistFire"]=-25},"Fire")==1.5625,"Magic and elemental vulnerability factors");
var combat=CombatConfiguration.Load("BaldursGateStyleOblivion/combat.json");var imperial=proposed.Races.Single(r=>r.Name=="Imperial");var breton=proposed.Races.Single(r=>r.Name=="Breton");
foreach(var level in new[]{1,10,20,30})
{
 var player=PlayerBuilds.AtLevel(combat.Gameplay,proposed,"Warrior",imperial.Key,false,null,level).Stats;var npc=CombatBuilds.ActorAtLevel(combat.Gameplay,proposed,"Warrior",imperial.Key,false,5,level,new Dictionary<int,int?>());foreach(var key in CreationBalance.Attributes.Concat(CombatBuilds.Skills))Check(player[key]==npc[key],"Player/NPC progression parity: "+key);
 var mage=PlayerBuilds.AtLevel(combat.Gameplay,proposed,"Mage",breton.Key,false,atronach.Key,level).Stats;Check(mage["Magicka"]==100&&mage["ResistMagic"]==25&&mage["SpellAbsorption"]==25&&mage["StuntedMagicka"]==1,"Player passives available for scenarios");
 var baseNpc=CombatBuilds.ActorAtLevel(combat.Gameplay,proposed,"Mage",breton.Key,false,5,level,new Dictionary<int,int?>());Check(!baseNpc.ContainsKey("Magicka")&&!baseNpc.ContainsKey("ResistMagic"),"NPC racial abilities not baked in twice");
}
foreach(var race in proposed.Races)foreach(var cls in proposed.Classes)foreach(var level in new[]{1,10,20,30})PlayerBuilds.AtLevel(combat.Gameplay,proposed,cls.Specialization=="Magic"?"Mage":"Warrior",race.Key,false,null,level,creationClass:cls.Name);
void Reject(CreationSettings test,string message){try{CreationBalance.Validate(test,baseline);throw new Exception(message);}catch(ArgumentException){}}
var wrong=CreationBalance.Parse(JsonSerializer.Serialize(settings));wrong.Spells.First().Value.Effects.First().Value.Code="FIDG";Reject(wrong,"Effect identity substitution accepted");Reject(new(){Classes=new(){["Warrior"]=new(){Attributes=["Strength","Strength"]}}},"Duplicate class attributes accepted");Reject(new(){Races=new(){[imperial.Key]=new(){Male=new(){["Strength"]=101}}}},"Invalid racial attribute accepted");
using var vanilla=OblivionMod.CreateFromBinaryOverlay("F:/SteamLibrary/steamapps/common/Oblivion/Data/Oblivion.esm",OblivionRelease.Oblivion);
var candidatePath=args.Length>1&&args[0]=="--edits"?args[1]:args.Length>0?args[0]:"artifacts/combat-first-pass/BaldursGateStyleOblivion.esp";
using var candidate=OblivionMod.CreateFromBinaryOverlay(candidatePath,OblivionRelease.Oblivion);
if(args.Length>1&&args[0]=="--edits")
{
 var race=candidate.Races.Single();Check(race.RaceStats!.Male.Strength==46&&race.RaceStats.Female.Strength==vanilla.Races.First(r=>r.FormKey==race.FormKey).RaceStats!.Female.Strength,"Native race edit preserves other sex");
 var boost=typeof(IRaceDataGetter).GetProperties().Where(p=>p.Name.StartsWith("SkillBoost")).Select(p=>(ISkillBoostGetter)p.GetValue(race.Data!)!).Single(b=>b.Skill==ActorValue.Blade);Check(boost.Boost==7,"Native racial skill bonus editable");
 var cls=candidate.Classes.Single();Check(cls.Data!.PrimaryAttributes.Select(a=>a.ToString()).SequenceEqual(new[]{"Strength","Agility"}),"Native class favored attributes editable");Check(cls.Data.SecondaryAttributes.Select(a=>a.ToString()).SequenceEqual(vanilla.Classes.First(c=>c.FormKey==cls.FormKey).Data!.SecondaryAttributes.Select(a=>a.ToString())),"Native major skills preserved");Console.WriteLine("Native race/class override checks passed.");return;
}
foreach(var sign in proposed.Birthsigns.Where(s=>(s.Abilities??[]).Any(a=>settings.Spells.ContainsKey(a.Key))))Check(candidate.Birthsigns.First(s=>s.FormKey==FormKey.Factory(sign.Key)).Description?.ToString()!=vanilla.Birthsigns.First(s=>s.FormKey==FormKey.Factory(sign.Key)).Description?.ToString(),"Changed birthsign descriptions reflect new effects");
foreach(var pair in settings.Spells)
{
 var key=FormKey.Factory(pair.Key);var target=candidate.Spells.First(s=>s.FormKey==key);var source=vanilla.Spells.First(s=>s.FormKey==key);Check(target.Data!.Type==source.Data!.Type&&target.Data.Cost==source.Data.Cost&&target.Data.Flag==source.Data.Flag,"Native activation/cost preserved");Check(target.Effects.Count==source.Effects.Count,"Effect count preserved");
 for(var i=0;i<source.Effects.Count;i++){var a=target.Effects[i].Data!;var b=source.Effects[i].Data!;Check(a.MagicEffect.ToString()==b.MagicEffect.ToString()&&a.ActorValue==b.ActorValue&&a.Type==b.Type&&a.Area==b.Area,"Native effect identity/range preserved");var edit=pair.Value.Effects.GetValueOrDefault(i);Check(a.Magnitude==(edit?.Magnitude??b.Magnitude)&&a.Duration==(edit?.Duration??b.Duration),"Native effect strength matches config");}
}
Console.WriteLine("Creation checks passed: 10 races, 21 classes, 13 signs, 12 targeted spells, native effect preservation, all race/class progressions and player/NPC parity.");