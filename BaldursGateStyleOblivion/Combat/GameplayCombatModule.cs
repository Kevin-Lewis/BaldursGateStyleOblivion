using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Combat;

internal static class GameplayCombatModule
{
    internal static string Build(ActorProfile? profile, string? classId)
    {
        var role=profile?.Dimensions.GetValueOrDefault("CombatRole")?.Selected.Value?.ToString();
        if(role is "Civilian")return "Civilian";
        if(role is "Mage" or "Support" or "Controller")return "Mage";
        if(role is "Archer")return "Scout";
        if(role is "Tank")return "Knight";
        if(role is "Bruiser")return "Barbarian";
        if(role is "Skirmisher")return "Rogue";
        if(role is "Battlemage")return "Battlemage";
        var id=classId??"";
        foreach(var name in new[]{"Battlemage","Spellsword","Knight","Barbarian","Warrior","Scout","Rogue"})
            if(id.Contains(name,StringComparison.OrdinalIgnoreCase))return name;
        if(Regex.IsMatch(id,"Mage|Necromancer|Conjurer|Sorcerer|Witch",RegexOptions.IgnoreCase))return "Mage";
        if(Regex.IsMatch(id,"Agent|Assassin|Thief|Acrobat",RegexOptions.IgnoreCase))return "Rogue";
        return "Warrior";
    }
    internal static void Stats(Npc target, Dictionary<string,double> stats)
    {
        var data=target.Stats!;
        foreach(var pair in stats)
        {
            var property=typeof(NpcData).GetProperty(pair.Key);
            if(property?.PropertyType==typeof(byte))property.SetValue(data,(byte)Math.Clamp(pair.Value,0,100));
            else if(property?.PropertyType==typeof(uint))property.SetValue(data,(uint)pair.Value);
        }
        target.Configuration!.Fatigue=(ushort)stats["Fatigue"];
        target.Configuration.BaseSpellPoints=(ushort)Math.Max(target.Configuration.BaseSpellPoints,stats["Intelligence"]*2);
        target.Configuration.Flags&=~Npc.NpcFlag.AutoCalcStats;
    }
    public static Dictionary<FormKey,string[]> Run(IPatcherState<IOblivionMod,IOblivionModGetter> state,
        Dictionary<FormKey,IMajorRecordGetter> records,IReadOnlyDictionary<FormKey,ActorProfile> profiles,
        CombatSettings settings,PatcherRun run)
    {
        var gameplay=settings.Gameplay;var write=run.Settings.EnablePhysicalCombatBalance&&!run.Settings.ReportOnly;
        var changes=new Dictionary<FormKey,string[]>();var plans=new List<object>();var skipped=new List<object>();
        if(gameplay.BalanceGameSettings)
            foreach(var pair in gameplay.GameSettings)
            {
                var source=records.Values.OfType<IGameSettingFloatGetter>().FirstOrDefault(r=>r.EditorID==pair.Key&&!r.IsDeleted);
                plans.Add(new{Kind="GameSetting",EditorID=pair.Key,Before=source?.Data,After=pair.Value});
                if(write)
                {
                    var target=source is null?state.PatchMod.GameSettings.AddNewFloat():(GameSettingFloat)state.PatchMod.GameSettings.GetOrAddAsOverride((IGameSettingGetter)source);
                    target.EditorID=pair.Key;target.Data=(float)pair.Value;records[target.FormKey]=target;changes[target.FormKey]=["Data"];
                }
            }
        var styles=new Dictionary<(FormKey,string),FormKey>();var weapons=new Dictionary<(FormKey,int),FormKey>();
        if(!gameplay.BalanceActors){Finish();return changes;}
        // Reuse the current deleveling pass's script and quest safeguards. Never trust stale reports.
        var allowed=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        if(run.Settings.EnableActorDeleveling&&File.Exists(run.ReportPath(".actor-deleveling.json")))
        {
            using var report=JsonDocument.Parse(File.ReadAllText(run.ReportPath(".actor-deleveling.json")));
            foreach(var row in report.RootElement.GetProperty("Actors").EnumerateArray())
                if(row.GetProperty("TargetLevel").ValueKind==JsonValueKind.Number&&row.GetProperty("PowerTier").ValueKind==JsonValueKind.Number)
                    allowed[row.GetProperty("FormKey").GetString()!]=row.GetProperty("TargetLevel").GetInt32();
        }
        var artifacts=Modules.RewardRecords.Protected(Modules.RewardConfiguration.Load(Modules.RewardRecords.PathFor(run)));
        FormKey Style(FormKey original,string name)
        {
            if(styles.TryGetValue((original,name),out var cached))return cached;
            var target=records.GetValueOrDefault(original) is ICombatStyleGetter source?state.PatchMod.CombatStyles.DuplicateInAsNewRecord(source):state.PatchMod.CombatStyles.AddNew();
            target.EditorID=$"BGSOCombatStyle{styles.Count}_{Regex.Replace(name,"[^A-Za-z]","")}";
            target.Data??=new CombatStyleData();var data=target.Data;var profile=gameplay.NativeStyles[name];
            data.AttackPercentChance=profile.AttackChance;data.BlockPercentChance=profile.BlockChance;data.PowerAttackPercentChance=profile.PowerAttackChance;data.DodgePercentChance=profile.DodgeChance;
            data.IdleTimerMin=(float)profile.IdleMin;data.IdleTimerMax=(float)profile.IdleMax;data.HoldTimerMin=(float)profile.HoldMin;data.HoldTimerMax=(float)profile.HoldMax;
            styles[(original,name)]=target.FormKey;records[target.FormKey]=target;return target.FormKey;
        }
        FormKey Weapon(FormKey original,int tier,HashSet<FormKey> path)
        {
            if(weapons.TryGetValue((original,tier),out var cached))return cached;
            if(!path.Add(original)||path.Count>32)return original;
            var result=original;
            if(records.GetValueOrDefault(original) is IWeaponGetter source && source.Data is not null && source.Data.Damage > 0 && source.Data.Type.ToString() != "Staff")
            {
                var item=PhysicalCombatModule.ReadItem(source,artifacts)!;
                if(!item.Protected||item.PreservationReason=="Enchanted item: enchantment balance pending")
                {
                    var copy=state.PatchMod.Weapons.DuplicateInAsNewRecord(source);copy.EditorID=$"BGSOCombatTier{tier}_{source.FormKey.ID:X6}_{source.EditorID}";
                    copy.Data!.Damage=(ushort)Math.Clamp(Math.Round(copy.Data.Damage*gameplay.WeaponPower(tier)),1,ushort.MaxValue);
                    records[copy.FormKey]=copy;result=copy.FormKey;
                }
            }
            else if(records.GetValueOrDefault(original) is ILeveledItemGetter list)
            {
                var copy=state.PatchMod.LeveledItems.DuplicateInAsNewRecord(list);var changed=false;
                foreach(var entry in copy.Entries??[])
                {
                    var key=Weapon(entry.Reference.FormKey,tier,path);
                    if(key!=entry.Reference.FormKey){entry.Reference.SetTo(key);changed=true;}
                }
                if(changed){copy.EditorID=$"BGSOCombatTier{tier}_List{weapons.Count}";records[copy.FormKey]=copy;result=copy.FormKey;}
                else state.PatchMod.LeveledItems.Remove(copy.FormKey);
            }
            path.Remove(original);weapons[(original,tier)]=result;return result;
        }
        var levels=ActorClassification.LoadSettings(run).LevelMapping;
        foreach(var actor in records.Values.Where(r=>r is INpcGetter or ICreatureGetter).OrderBy(r=>r.FormKey.ToString(),StringComparer.Ordinal).ToArray())
        {
            var key=actor.FormKey.ToString();var tier=profiles.GetValueOrDefault(actor.FormKey)?.Tier?.Value;
            gameplay.ActorOverrides.TryGetValue(key,out var individual);
            if(actor.IsDeleted||!run.Includes(actor.FormKey.ModKey)||individual?.Preserve==true||tier is null||!allowed.TryGetValue(key,out var level))
            {skipped.Add(new{FormKey=key,Reason="Unclassified, exempt, or deleveling safeguard"});continue;}
            if(actor is INpcGetter npc && npc.Stats is not null && npc.Configuration is not null)
            {
                var build=individual?.Build??Build(profiles.GetValueOrDefault(actor.FormKey),(records.GetValueOrDefault(npc.Class.FormKey) as IClassGetter)?.EditorID);
                var stats=CombatBuilds.AtLevel(gameplay,build,tier.Value,level,levels);if(individual?.Health is not null)stats["Health"]=individual.Health.Value;
                var style=gameplay.ActorBuilds[build].Style;
                if(profiles.GetValueOrDefault(actor.FormKey)?.Dimensions.GetValueOrDefault("BossStatus")?.Selected.Value?.ToString()=="Major" && build is not "Mage" and not "Scout") style="Boss";
                plans.Add(new{Kind="NPC",FormKey=key,Tier=tier,Level=level,Build=build,Stats=stats,Style=style,WeaponPower=gameplay.WeaponPower(tier.Value)});
                if(!write)continue;
                var target=state.PatchMod.Npcs.GetOrAddAsOverride(npc);Stats(target,stats);target.CombatStyle.SetTo(Style(npc.CombatStyle.FormKey,style));
                if(tier>6)foreach(var entry in target.Items)entry.Item.SetTo(Weapon(entry.Item.FormKey,tier.Value,new()));
                records[target.FormKey]=target;changes[target.FormKey]=["Stats","Configuration.Fatigue","Configuration.Flags.AutoCalcStats","Configuration.BaseSpellPoints","CombatStyle","Items"];
            }
            else if(actor is ICreatureGetter creature && creature.Data is not null && creature.Configuration is not null)
            {
                var name=creature.EditorID??"";var type=creature.Data.Type.ToString();
                var build=Regex.IsMatch(name,"Ogre|Minotaur|Troll|Daedroth|Xivilai|Gatekeeper|Clannfear",RegexOptions.IgnoreCase)?"Large":type is "Daedra" or "Undead"?"Supernatural":Regex.IsMatch(name,"Rat|Deer|Imp",RegexOptions.IgnoreCase)?"Frail":"Default";
                var budget=gameplay.ActorTiers[tier.Value];var factor=gameplay.CreatureBuilds[build];
                var health=individual?.Health??Math.Round(budget.Health*factor.Health);var damage=Math.Round(budget.NaturalDamage*factor.Damage);var speed=Math.Clamp(budget.Attribute*factor.Speed,5,100);
                plans.Add(new{Kind="Creature",FormKey=key,Tier=tier,Level=level,Build=build,Health=health,AttackDamage=damage,Speed=speed});
                if(!write)continue;
                var target=state.PatchMod.Creatures.GetOrAddAsOverride(creature);target.Configuration!.Flags&=~Creature.CreatureFlag.PCLevelOffset;target.Configuration.LevelOffset=(short)level;target.Configuration.CalcMin=target.Configuration.CalcMax=0;
                target.Data!.Health=(uint)health;target.Data.AttackDamage=(ushort)damage;target.Data.CombatSkill=(byte)budget.Specialty;target.Data.Speed=(byte)speed;
                target.Data.Strength=target.Data.Endurance=(byte)budget.Attribute;target.Configuration.Fatigue=(ushort)(budget.Attribute*4);
                var role=profiles.GetValueOrDefault(actor.FormKey)?.Dimensions.GetValueOrDefault("CombatRole")?.Selected.Value?.ToString();
                var style=role is "Mage" or "Support" or "Controller"?"Mage":role=="Archer"?"Archer":build=="Large"?"Berserker":"Aggressive Fighter";
                target.CombatStyle.SetTo(Style(creature.CombatStyle.FormKey,style));
                if(tier>6)foreach(var entry in target.Items)entry.Item.SetTo(Weapon(entry.Item.FormKey,tier.Value,new()));
                records[target.FormKey]=target;changes[target.FormKey]=["Data.Health","Data.AttackDamage","Data.CombatSkill","Data.Speed","Data.Strength","Data.Endurance","Configuration","CombatStyle","Items"];
            }
        }
        Finish();return changes;
        void Finish()
        {
            run.WriteReport(".combat-gameplay.json",new{Schema=1,Applied=write,Plans=plans,Skipped=skipped,Changes=changes.Select(p=>new{FormKey=p.Key.ToString(),Fields=p.Value}),RareWeaponVariants=weapons!.Where(p=>p.Key.Item1!=p.Value).Select(p=>p.Value).Distinct().Count(),NativeStyleVariants=styles!.Count},CombatConfiguration.Options);
            run.Log($"Combat gameplay: {plans.Count} plans, {changes.Count} writes; {skipped.Count} safeguarded actors.");
        }
    }
}
