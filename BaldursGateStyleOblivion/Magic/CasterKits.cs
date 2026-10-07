using BaldursGateStyleOblivion.Combat;

namespace BaldursGateStyleOblivion.Magic;

public sealed record KitSpell(string Key,string Slot,string Rank,string School,int RequiredSkill,int MinimumTier,string Code,string Range,double Magnitude,double Duration,double BaseCost,string Metric);
public sealed record KitEntry(KitSpell Spell,double Skill,double EstimatedCost,double CastsFromPool);
public static class CasterKits
{
    public static readonly string[] Schools=["Alteration","Conjuration","Destruction","Illusion","Mysticism","Restoration"];
    public static string? Profile(MagicSettings settings,string build,string classId,string actorKey="")
    {
        if(settings.ActorOverrides.TryGetValue(actorKey,out var rule))
        {if(rule.Preserve)return null;if(rule.Profile is not null)return rule.Profile;}
        var assigned=settings.Profiles.FirstOrDefault(p=>p.Value.Build==build).Key;
        if(build!="Mage"&&assigned is not null)return assigned;
        foreach(var pair in settings.Profiles)
            if(pair.Value.ClassNames.Any(name=>classId.Contains(name,StringComparison.OrdinalIgnoreCase)))return pair.Key;
        return assigned;
    }
    public static KitSpell[] Spells(MagicSettings settings)=>settings.Slots.SelectMany(pair=>settings.Ranks.Select((rank,index)=>
    {
        var slot=pair.Value;
        var magnitude=slot.Metric switch{"Damage"=>rank.Damage,"Healing"=>rank.Healing,"Shield"=>rank.Shield,_=>0};
        var duration=slot.Metric switch{"Damage" or "Healing"=>1,"Summon"=>rank.SummonDuration,"Paralyze"=>rank.ParalyzeDuration,"Silence"=>rank.SilenceDuration,_=>rank.Duration};
        return new KitSpell($"{pair.Key}_{rank.Name}",pair.Key,rank.Name,slot.School,Math.Max(rank.RequiredSkill,slot.MinimumSkill),rank.MinimumTier,slot.EffectCodes[index],slot.Range,Math.Round(magnitude),Math.Round(duration),Math.Round(rank.BaseCost*slot.CostMultiplier),slot.Metric);
    })).ToArray();
    public static double Cost(KitSpell spell,double skill,IReadOnlyDictionary<string,double> settings)=>Math.Max(1,spell.BaseCost*(settings.GetValueOrDefault("fMagicCasterSkillCostBase",.2)+(1-Math.Clamp(skill,0,100)/100)*settings.GetValueOrDefault("fMagicCasterSkillCostMult",1.2)));
    public static KitEntry[] Select(MagicSettings settings,string profile,IReadOnlyDictionary<string,double> stats,double pool,int tier,IReadOnlyDictionary<string,double> gameSettings,bool allowRare=false,KitSpell[]? spells=null)
    {
        if(!settings.Profiles.TryGetValue(profile,out var definition))throw new ArgumentException("Unknown caster profile.");
        var available=spells??Spells(settings);var result=new List<KitEntry>();
        foreach(var slot in definition.Slots)
        {
            var candidates=available.Where(s=>s.Slot==slot&&stats.GetValueOrDefault(s.School)>=s.RequiredSkill&&s.MinimumTier<=tier&&(s.MinimumTier==0||allowRare))
                .OrderByDescending(s=>s.MinimumTier).ThenByDescending(s=>s.RequiredSkill).ThenByDescending(s=>s.BaseCost);
            foreach(var spell in candidates)
            {
                var skill=stats.GetValueOrDefault(spell.School);var cost=Cost(spell,skill,gameSettings);
                if(cost>pool*settings.MaximumPoolFraction)continue;
                result.Add(new(spell,skill,cost,Math.Floor(pool/cost)));break;
            }
        }
        return result.ToArray();
    }
    public static PlayerBuildResult Build(CombatSettings combat,CreationCatalog creation,MagicSettings settings,string profile,string race,bool female,string? sign,double level)
    {
        if(!settings.Profiles.TryGetValue(profile,out var definition))throw new ArgumentException("Unknown caster profile.");
        return PlayerBuilds.AtLevel(combat.Gameplay,creation,definition.Build,race,female,sign,level);
    }
}