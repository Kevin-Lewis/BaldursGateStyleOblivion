namespace BaldursGateStyleOblivion.Combat;

public static class CombatBuilds
{
    public static readonly string[] Skills = ["Armorer","Athletics","Blade","Block","Blunt","HandToHand","HeavyArmor","Alchemy","Alteration","Conjuration","Destruction","Illusion","Mysticism","Restoration","Acrobatics","LightArmor","Marksman","Mercantile","Security","Sneak","Speechcraft"];
    public static Dictionary<string, double> AtLevel(GameplaySettings settings, string build, int tier, double level, IReadOnlyDictionary<int,int?> levels)
    {
        var lower=levels.GetValueOrDefault(tier); var upper=levels.GetValueOrDefault(tier+1);
        var blend=lower.HasValue && upper.HasValue && upper>lower ? Math.Clamp((level-lower.Value)/(upper.Value-lower.Value),0,1) : 0;
        var current=settings.ActorTiers[tier];var next=settings.ActorTiers.GetValueOrDefault(tier+1,current);
        double Mix(Func<ActorTierStats,double> field)=>field(current)+(field(next)-field(current))*blend;
        var profile=settings.ActorBuilds[build];
        double Attribute(double factor)=>Math.Clamp(Math.Round(Mix(t=>t.Attribute)*factor),1,100);
        var stats=new Dictionary<string,double>
        {
            ["Health"]=Math.Round(Mix(t=>t.Health)*profile.Health),["Strength"]=Attribute(profile.Strength),
            ["Endurance"]=Attribute(profile.Endurance),["Agility"]=Attribute(profile.Agility),["Willpower"]=Attribute(profile.Willpower),
            ["Speed"]=Attribute(profile.Speed),["Luck"]=50,["Intelligence"]=Attribute(build is "Mage" or "Battlemage" or "Spellsword" ? 1.2:.8)
        };
        foreach(var skill in Skills)
            stats[skill]=Math.Clamp(Math.Round(Mix(profile.Skills.Contains(skill)?t=>t.Specialty:profile.SupportingSkills.Contains(skill)?t=>t.Secondary:t=>t.Untrained)*profile.SkillMultiplier),0,100);
        stats["Fatigue"]=stats["Strength"]+stats["Endurance"]+stats["Agility"]+stats["Willpower"];
        stats["FatigueRegen"]=settings.GameSettings.GetValueOrDefault("fFatigueReturnBase",10)+stats["Endurance"]*settings.GameSettings.GetValueOrDefault("fFatigueReturnMult",0);
        return stats;
    }
}
