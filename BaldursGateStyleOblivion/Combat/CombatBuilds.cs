namespace BaldursGateStyleOblivion.Combat;

public static class CombatBuilds
{
    public static readonly string[] Skills = ["Armorer","Athletics","Blade","Block","Blunt","HandToHand","HeavyArmor","Alchemy","Alteration","Conjuration","Destruction","Illusion","Mysticism","Restoration","Acrobatics","LightArmor","Marksman","Mercantile","Security","Sneak","Speechcraft"];
    public static Dictionary<string,double> ActorAtLevel(GameplaySettings settings, CreationCatalog catalog, string build, string raceKey, bool female, int tier, double level, IReadOnlyDictionary<int,int?> levels, string? creationClass = null)
    {
        if(!catalog.Races.Any(r=>r.Key==raceKey) || !catalog.Classes.Any(c=>c.Name==(creationClass??settings.ActorBuilds.GetValueOrDefault(build)?.CreationClass??build)))
            return AtLevel(settings,build,tier,level,levels);
        // NPC racial abilities remain runtime effects; do not bake them into stored stats twice.
        var baseCatalog=catalog with { Races=catalog.Races.Select(r=>r with { Bonuses=new() }).ToArray() };
        var priorities=PlayerBuilds.AttributePriorities(settings,build);
        var stats=PlayerBuilds.AtLevel(settings,baseCatalog,build,raceKey,female,null,level,priorities.Take(3).ToArray(),priorities[3],creationClass:creationClass).Stats;
        stats["Health"]=Math.Round(stats["Health"]*settings.ActorBuilds[build].Health);
        return stats;
    }
    public static Dictionary<string, double> AtLevel(GameplaySettings settings, string build, int tier, double level, IReadOnlyDictionary<int,int?> levels)
    {
        var lower=levels.GetValueOrDefault(tier); var upper=levels.GetValueOrDefault(tier+1);
        var blend=lower.HasValue && upper.HasValue && upper>lower ? Math.Clamp((level-lower.Value)/(upper.Value-lower.Value),0,1) : 0;
        var current=settings.ActorTiers[tier];var next=settings.ActorTiers.GetValueOrDefault(tier+1,current);
        double Mix(Func<ActorTierStats,double> field)=>field(current)+(field(next)-field(current))*blend;
        var profile=settings.ActorBuilds[build];
        double Attribute(string name,double factor)=>Math.Clamp(Math.Round(Mix(profile.FocusedAttributes.Contains(name)?t=>t.Attribute:t=>t.SupportingAttribute??t.Attribute)*factor),1,100);
        var stats=new Dictionary<string,double>
        {
            ["Health"]=Math.Round(Attribute("Endurance",profile.Endurance)*settings.GameSettings.GetValueOrDefault("fPCBaseHealthMult",2)*profile.Health),["Strength"]=Attribute("Strength",profile.Strength),
            ["Endurance"]=Attribute("Endurance",profile.Endurance),["Agility"]=Attribute("Agility",profile.Agility),["Willpower"]=Attribute("Willpower",profile.Willpower),
            ["Speed"]=Attribute("Speed",profile.Speed),["Luck"]=50,["Intelligence"]=Attribute("Intelligence",profile.Intelligence),["Personality"]=Attribute("Personality",profile.Personality)
        };
        foreach(var skill in Skills)
            stats[skill]=Math.Clamp(Math.Round(Mix(skill==(profile.PrimarySkill??profile.Skills.FirstOrDefault())?t=>t.Specialty:profile.Skills.Contains(skill)?t=>t.Focused??t.Specialty:profile.SupportingSkills.Contains(skill)?t=>t.Secondary:t=>t.Untrained)*profile.SkillMultiplier),0,100);
        stats["Fatigue"]=stats["Strength"]+stats["Endurance"]+stats["Agility"]+stats["Willpower"];
        stats["FatigueRegen"]=settings.GameSettings.GetValueOrDefault("fFatigueReturnBase",10)+stats["Endurance"]*settings.GameSettings.GetValueOrDefault("fFatigueReturnMult",0);
        return stats;
    }
}
