using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using BaldursGateStyleOblivion.Core;
using BaldursGateStyleOblivion.Modules;
using BaldursGateStyleOblivion.Combat;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("Enhancements.Tests")]
namespace BaldursGateStyleOblivion.Enhancements;

internal static class EnhancementModule
{
    internal static string PathFor(PatcherRun run,string name,string configured)
    {
        if(!string.IsNullOrWhiteSpace(configured))return Path.GetFullPath(configured,run.DataDirectory);
        var path=Path.Combine(run.DataDirectory,name+".json");return File.Exists(path)?path:Path.Combine(AppContext.BaseDirectory,name+".json");
    }
    internal static EnhancementEffect[] Read(IEnumerable<IEffectGetter> effects,IReadOnlyDictionary<string,IMagicEffectGetter> definitions)=>effects.Where(e=>e.Data is not null).Select(e=>
    {
        var d=e.Data!;var code=d.MagicEffect.ToString()??"Unknown";return new EnhancementEffect(code,d.ActorValue.ToString()??"None",d.Magnitude,d.Duration,definitions.GetValueOrDefault(code)?.Data?.Flags.HasFlag(MagicEffect.MagicFlag.Hostile)==true,e.ScriptEffect is not null||code=="SEFF");
    }).ToArray();
    internal static void Apply(IList<Effect> target,EnhancementEffect[] effects)
    {
        if(target.Count!=effects.Length)throw new InvalidDataException("Native effect count changed.");
        for(var i=0;i<effects.Length;i++)
        {
            var data=target[i].Data??throw new InvalidDataException("Missing native effect data.");var source=effects[i];
            // Never replace an artifact's special effect or governing actor value.
            if(data.MagicEffect.ToString()!=source.Code||data.ActorValue.ToString()!=source.ActorValue)throw new InvalidDataException("Effect identity changes are not permitted here.");
            data.Magnitude=(uint)Math.Round(source.Magnitude);data.Duration=(uint)Math.Round(source.Duration);
        }
    }
    internal static PhysicalItem? Physical(IMajorRecordGetter record,HashSet<FormKey> artifacts)
    {
        if(record is not IClothingGetter c||c.Data is null)return PhysicalCombatModule.ReadItem(record,artifacts);
        var slots=c.ClothingFlags?.BipedFlags.ToString()??"";
        return new(record.FormKey.ToString(),record.EditorID,c.Name?.ToString()??record.EditorID??"Clothing","Clothing",slots.Contains("Ring")||slots.Contains("Amulet")?"Jewelry":"Clothing",PhysicalCombatModule.Material(record.EditorID),0,0,0,c.Data.Weight,0,0,c.Data.Value,slots,false,!c.Enchantment.IsNull,artifacts.Contains(record.FormKey),null);
    }
    internal static int Tier(string? editor,string material)=>material switch{"Iron" or "Fur"=>1,"Steel" or "Leather"=>2,"Silver"=>3,"Dwarven" or "Chainmail"=>4,"Elven" or "Orcish" or "Mithril"=>5,"Glass" or "Ebony"=>6,"Daedric" or "Amber" or "Madness"=>7,_=>3};
    internal static int Rank(string? id)
    {
        if(Regex.IsMatch(id??"","Master|Strong|Grand",RegexOptions.IgnoreCase))return 4;
        if(Regex.IsMatch(id??"","Expert|Greater",RegexOptions.IgnoreCase))return 3;
        if(Regex.IsMatch(id??"","Journeyman|Standard",RegexOptions.IgnoreCase))return 2;
        if(Regex.IsMatch(id??"","Apprentice|Minor",RegexOptions.IgnoreCase))return 1;
        return 0;
    }
    internal static FormKey Stable(ModKey mod,string identity,uint range)=>new(mod,range|(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))&0xFFFFFu));
    public static Dictionary<FormKey,string[]> Run(IPatcherState<IOblivionMod,IOblivionModGetter> state,PatcherRun run)
    {
        var enchant=EnhancementConfiguration.Load<EnchantmentSettings>(PathFor(run,"enchantments",run.Settings.EnchantmentConfigurationFile));
        var alchemy=EnhancementConfiguration.Load<AlchemySettings>(PathFor(run,"alchemy",run.Settings.AlchemyConfigurationFile));
        var writeEnchant=run.Settings.EnableEnchantmentBalance&&enchant.Enabled&&!run.Settings.ReportOnly;
        var writeAlchemy=run.Settings.EnableAlchemyBalance&&alchemy.Enabled&&!run.Settings.ReportOnly;
        var records=new Dictionary<FormKey,IMajorRecordGetter>();
        foreach(var listing in state.LoadOrder.PriorityOrder.Where(l=>l.Enabled&&l.Mod is not null&&run.IsInputPlugin(l.ModKey)))foreach(var r in listing.Mod!.EnumerateMajorRecords())records.TryAdd(r.FormKey,r);
        foreach(var r in state.PatchMod.EnumerateMajorRecords())records[r.FormKey]=r;
        var definitions=records.Values.OfType<IMagicEffectGetter>().Where(e=>!e.IsDeleted&&e.EditorID is not null).ToDictionary(e=>e.EditorID!,StringComparer.Ordinal);
        var artifacts=RewardConfiguration.Load(RewardRecords.PathFor(run));var protectedItems=RewardRecords.Protected(artifacts);
        var combat=CombatConfiguration.Load(PathFor(run,"combat",run.Settings.CombatConfigurationFile));
        var changes=new Dictionary<FormKey,string[]>();var engine=new List<object>();var enchanting=new List<object>();var items=new List<object>();var sigils=new List<object>();var potions=new List<object>();var ingredients=new List<object>();var apparatus=new List<object>();var stock=new List<object>();
        void Changed(IMajorRecord record,string[] fields){records[record.FormKey]=record;changes[record.FormKey]=changes.GetValueOrDefault(record.FormKey,[]).Concat(fields).Distinct().ToArray();}
        void Setting(string name,double value,bool write)
        {
            var source=records.Values.OfType<IGameSettingGetter>().FirstOrDefault(g=>g.EditorID==name&&!g.IsDeleted);double? before=source is IGameSettingFloatGetter f?f.Data:source is IGameSettingIntGetter n?n.Data:null;
            engine.Add(new{EditorID=name,Before=before,After=value,Applied=write});if(!write||before==value)return;
            IGameSetting target;
            if(name.StartsWith('i')){if(source is not null&&source is not IGameSettingIntGetter)throw new InvalidDataException("Wrong GMST type: "+name);var t=source is null?state.PatchMod.GameSettings.AddNewInt():(GameSettingInt)state.PatchMod.GameSettings.GetOrAddAsOverride(source);t.Data=(int)value;target=t;}
            else{if(source is not null&&source is not IGameSettingFloatGetter)throw new InvalidDataException("Wrong GMST type: "+name);var t=source is null?state.PatchMod.GameSettings.AddNewFloat():(GameSettingFloat)state.PatchMod.GameSettings.GetOrAddAsOverride(source);t.Data=(float)value;target=t;}
            target.EditorID=name;Changed(target,["Data"]);
        }
        if(run.Settings.EnableEnchantmentBalance&&enchant.Enabled&&enchant.RestrictCustomEnchanting)
        {
            Setting("fMagicCEEnchantMagOffset",0,writeEnchant);
            foreach(var effect in definitions.Values.Where(e=>e.Data is not null&&run.Includes(e.FormKey.ModKey)))
            {
                var code=effect.EditorID!;var excluded=enchant.ExcludedCustomEffects.Contains(code);var cap=enchant.CustomConstantCaps.GetValueOrDefault(code,-1d);if(!excluded&&cap<0)continue;
                var before=effect.Data!;var factor=before.SubData?.ConstantEffectEnchantmentFactor;
                double? after=cap>=0&&before.BaseCost>0&&factor.HasValue?Math.Min(factor.Value,cap/(5*before.BaseCost)):factor;
                enchanting.Add(new{Code=code,Excluded=excluded,ConstantCap=cap,BeforeFactor=factor,AfterFactor=after,Applied=writeEnchant});
                if(!writeEnchant)continue;
                var target=state.PatchMod.MagicEffects.GetOrAddAsOverride(effect);
                if(excluded)target.Data!.Flags&=~MagicEffect.MagicFlag.Enchanting;
                if(after.HasValue&&target.Data!.SubData is not null)target.Data!.SubData!.ConstantEffectEnchantmentFactor=(float)after.Value;
                Changed(target,["Data.Flags.Enchanting","Data.SubData.ConstantEffectEnchantmentFactor"]);
            }
        }
        FormKey CloneEnchantment(IEnchantmentGetter source,string identity,EnhancementEffect[] effects,int hits,uint capacity)
        {
            var key=Stable(state.PatchMod.ModKey,identity,0xD00000);while(records.ContainsKey(key))key=new FormKey(state.PatchMod.ModKey,0xD00000|((key.ID+1)&0xFFFFF));
            var target=new Enchantment(key,OblivionRelease.Oblivion);target.DeepCopyIn(source);target.EditorID="BGSOEnchantment_"+key.ID.ToString("X6");Apply(target.Effects,effects);
            if(target.Data!.Type is Enchantment.EnchantmentType.Weapon or Enchantment.EnchantmentType.Staff)
            {
                target.Data.EnchantCost=(uint)Math.Max(1,Math.Ceiling(capacity/(double)hits));target.Data.ChargeAmount=target.Data.EnchantCost;target.Data.Flags|=Enchantment.Flag.ManualEnchantCost;
            }
            state.PatchMod.Enchantments.Add(target);Changed(target,["New enchantment"]);return key;
        }
        var equipment=records.Values.Where(r=>r is IWeaponGetter or IArmorGetter or IClothingGetter or IAmmunitionGetter).OrderBy(r=>r.FormKey.ToString(),StringComparer.Ordinal).ToArray();
        foreach(var record in equipment.Where(r=>!r.IsDeleted&&run.Includes(r.FormKey.ModKey)||r.FormKey.ModKey==state.PatchMod.ModKey))
        {
            var key=record.FormKey.ToString();var enchantKey=record switch{IWeaponGetter w=>w.Enchantment.FormKey,IArmorGetter a=>a.Enchantment.FormKey,IClothingGetter c=>c.Enchantment.FormKey,IAmmunitionGetter a=>a.Enchantment.FormKey,_=>FormKey.Null};
            var source=records.GetValueOrDefault(enchantKey) as IEnchantmentGetter;var registered=protectedItems.Contains(record.FormKey);enchant.Artifacts.TryGetValue(key,out var rule);
            var physical=Physical(record,protectedItems);var slots=record switch{IArmorGetter a=>a.ClothingFlags?.BipedFlags.ToString(),IClothingGetter c=>c.ClothingFlags?.BipedFlags.ToString(),_=>"Weapon"}??"";
            var scripted=record switch{IWeaponGetter w=>!w.Script.IsNull,IArmorGetter a=>!a.Script.IsNull,IClothingGetter c=>!c.Script.IsNull,_=>false};
            var before=source is null?[]:Read(source.Effects,definitions);var constant=source?.Data?.Type==Enchantment.EnchantmentType.Apparel;
            var tier=rule?.Tier??enchant.ItemTiers.GetValueOrDefault(key,Tier(record.EditorID,physical?.Material??PhysicalCombatModule.Material(record.EditorID)));
            if(source?.Data?.Type==Enchantment.EnchantmentType.Staff&&rule is null&&!enchant.ItemTiers.ContainsKey(key)){var damage=EnhancementBalance.Damage(before);tier=Array.FindIndex(enchant.StaffDamage,v=>v>=damage);if(tier<0)tier=10;}
            var preserve=(record.MajorRecordFlagsRaw&(int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference)!=0&&rule is null||rule?.Preserve==true||enchant.PreserveItems.Contains(key)||registered&&rule is null||scripted&&rule is null||before.Any(e=>e.Scripted)&&rule?.BalanceScriptedEquipment!=true||record.EditorID?.StartsWith("Test",StringComparison.OrdinalIgnoreCase)==true;
            var after=preserve?before:EnhancementBalance.Enchant(before,enchant,tier,slots,constant,rule?.EnchantmentPower??1,source?.Data?.Type==Enchantment.EnchantmentType.Staff,rule?.BalanceScriptedEquipment==true);
            var compatibilityNote="";
            if(rule?.Effects is {} explicitEffects)
            {
                var targets=EnhancementBalance.ArtifactTargets(before,explicitEffects,out var compatible);
                if(!compatible)
                {
                    compatibilityNote="Winning enchantment differs from configured effects; retaining winning identities and applying tier budgets.";
                    run.Log($"Artifact compatibility: {record.EditorID} ({key}): {compatibilityNote}");
                }
                after=preserve?before:EnhancementBalance.Enchant(targets,enchant,tier,slots,constant,rule.EnchantmentPower,source?.Data?.Type==Enchantment.EnchantmentType.Staff,rule?.BalanceScriptedEquipment==true);
            }
            var proposedPhysical=physical;
            if(!preserve&&rule is not null&&physical is not null)proposedPhysical=EnhancementBalance.ArtifactPhysical(physical,combat,rule);
            if(source is null&&!registered&&rule is null)continue;
            var hits=rule?.ChargedHits??enchant.ChargedHits;
            items.Add(new{FormKey=key,record.EditorID,Name=record switch{IWeaponGetter w=>w.Name?.ToString(),IArmorGetter a=>a.Name?.ToString(),IClothingGetter c=>c.Name?.ToString(),_=>record.EditorID},Tier=tier,Artifact=registered||rule is not null,Curated=rule is not null,Preserved=preserve,CompatibilityNote=compatibilityNote,Scripted=scripted,Slots=slots,Activation=source?.Data?.Type.ToString()??"None",Before=before,After=after,BeforePhysical=physical,AfterPhysical=proposedPhysical,Charge=record is IWeaponGetter charged?charged.EnchantmentPoints:null,BeforeCost=source?.Data?.EnchantCost,ChargedHits=constant||source is null?0:hits,Damage=EnhancementBalance.Damage(after),Warnings=EnhancementBalance.Warnings(after,constant),Reason=rule?.Reason??(preserve?"Protected unique, quest, scripted or explicit exemption.":"Slot and tier budget; original effect identities retained.")});
            if(!writeEnchant||preserve)continue;
            var copy=record.DeepCopy();
            if(source?.Data is not null)
            {
                var capacity=rule?.ChargeCapacity??(record is IWeaponGetter chargedWeapon?Math.Max(100u,chargedWeapon.EnchantmentPoints??700u):700u);
                var link=CloneEnchantment(source,key,after,hits,capacity);
                switch(copy){case Weapon w:w.Enchantment.SetTo(link);w.EnchantmentPoints=(ushort)capacity;break;case Armor a:a.Enchantment.SetTo(link);break;case Clothing c:c.Enchantment.SetTo(link);break;case Ammunition a:a.Enchantment.SetTo(link);break;}
            }
            if(proposedPhysical is not null&&rule is not null)
            {
                if(copy is Weapon w){PhysicalCombatModule.Apply(w,proposedPhysical);w.Data!.Value=proposedPhysical.Value;}
                if(copy is Armor a){PhysicalCombatModule.Apply(a,proposedPhysical);a.Data!.Value=proposedPhysical.Value;}
                if(copy is Clothing c){c.Data!.Value=proposedPhysical.Value;c.Data.Weight=(float)proposedPhysical.Weight;}
            }
            switch(copy){case Weapon w:state.PatchMod.Weapons.GetOrAddAsOverride(w).DeepCopyIn(w);break;case Armor a:state.PatchMod.Armors.GetOrAddAsOverride(a).DeepCopyIn(a);break;case Clothing c:state.PatchMod.Clothes.GetOrAddAsOverride(c).DeepCopyIn(c);break;case Ammunition a:state.PatchMod.Ammunitions.GetOrAddAsOverride(a).DeepCopyIn(a);break;}Changed(copy,["Enchantment","Artifact physical values"]);
        }
        foreach(var stone in records.Values.OfType<ISigilStoneGetter>().Where(s=>!s.IsDeleted&&run.Includes(s.FormKey.ModKey)).ToArray())
        {
            var before=Read(stone.Effects,definitions);var armorIndices=Enumerable.Range(0,stone.Effects.Count).Where(i=>stone.Effects[i].Data?.Type==Effect.EffectType.Self).ToArray();var match=Regex.Match(stone.EditorID??"","([1-5])$");var tier=enchant.ItemTiers.GetValueOrDefault(stone.FormKey.ToString(),match.Success?int.Parse(match.Value)+1:4);
            var preserve=!stone.Script.IsNull||protectedItems.Contains(stone.FormKey)||enchant.PreserveItems.Contains(stone.FormKey.ToString())||before.Any(e=>e.Scripted);var after=preserve?before:EnhancementBalance.Sigil(before,enchant,tier,armorIndices);
            sigils.Add(new{FormKey=stone.FormKey.ToString(),stone.EditorID,Name=stone.Name?.ToString(),Tier=tier,ArmorIndices=armorIndices,Before=before,After=after,Preserved=preserve});
            if(!writeEnchant||preserve)continue;var target=state.PatchMod.SigilStones.GetOrAddAsOverride(stone);Apply(target.Effects,after);Changed(target,["Effects"]);
        }
        if(run.Settings.EnableAlchemyBalance&&alchemy.Enabled)
        {
            Setting("fPotionGoldValueMult",alchemy.CraftedValueMultiplier,writeAlchemy);
            foreach(var rank in new[]{"Novice","Apprentice","Journeyman","Expert","Master"})Setting("iMagicMaxPotions"+rank,alchemy.MaximumActivePotions,writeAlchemy);
            foreach(var potion in records.Values.OfType<IPotionGetter>().Where(p=>!p.IsDeleted&&run.Includes(p.FormKey.ModKey)&&p.Data is not null).ToArray())
            {
                var key=potion.FormKey.ToString();var before=Read(potion.Effects,definitions);var protectedPotion=alchemy.PreserveItems.Contains(key)||protectedItems.Contains(potion.FormKey)||!potion.Script.IsNull||(potion.MajorRecordFlagsRaw&(int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference)!=0||potion.EditorID?.StartsWith("Test",StringComparison.OrdinalIgnoreCase)==true||potion.Data!.Flags.HasFlag(IngredientFlag.FoodItem)||before.Any(e=>e.Scripted);
                var rank=alchemy.PotionRanks.GetValueOrDefault(key,Rank(potion.EditorID));var after=protectedPotion?before:EnhancementBalance.Potion(before,alchemy,rank);var value=protectedPotion?potion.Data!.Value:alchemy.PotionValues.GetValueOrDefault(key,EnhancementBalance.PotionValue(after,rank));
                potions.Add(new{FormKey=key,potion.EditorID,Name=potion.Name?.ToString(),Rank=rank,Poison=before.Length>0&&before.All(e=>e.Hostile),Preserved=protectedPotion,Before=before,After=after,BeforeValue=potion.Data!.Value,Value=value,Damage=EnhancementBalance.Damage(after),Healing=EnhancementBalance.Recovery(after),Warnings=EnhancementBalance.Warnings(after)});
                if(!writeAlchemy||protectedPotion)continue;var target=state.PatchMod.Potions.GetOrAddAsOverride(potion);Apply(target.Effects,after);target.Data!.Value=value;target.Data.Flags|=IngredientFlag.ManualValue;Changed(target,["Effects","Data.Value","Data.Flags.ManualValue"]);
            }
            foreach(var ingredient in records.Values.OfType<IIngredientGetter>().Where(i=>!i.IsDeleted&&run.Includes(i.FormKey.ModKey)&&i.Data is not null).ToArray())
            {
                var key=ingredient.FormKey.ToString();var before=Read(ingredient.Effects,definitions);var preserve=alchemy.PreserveItems.Contains(key)||protectedItems.Contains(ingredient.FormKey)||!ingredient.Script.IsNull||(ingredient.MajorRecordFlagsRaw&(int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference)!=0||ingredient.EditorID?.StartsWith("Test",StringComparison.OrdinalIgnoreCase)==true||before.Any(e=>e.Scripted);
                var value=preserve?ingredient.Data!.Value:alchemy.IngredientValues.GetValueOrDefault(key,Math.Max(ingredient.Data!.Value,(uint)alchemy.CommonIngredientValueFloor));
                var after=preserve?before:before.Select(e=>alchemy.IngredientReplacements.TryGetValue(e.Code,out var replacement)?e with{Code=replacement,Hostile=definitions.GetValueOrDefault(replacement)?.Data?.Flags.HasFlag(MagicEffect.MagicFlag.Hostile)==true}:e).ToArray();
                ingredients.Add(new{FormKey=key,ingredient.EditorID,Name=ingredient.Name?.ToString(),Before=before,After=after,BeforeValue=ingredient.Data!.Value,Value=value,Preserved=preserve,Rare=value>=alchemy.RareIngredientValue});
                if(!writeAlchemy||preserve)continue;var target=state.PatchMod.Ingredients.GetOrAddAsOverride(ingredient);target.Data!.Value=value;target.Data.Flags|=IngredientFlag.ManualValue;
                for(var i=0;i<after.Length;i++)if(after[i].Code!=before[i].Code)
                {
                    if(!definitions.ContainsKey(after[i].Code))throw new InvalidDataException("Unknown ingredient replacement: "+after[i].Code);
                    target.Effects[i].Data!.MagicEffect.SetTo(after[i].Code);
                }
                Changed(target,["Effects","Data.Value"]);
            }
            foreach(var tool in records.Values.OfType<IAlchemicalApparatusGetter>().Where(a=>!a.IsDeleted&&run.Includes(a.FormKey.ModKey)&&a.Data is not null).ToArray())
            {
                var preserve=!tool.Script.IsNull||protectedItems.Contains(tool.FormKey)||alchemy.PreserveItems.Contains(tool.FormKey.ToString())||Regex.IsMatch(tool.EditorID??"","^(CG|Test|SE[0-9]|MQ)",RegexOptions.IgnoreCase);
                var rank=tool.Data!.Quality>=100?4:tool.Data.Quality>=75?3:tool.Data.Quality>=50?2:tool.Data.Quality>=25?1:0;var quality=preserve?tool.Data.Quality:alchemy.ApparatusQualities[rank];
                apparatus.Add(new{FormKey=tool.FormKey.ToString(),tool.EditorID,Name=tool.Name?.ToString(),Type=tool.Data.Type.ToString(),Rank=rank,BeforeQuality=tool.Data.Quality,Quality=quality,Value=tool.Data.Value,Preserved=preserve});
                if(!writeAlchemy||preserve)continue;var target=(AlchemicalApparatus)state.PatchMod.GetTopLevelGroup<AlchemicalApparatus>().GetOrAddAsOverride(tool);target.Data!.Quality=(float)quality;Changed(target,["Data.Quality"]);
            }
            StockLimits();
        }
        void StockLimits()
        {
            int? Limit(FormKey root,HashSet<FormKey> path)
            {
                if(!path.Add(root)||path.Count>32)return null;
                return records.GetValueOrDefault(root) switch
                {
                    IIngredientGetter i=>i.Data?.Value>=alchemy.RareIngredientValue?alchemy.MerchantRareIngredientCount:alchemy.MerchantIngredientCount,
                    IPotionGetter p when p.Script.IsNull&&!protectedItems.Contains(root)=>alchemy.MerchantPotionCount,
                    IAlchemicalApparatusGetter=>1,
                    ILeveledItemGetter l when l.Entries is {Count:>0}=>l.Entries.Select(e=>Limit(e.Reference.FormKey,new(path))).All(v=>v.HasValue)?l.Entries.Select(e=>Limit(e.Reference.FormKey,new(path))!.Value).Min():null,
                    _=>null
                };
            }
            foreach(var container in records.Values.OfType<IContainerGetter>().Where(c=>c.EditorID?.StartsWith("BGSO_Merchant",StringComparison.Ordinal)==true).ToArray())
            {
                for(var i=0;i<container.Items.Count;i++)
                {
                    var item=container.Items[i];var limit=Limit(item.Item.FormKey,[]);if(limit is null||item.Count<=limit||item.Count<=0)continue;
                    stock.Add(new{Owner=container.FormKey.ToString(),Item=item.Item.FormKey.ToString(),Before=item.Count,After=limit.Value});if(!writeAlchemy)continue;
                    var target=state.PatchMod.Containers.GetOrAddAsOverride(container);target.Items[i].Count=(uint)limit.Value;Changed(target,["Items.Count"]);
                }
            }
            foreach(var npc in records.Values.OfType<INpcGetter>().Where(n=>state.PatchMod.Npcs.ContainsKey(n.FormKey)&&run.Includes(n.FormKey.ModKey)&&n.AIData?.BuySellServices.ToString().Contains("Ingredients")==true&&!n.IsDeleted).ToArray())
            {
                for(var i=0;i<npc.Items.Count;i++){var item=npc.Items[i];var limit=Limit(item.Item.FormKey,[]);if(limit is null||item.Count<=limit||item.Count<=0)continue;stock.Add(new{Owner=npc.FormKey.ToString(),Item=item.Item.FormKey.ToString(),Before=item.Count,After=limit.Value});if(!writeAlchemy)continue;var target=state.PatchMod.Npcs.GetOrAddAsOverride(npc);target.Items[i].Count=limit.Value;Changed(target,["Items.Count"]);}
            }
        }
        run.WriteReport(".enhancements.json",new{Schema=1,AppliedEnchantments=writeEnchant,AppliedAlchemy=writeAlchemy,EnchantmentSettings=enchant,AlchemySettings=alchemy,EngineSettings=engine,CustomEnchanting=enchanting,Items=items,SigilStones=sigils,Potions=potions,Ingredients=ingredients,Apparatus=apparatus,MerchantStock=stock,StandardClothing=equipment.OfType<IClothingGetter>().Where(c=>c.Enchantment.IsNull&&c.Script.IsNull&&!protectedItems.Contains(c.FormKey)).Select(c=>Physical(c,protectedItems)),EffectDefinitions=definitions.Values.Select(e=>new{Code=e.EditorID,Name=e.Name?.ToString(),School=e.Data?.MagicSchool.ToString(),BaseCost=e.Data?.BaseCost,ConstantFactor=e.Data?.SubData?.ConstantEffectEnchantmentFactor}),Notes=new[]{"Artifact effects retain native identities and scripts; uncurated protected items remain unchanged.","Custom exclusions affect future enchanting, not already-created save items or existing spell effects. Constant-effect caps are per item, not a runtime loadout cap.","Alchemy quality/value/active-potion settings affect future crafted potions; catalogue potion budgets do not hard-cap player-created effects.","Repeated weakness, enchantment charge rounding, poison delivery, crafted values and potion stacking need in-game calibration."}},EnhancementConfiguration.Options);
        run.Log($"Enhancements: {items.Count} enchanted/artifact items, {potions.Count} potions, {ingredients.Count} ingredients, {apparatus.Count} apparatus; {changes.Count} writes.");return changes;
    }
}