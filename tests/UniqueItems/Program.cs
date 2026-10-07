using System.Text.Json;
using BaldursGateStyleOblivion.Enhancements;
using BaldursGateStyleOblivion.Combat;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
void Check(bool value,string reason){if(!value)throw new Exception(reason);}
var settings=EnhancementConfiguration.Load<EnchantmentSettings>("BaldursGateStyleOblivion/enchantments.json");var combat=CombatConfiguration.Load("BaldursGateStyleOblivion/combat.json");
using var audit=JsonDocument.Parse(File.ReadAllText("docs/UniqueItemTranslation.json"));var families=audit.RootElement.GetProperty("Families").EnumerateArray().ToArray();Check(families.Length==111,"All registered unique equipment families reviewed");Check(families.Sum(f=>f.GetProperty("Records").GetArrayLength())==556,"All registered unique equipment records covered");
foreach(var family in families)foreach(var key in family.GetProperty("Records").EnumerateArray().Select(k=>k.GetString()!)){Check(settings.Artifacts.ContainsKey(key),"Reviewed unique has an explicit rule");var rule=settings.Artifacts[key];Check(rule.Tier==family.GetProperty("Tier").GetInt32(),"Fixed family tier");Check(rule.Preserve==family.GetProperty("Preserved").GetBoolean(),"Preservation deliberate");Check(rule.Sources.Length>0,"Lore tier has consulted evidence");}
var ranks=families.ToDictionary(f=>f.GetProperty("Name").GetString()!,f=>f.GetProperty("Tier").GetInt32());
Check(ranks["Dawnfang"]==ranks["Duskfang"]&&ranks["Dawnfang Superior"]==ranks["Duskfang Superior"]&&ranks["Dawnfang Superior"]>=ranks["Dawnfang"],"Paired transforming forms have coherent ranks");
Check(families.Where(f=>f.GetProperty("Name").GetString()!.StartsWith("Umbra's Ebony")).Select(f=>f.GetProperty("Tier").GetInt32()).Distinct().Count()==1,"Plain armor set shares craftsmanship rank");
EnhancementEffect[] mixed=[new("SEFF","Health",0,30,false,true),new("FIDG","Health",200,10,true)];var original=mixed.ToArray();var result=EnhancementBalance.Enchant(mixed,settings,8,"Weapon",false,1.4,false,true);Check(result[0]==mixed[0],"Scripted effect retained exactly");Check(EnhancementBalance.Damage(result)<=settings.Tiers[8].Damage*1.4,"Non-scripted damage bounded on scripted artifact");Check(mixed.SequenceEqual(original),"Original effect array unchanged");Check(EnhancementBalance.Enchant(mixed,settings,8,"Weapon",false).SequenceEqual(original),"Scripted artifacts remain protected without explicit opt-in");
var disintegrate=EnhancementBalance.Enchant([new("DIAR","Health",1000,0,true)],settings,5,"Weapon",false,1.2);Check(disintegrate[0].Magnitude<=settings.Tiers[5].Damage*3*1.2,"Disintegration fitted to equipment durability scale");
foreach(var tier in Enumerable.Range(0,11))Check(combat.Gameplay.WeaponPower(tier)==1,"Ordinary equipment must not gain hidden actor-tier damage");
foreach(var itemClass in combat.Gameplay.WeaponBaselines.Keys){var item=new PhysicalItem("Test",null,"Comparison","Weapon",itemClass,"Daedric",1,1,1,1,1,0,1,"",false,false,false,null);var standard=PhysicalBalance.Propose(item,combat);var legendary=EnhancementBalance.ArtifactPhysical(item,combat,new ArtifactRule{Tier=10,Class=itemClass,PhysicalPower=1.1});Check(legendary.Damage<=Math.Round(standard.Damage*1.15),"Apex label must not multiply equipment power: "+itemClass);}
if(args.Length==0){Console.WriteLine("Unique item rule and mixed-script budget checks passed.");return;}
using var candidate=OblivionMod.CreateFromBinaryOverlay(args[0],OblivionRelease.Oblivion);using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0])!,"Reports","BaldursGateStyleOblivion.enhancements.json")));var rows=report.RootElement.GetProperty("Items").EnumerateArray().ToDictionary(r=>r.GetProperty("FormKey").GetString()!);var records=candidate.EnumerateMajorRecords().ToDictionary(r=>r.FormKey);var originals=new Dictionary<FormKey,IMajorRecordGetter>();var mods=new List<IOblivionModDisposableGetter>();
try
{
 foreach(var path in Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data").Where(p=>Path.GetFileName(p)=="Oblivion.esm"||Path.GetFileName(p)=="Knights.esp"||Path.GetFileName(p).StartsWith("DLC")&&p.EndsWith(".esp"))){var mod=OblivionMod.CreateFromBinaryOverlay(path,OblivionRelease.Oblivion);mods.Add(mod);foreach(var r in mod.EnumerateMajorRecords())originals[r.FormKey]=r;}
 foreach(var family in families)
 {
  var physicalSignatures=new Dictionary<string,HashSet<string>>();var effectSignatures=new Dictionary<string,string>();
  foreach(var key in family.GetProperty("Records").EnumerateArray().Select(k=>k.GetString()!))
  {
   var row=rows[key];Check(row.GetProperty("Curated").GetBoolean(),"All reviewed items visible in artifact editor");var rule=settings.Artifacts[key];var form=FormKey.Factory(key);var source=originals[form];if(rule.Preserve){Check(row.GetProperty("Preserved").GetBoolean(),"Quest/script exception preserved");continue;}
   Check(!row.GetProperty("Preserved").GetBoolean(),"Reviewed equipment applied: "+key);var raw=row.GetProperty("Before").Deserialize<EnhancementEffect[]>()!;var bounded=EnhancementBalance.Enchant(rule.Effects??raw,settings,rule.Tier,row.GetProperty("Slots").GetString()!,row.GetProperty("Activation").GetString()=="Apparel",rule.EnchantmentPower,row.GetProperty("Activation").GetString()=="Staff",rule.BalanceScriptedEquipment);Check(bounded.SequenceEqual(row.GetProperty("After").Deserialize<EnhancementEffect[]>()!),"Native/report proposal uses the shared budget, including mixed scripts: "+key);var target=records[form];var expected=row.GetProperty("AfterPhysical").Deserialize<PhysicalItem>()!;
   var identityClass=expected.Class+":"+expected.Slots;
   if(!physicalSignatures.ContainsKey(identityClass))physicalSignatures[identityClass]=[];
   if(rule.NormalizePhysical){var standard=PhysicalBalance.Propose(expected with{FormKey="Comparison",Material=expected.Kind=="Weapon"||expected.Heavy?"Daedric":"Glass",Protected=false,Enchanted=false,EditorID=null},combat);Check(expected.Damage<=Math.Round(standard.Damage*1.15)&&expected.Armor<=standard.Armor*1.15+.01,"Unique stays within best same-class equipment scale: "+key);}
   if(target is IWeaponGetter weapon){Check(weapon.Data!.Damage==expected.Damage&&Math.Abs(weapon.Data.Weight-expected.Weight)<.001&&weapon.Data.Health==expected.Durability,"Native weapon physical translation");Check(weapon.Script.FormKey==((IWeaponGetter)source).Script.FormKey,"Native weapon script link retained");physicalSignatures[identityClass].Add(weapon.Data.Damage+":"+weapon.Data.Weight+":"+weapon.Data.Health);}
   if(target is IArmorGetter armor){Check(Math.Abs(armor.Data!.ArmorValue-expected.Armor)<.011&&Math.Abs(armor.Data.Weight-expected.Weight)<.001,"Native armor physical translation");Check(armor.Script.FormKey==((IArmorGetter)source).Script.FormKey,"Native armor script link retained");physicalSignatures[identityClass].Add(armor.Data.ArmorValue+":"+armor.Data.Weight+":"+armor.Data.Health);}
   FormKey Enchantment(IMajorRecordGetter item)=>item switch{IWeaponGetter w=>w.Enchantment.FormKey,IArmorGetter a=>a.Enchantment.FormKey,IClothingGetter c=>c.Enchantment.FormKey,_=>FormKey.Null};
   var oldLink=Enchantment(source);var newLink=Enchantment(target);if(oldLink.IsNull)continue;var old=(IEnchantmentGetter)originals[oldLink];var next=candidate.Enchantments.First(e=>e.FormKey==newLink);Check(old.Effects.Count==next.Effects.Count,"Native effect count retained");
   for(var i=0;i<old.Effects.Count;i++){var a=old.Effects[i];var b=next.Effects[i];Check(a.Data!.MagicEffect.ToString()==b.Data!.MagicEffect.ToString()&&a.Data.ActorValue==b.Data.ActorValue&&a.Data.Type==b.Data.Type&&a.Data.Area==b.Data.Area,"Native effect identity and delivery retained");if(a.ScriptEffect is not null){Check(b.ScriptEffect?.Data?.Script.FormKey==a.ScriptEffect.Data?.Script.FormKey&&b.ScriptEffect?.Name==a.ScriptEffect.Name&&b.Data.Magnitude==a.Data.Magnitude&&b.Data.Duration==a.Data.Duration,"Scripted effect metadata retained exactly");}}
   var identity=string.Join("|",old.Effects.Select(e=>e.Data!.MagicEffect+":"+e.Data.ActorValue));var numeric=string.Join("|",next.Effects.Where(e=>e.ScriptEffect is null).Select(e=>e.Data!.Magnitude+":"+e.Data.Duration));if(effectSignatures.TryGetValue(identity,out var first))Check(first==numeric,"Leveled variants share a fixed enchantment profile: "+family.GetProperty("Name"));else effectSignatures[identity]=numeric;
  }
  Check(physicalSignatures.Values.All(s=>s.Count<=1),"Leveled variants share physical power: "+family.GetProperty("Name"));
 }
 Console.WriteLine("Native unique checks passed: all 111 families, physical translations, fixed variants, quest exceptions and script/effect links.");
}
finally{foreach(var mod in mods)mod.Dispose();}