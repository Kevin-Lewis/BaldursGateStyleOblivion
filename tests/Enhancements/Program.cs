using System.Text.Json;
using BaldursGateStyleOblivion.Enhancements;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

void Check(bool ok,string message){if(!ok)throw new Exception(message);}
var enchant=EnhancementConfiguration.Load<EnchantmentSettings>("BaldursGateStyleOblivion/enchantments.json");var alchemy=EnhancementConfiguration.Load<AlchemySettings>("BaldursGateStyleOblivion/alchemy.json");
EnhancementEffect[] source=[new("FIDG","None",20,3,true),new("FRDG","None",20,3,true)];var bounded=EnhancementBalance.Enchant(source,enchant,2,"Weapon",false);
Check(EnhancementBalance.Damage(bounded)<=enchant.Tiers[2].Damage,"Multi-element enchantments share a budget");
Check(source[0].Magnitude==20&&source[0].Duration==3,"Source effects unchanged");
var heal=EnhancementBalance.Potion([new("REHE","None",1,120)],alchemy,0);
Check(heal[0].Magnitude>=1&&EnhancementBalance.Recovery(heal)<=alchemy.RecoveryBudgets[0],"Long low-rate recovery remains useful within budget");
var drain=EnhancementBalance.Enchant([new("DRHE","None",100,1,true)],enchant,1,"Weapon",false);Check(EnhancementBalance.Damage(drain)==0,"Drain health is not permanent damage");
var scripted=new EnhancementEffect[]{new("SEFF","None",100,30,true,true)};Check(EnhancementBalance.Enchant(scripted,enchant,1,"Weapon",false).SequenceEqual(scripted),"Script effects preserved");
var ring=EnhancementBalance.Enchant([new("RSMA","None",50,0)],enchant,3,"LeftRing, RightRing",true);var oneRing=EnhancementBalance.Enchant([new("RSMA","None",50,0)],enchant,3,"LeftRing",true);Check(ring.SequenceEqual(oneRing),"Two possible ring slots do not double item allowance");
Check(EnhancementBalance.CraftedValue(100,50,65,.12)==13,"Master crafted value estimate");Check(EnhancementBalance.CraftedValue(100,50,65,.12)*.4<5*1.4,"Common purchased ingredients do not fund master resale loop under tested barter");
foreach(var tier in Enumerable.Range(0,11))foreach(var code in new[]{"FIDG","FRDG","ABHE"}){var fx=EnhancementBalance.Enchant([new(code,"None",100,20,true)],enchant,tier,"Weapon",false);Check(EnhancementBalance.Damage(fx)<=enchant.Tiers[tier].Damage,"Damage budget across tiers: "+tier+code);}
try{EnhancementConfiguration.Parse<AlchemySettings>("{\"MaximumActivePotions\":10}");throw new Exception("Invalid configuration accepted");}catch(ArgumentException){}
using var vanilla=OblivionMod.CreateFromBinaryOverlay("F:/SteamLibrary/steamapps/common/Oblivion/Data/Oblivion.esm",OblivionRelease.Oblivion);
var definitions=vanilla.MagicEffects.Where(e=>e.EditorID is not null).ToDictionary(e=>e.EditorID!);
var template=vanilla.Enchantments.First(e=>e.Effects.All(f=>f.Data is not null)&&e.Effects.Count>0);var effects=EnhancementModule.Read(template.Effects,definitions);var patch=new OblivionMod(ModKey.FromNameAndExtension("EnhancementTests.esp"),OblivionRelease.Oblivion);var target=patch.Enchantments.DuplicateInAsNewRecord(template);var capped=EnhancementBalance.Enchant(effects,enchant,3,"Weapon",false);EnhancementModule.Apply(target.Effects,capped);Directory.CreateDirectory("artifacts/enhancements");patch.WriteToBinary("artifacts/enhancements/EnhancementTests.esp");using(var read=OblivionMod.CreateFromBinaryOverlay("artifacts/enhancements/EnhancementTests.esp",OblivionRelease.Oblivion)){Check(EnhancementModule.Read(read.Enchantments.Single().Effects,definitions).SequenceEqual(capped),"Native enchantment effect binary roundtrip");}
var sigil=EnhancementBalance.Sigil([new("FIDG","Health",30,1,true),new("FOSP","Health",100,0)],enchant,6,[1]);Check(sigil[1].Magnitude<=enchant.CustomConstantCaps["FOSP"]*enchant.SigilArmorMultiplier,"Sigil reserves respect permanent-effect allowance");
var resource=EnhancementBalance.Potion([new("FOHE","Health",1000,300)],alchemy,4);Check(resource[0].Magnitude<=65&&resource[0].Duration<=90,"Resource potion strength and duration bounded");
if(args.Length==2&&args[0]=="--candidate")
{
 using var candidate=OblivionMod.CreateFromBinaryOverlay(args[1],OblivionRelease.Oblivion);var reports=Path.Combine(Path.GetDirectoryName(args[1])!,"Reports","BaldursGateStyleOblivion.enhancements.json");using var report=JsonDocument.Parse(File.ReadAllText(reports));
 var originals=vanilla.EnumerateMajorRecords().ToDictionary(r=>r.FormKey);var mods=new List<IOblivionModDisposableGetter>();try
 {
  foreach(var file in Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data","*.esp").Where(f=>Path.GetFileName(f).StartsWith("DLC")||Path.GetFileName(f)=="Knights.esp")){var mod=OblivionMod.CreateFromBinaryOverlay(file,OblivionRelease.Oblivion);mods.Add(mod);foreach(var r in mod.EnumerateMajorRecords())originals.TryAdd(r.FormKey,r);}
  var records=candidate.EnumerateMajorRecords().ToDictionary(r=>r.FormKey);var enchantments=candidate.Enchantments.ToDictionary(r=>r.FormKey);var tested=0;
  foreach(var row in report.RootElement.GetProperty("Items").EnumerateArray())
  {
   var key=FormKey.Factory(row.GetProperty("FormKey").GetString()!);if(key.ModKey.ToString()=="Synthesis.esp")key=new(candidate.ModKey,key.ID);
   if(row.GetProperty("Preserved").GetBoolean())continue;if(!records.TryGetValue(key,out var record))throw new Exception("Missing equipment override: "+key);
   var link=record switch{IWeaponGetter w=>w.Enchantment.FormKey,IArmorGetter a=>a.Enchantment.FormKey,IClothingGetter c=>c.Enchantment.FormKey,IAmmunitionGetter a=>a.Enchantment.FormKey,_=>FormKey.Null};var expected=row.GetProperty("After").Deserialize<EnhancementEffect[]>()!;
   if(expected.Length==0)continue;Check(enchantments.ContainsKey(link),"Private enchantment resolves: "+key);var actual=EnhancementModule.Read(enchantments[link].Effects,definitions);Check(actual.SequenceEqual(expected),"Native effects match proposal: "+key);var before=row.GetProperty("Before").Deserialize<EnhancementEffect[]>()!;Check(before.Zip(actual).All(p=>p.First.Code==p.Second.Code&&p.First.ActorValue==p.Second.ActorValue),"Native effect identities retained: "+key);if(record is IWeaponGetter charged && enchantments[link].Data?.Type is Enchantment.EnchantmentType.Weapon or Enchantment.EnchantmentType.Staff){var capacity=charged.EnchantmentPoints??0;var cost=enchantments[link].Data!.EnchantCost;Check(capacity>0&&cost>0&&cost<=capacity,"Weapon has usable charge: "+key);Check(cost==(uint)Math.Ceiling(Math.Max(100u,capacity)/(double)row.GetProperty("ChargedHits").GetInt32()),"Weapon charge uses item capacity: "+key);}tested++;
  }
  foreach(var row in report.RootElement.GetProperty("Potions").EnumerateArray().Where(r=>!r.GetProperty("Preserved").GetBoolean())){var key=FormKey.Factory(row.GetProperty("FormKey").GetString()!);var potion=(IPotionGetter)records[key];Check(potion.Data!.Value==row.GetProperty("Value").GetUInt32(),"Potion value matches report");Check(potion.Data.Flags.HasFlag(IngredientFlag.ManualValue),"Potion value applied manually");Check(EnhancementModule.Read(potion.Effects,definitions).SequenceEqual(row.GetProperty("After").Deserialize<EnhancementEffect[]>()!),"Potion power matches native write");}
  foreach(var row in report.RootElement.GetProperty("SigilStones").EnumerateArray().Where(r=>!r.GetProperty("Preserved").GetBoolean())){var stone=candidate.SigilStones.First(s=>s.FormKey==FormKey.Factory(row.GetProperty("FormKey").GetString()!));Check(EnhancementModule.Read(stone.Effects,definitions).SequenceEqual(row.GetProperty("After").Deserialize<EnhancementEffect[]>()!),"Sigil effects match native write");}
  foreach(var effect in candidate.MagicEffects.Where(e=>enchant.ExcludedCustomEffects.Contains(e.EditorID!)))Check(!effect.Data!.Flags.HasFlag(MagicEffect.MagicFlag.Enchanting),"Unsafe custom enchanting flag removed");
  Console.WriteLine("Candidate native checks passed: "+tested+" item enchantments, potion power/values, crafting restrictions and crafting settings.");
 }
 finally{foreach(var mod in mods)mod.Dispose();}
}
Console.WriteLine("Enhancement checks passed: tier/slot budgets, effect preservation, alchemy economics and native roundtrip.");