namespace BaldursGateStyleOblivion.Combat;

public sealed class PlayerProgression
{
    public double MasteryLevel { get; set; } = 20;
    public double PrimaryAttributePerLevel { get; set; } = 2.5;
    public double OtherAttributePerLevel { get; set; } = 1;
    public double SecondarySkillAtMastery { get; set; } = 55;
    public double UntrainedSkillPerLevel { get; set; } = .5;
}
public sealed record CreationRace(string Key, string Name, Dictionary<string,double> Male, Dictionary<string,double> Female, Dictionary<string,double> Skills, Dictionary<string,double> Bonuses);
public sealed record CreationClass(string Name, string Specialization, string[] Attributes, string[] Skills);
public sealed record CreationSign(string Key, string Name, Dictionary<string,double> Bonuses);
public sealed record CreationCatalog(CreationRace[] Races, CreationClass[] Classes, CreationSign[] Birthsigns, Dictionary<string,double> Settings);
public sealed record PlayerBuildResult(Dictionary<string,double> Stats, string[] Notes);

public static class PlayerBuilds
{
    public static PlayerBuildResult AtLevel(GameplaySettings settings, CreationCatalog catalog, string build, string raceKey, bool female, string? signKey, double level)
    {
        var race=catalog.Races.SingleOrDefault(r=>r.Key==raceKey) ?? throw new ArgumentException("Choose a loaded playable race.");
        var cls=catalog.Classes.SingleOrDefault(c=>c.Name==build) ?? throw new ArgumentException("Choose a loaded playable class.");
        var sign=string.IsNullOrEmpty(signKey)?null:catalog.Birthsigns.SingleOrDefault(s=>s.Key==signKey) ?? throw new ArgumentException("Unknown birthsign.");
        var progression=settings.PlayerProgression;
        var stats=new Dictionary<string,double>(female?race.Female:race.Male);
        var creation=settings.GameSettings.Concat(catalog.Settings.Where(p=>!settings.GameSettings.ContainsKey(p.Key))).ToDictionary();
        double GS(string key,double fallback)=>creation.GetValueOrDefault(key,fallback);

        foreach(var key in stats.Keys.ToArray())
        {
            if(cls.Attributes.Contains(key))stats[key]+=GS("iAVDAttributeBonus",5);
            stats[key]+=race.Bonuses.GetValueOrDefault(key)+(sign?.Bonuses.GetValueOrDefault(key)??0);
        }
        var startingEndurance=stats["Endurance"];

        foreach(var key in stats.Keys.ToArray())
            stats[key]=Math.Clamp(stats[key]+(key=="Luck"?0:(level-1)*(cls.Attributes.Contains(key)?progression.PrimaryAttributePerLevel:progression.OtherAttributePerLevel)),1,100);
        var combat=new HashSet<string>(["Armorer","Athletics","Blade","Block","Blunt","HandToHand","HeavyArmor"]);
        var magic=new HashSet<string>(["Alchemy","Alteration","Conjuration","Destruction","Illusion","Mysticism","Restoration"]);
        var blend=Math.Clamp((level-1)/(progression.MasteryLevel-1),0,1);
        foreach(var skill in CombatBuilds.Skills)
        {
            var specialized=cls.Specialization==(combat.Contains(skill)?"Combat":magic.Contains(skill)?"Magic":"Stealth");
            var start=GS("iAVDSkillStart",5)+race.Skills.GetValueOrDefault(skill)+(cls.Skills.Contains(skill)?GS("iAVDMajorSkillBonus",20):0)+(specialized?GS("iAVDSpecializationBonus",5):0);
            stats[skill]=Math.Clamp(Math.Round(cls.Skills.Contains(skill)?start+(100-start)*blend:specialized?start+(Math.Max(start,progression.SecondarySkillAtMastery)-start)*blend:start+(level-1)*progression.UntrainedSkillPerLevel),0,100);
        }
        var health=startingEndurance*GS("fPCBaseHealthMult",2)*GS("fStatsHealthStartMult",1);
        for(var gained=2;gained<=Math.Floor(level);gained++)health+=Math.Min(100,startingEndurance+(gained-1)*(cls.Attributes.Contains("Endurance")?progression.PrimaryAttributePerLevel:progression.OtherAttributePerLevel))*GS("fStatsHealthLevelMult",.1);
        stats["Health"]=Math.Round(health+race.Bonuses.GetValueOrDefault("Health")+(sign?.Bonuses.GetValueOrDefault("Health")??0),2);
        stats["Fatigue"]=stats["Strength"]+stats["Endurance"]+stats["Agility"]+stats["Willpower"];
        stats["FatigueRegen"]=GS("fFatigueReturnBase",10)+stats["Endurance"]*GS("fFatigueReturnMult",0);
        return new(stats,["Level 1: loaded race, sex, class bonuses and passive attribute/health abilities.","Higher levels: editable development estimate; actual skill use and attribute choices vary.","Active powers, equipment bonuses and runtime effects are not applied."]);
    }
}