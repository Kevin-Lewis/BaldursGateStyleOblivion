using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Enhancements;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using System.Text.RegularExpressions;
void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
void Near(double a,double b,string reason)=>Check(Math.Abs(a-b)<.011,reason+": "+a+" vs "+b);
var settings=CombatConfiguration.Load("BaldursGateStyleOblivion/combat.json");var enchant=EnhancementConfiguration.Load<EnchantmentSettings>("BaldursGateStyleOblivion/enchantments.json");
using var source=OblivionMod.CreateFromBinaryOverlay("F:/SteamLibrary/steamapps/common/Oblivion/Data/Oblivion.esm",OblivionRelease.Oblivion);
Check(PhysicalCombatModule.Material("SEDarkSeducerArmor3Chain")=="Chainmail","Chain armor alias");Check(PhysicalCombatModule.Material("SEGoldenSaint4Longsword")=="Ebony","Golden weapon craftsmanship rank");Check(PhysicalCombatModule.Material("SEDarkSeducerOfficerHelmet")=="Glass","Officer helmet classified");
var originals=source.EnumerateMajorRecords().Where(r=>Regex.IsMatch(r.EditorID??"",@"^(SEGoldenSaint|SEDarkSeducer|SEOrderKnight|SE07ADarkSeducerEliteHelmet)",RegexOptions.IgnoreCase)&&r is IWeaponGetter or IArmorGetter).ToArray();
IOblivionModDisposableGetter? candidate=args.Length>0?OblivionMod.CreateFromBinaryOverlay(args[0],OblivionRelease.Oblivion):null;
try
{
 var patched=candidate?.EnumerateMajorRecords().ToDictionary(r=>r.FormKey);var count=0;
 foreach(var record in originals)
 {
  if(record.EditorID!.Contains("Test",StringComparison.OrdinalIgnoreCase))continue;
  var item=PhysicalCombatModule.ReadItem(record,[])!;Check(item.Material!="Unknown","Faction equipment left unclassified: "+record.EditorID);
  if(record is IWeaponGetter w&&record.EditorID!.StartsWith("SEOrderKnight"))Check(item.Class=="Longsword","Order sword class uses longsword baseline");
  var expected=enchant.Artifacts.TryGetValue(record.FormKey.ToString(),out var rule)?EnhancementBalance.ArtifactPhysical(item,settings,rule):PhysicalBalance.Propose(item,settings);
  var comparison=PhysicalBalance.Propose(item with{FormKey="Comparison",Material=expected.Material,Damage=item.Kind=="Weapon"?1:0,Armor=item.Kind=="Weapon"?0:1,Weight=1,Durability=1,Protected=false},settings);
  Near(expected.Damage,comparison.Damage,"Faction weapon damage matches same-class material baseline: "+record.EditorID);Near(expected.Armor,comparison.Armor,"Faction armor matches same covered slots: "+record.EditorID);
  Near(expected.Weight,comparison.Weight,"Faction weight matches coverage/material: "+record.EditorID);Check(expected.Durability==comparison.Durability,"Faction durability matches coverage/material: "+record.EditorID);
  if(patched is not null)
  {
   var actual=patched.GetValueOrDefault(record.FormKey)??record;var physical=PhysicalCombatModule.ReadItem(actual,[])!;
   Near(physical.Damage,expected.Damage,"Native faction damage: "+record.EditorID);Near(physical.Armor,expected.Armor,"Native faction armor: "+record.EditorID);Near(physical.Weight,expected.Weight,"Native faction weight: "+record.EditorID);Check(physical.Durability==expected.Durability,"Native faction durability: "+record.EditorID);
   if(record is IArmorGetter a)Check(((IArmorGetter)actual).ClothingFlags!.BipedFlags==a.ClothingFlags!.BipedFlags&&((IArmorGetter)actual).ClothingFlags!.GeneralFlags==a.ClothingFlags!.GeneralFlags,"Coverage and playability retained");
  }
  count++;
 }
 var order=source.Weapons.Single(w=>w.EditorID=="SEOrderKnight6SwordDaedric");var orderResult=PhysicalBalance.Propose(PhysicalCombatModule.ReadItem(order,[])!,settings);var daedric=PhysicalBalance.Propose(PhysicalCombatModule.ReadItem(source.Weapons.Single(w=>w.EditorID=="WeapDaedricLongsword"),[])!,settings);Near(orderResult.Damage,daedric.Damage,"Perfect Order sword is not stronger than a Daedric longsword");
 var arrowMax=source.Ammunitions.Single(a=>a.Name?.ToString()=="Daedric Arrow"&&a.Enchantment.IsNull).Data!.Damage;
 foreach(var arrow in source.Ammunitions.Where(a=>Regex.IsMatch(a.EditorID??"",@"^SE(?:GoldenSaint|DarkSeducer)Arrow")))Check(arrow.Data!.Damage<=arrowMax,"Faction arrows exceed Daedric arrow baseline: "+arrow.EditorID);
 Console.WriteLine($"Shivering equipment checks passed: {count} faction weapons/armor, damage/coverage/weight/durability comparisons, Order sword baseline, flags and native arrow audit.");
}
finally{candidate?.Dispose();}




