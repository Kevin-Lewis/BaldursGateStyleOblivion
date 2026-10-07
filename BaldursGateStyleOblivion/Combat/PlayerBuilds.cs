namespace BaldursGateStyleOblivion.Combat;

public sealed class PlayerProgression
{
    public double MasteryLevel { get; set; } = 20;
    public double FocusedMasteryLevel { get; set; } = 30;
    public double SupportingSkillPerLevelAfterMastery { get; set; } = 1.5;
    public double SecondarySkillAtMastery { get; set; } = 55;
    public double UntrainedSkillPerLevel { get; set; } = .5;
}
public sealed record CreationRace(string Key, string Name, Dictionary<string,double> Male, Dictionary<string,double> Female, Dictionary<string,double> Skills, Dictionary<string,double> Bonuses, BaldursGateStyleOblivion.Creation.CreationAbility[]? Abilities=null);
public sealed record CreationClass(string Name, string Specialization, string[] Attributes, string[] Skills);
public sealed record CreationSign(string Key, string Name, Dictionary<string,double> Bonuses, BaldursGateStyleOblivion.Creation.CreationAbility[]? Abilities=null);
public sealed record CreationCatalog(CreationRace[] Races, CreationClass[] Classes, CreationSign[] Birthsigns, Dictionary<string,double> Settings);
public sealed record PlayerBuildResult(Dictionary<string,double> Stats, string[] Notes);

public static class PlayerBuilds
{
    public static string[] AttributePriorities(GameplaySettings settings, string build)
    {
        var focused=settings.ActorBuilds[build].FocusedAttributes;
        return focused.Concat(settings.ActorBuilds[build].RotatingAttributes).ToArray();
    }
    public static PlayerBuildResult AtLevel(GameplaySettings settings, CreationCatalog catalog, string build, string raceKey, bool female, string? signKey, double level, string[]? attributes = null, string? alternateAttribute = null, string? primarySkill = null, string? creationClass = null)
    {
        var race=catalog.Races.SingleOrDefault(r=>r.Key==raceKey) ?? throw new ArgumentException("Choose a loaded playable race.");
        var cls=catalog.Classes.SingleOrDefault(c=>c.Name==(creationClass??settings.ActorBuilds.GetValueOrDefault(build)?.CreationClass??build)) ?? throw new ArgumentException("Choose a loaded playable class.");
        cls=cls with { Skills=cls.Skills.Select(s=>s.Replace("Speechraft","Speechcraft")).ToArray() };
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
        var levelHealth=0d;
        if(attributes is null)
        {
            var priorities=AttributePriorities(settings,build);
            attributes=priorities.Take(3).ToArray();alternateAttribute??=priorities[3];
        }
        if(attributes.Length!=3 || attributes.Distinct().Count()!=3 || attributes.Any(a=>!stats.ContainsKey(a)))throw new ArgumentException("Choose three distinct level-up attributes.");

        alternateAttribute=string.IsNullOrEmpty(alternateAttribute)?null:alternateAttribute;
        if(!string.IsNullOrEmpty(alternateAttribute) && (!stats.ContainsKey(alternateAttribute)||attributes.Contains(alternateAttribute)))throw new ArgumentException("The alternating attribute must be distinct from the first three.");
        var fallback=new[]{"Strength","Endurance","Agility","Speed","Intelligence","Willpower","Personality","Luck"};
        for(var gained=2;gained<=Math.Floor(level);gained++)
        {
            var alternate=!string.IsNullOrEmpty(alternateAttribute) && gained%2==1;
            var third=alternate?alternateAttribute!:attributes[2];
            var other=alternate?attributes[2]:alternateAttribute;
            var choices=attributes.Take(2).Concat(new[]{third}).Concat(other is null?[]:new[]{other}).Concat(fallback)
                .Distinct().Where(a=>stats[a]<100).Take(3).ToArray();
            foreach(var name in choices)stats[name]=Math.Min(100,stats[name]+(name=="Luck"?1:GS("iLevelUp01Mult",2)));
            levelHealth+=stats["Endurance"]*GS("fStatsHealthLevelMult",.1);
        }
        var profile=settings.ActorBuilds.GetValueOrDefault(build);
        primarySkill ??=profile?.PrimarySkill is {} preferred && cls.Skills.Contains(preferred)?preferred:profile?.Skills.FirstOrDefault(cls.Skills.Contains)??cls.Skills.First();
        if(!cls.Skills.Contains(primarySkill))throw new ArgumentException("Primary skill must be a major skill of the selected class.");
        var combat=new HashSet<string>(["Armorer","Athletics","Blade","Block","Blunt","HandToHand","HeavyArmor"]);
        var magic=new HashSet<string>(["Alchemy","Alteration","Conjuration","Destruction","Illusion","Mysticism","Restoration"]);
        var blend=Math.Clamp((level-1)/(progression.MasteryLevel-1),0,1);
        var focusedBlend=Math.Clamp((level-1)/(progression.FocusedMasteryLevel-1),0,1);
        var startingSkills=new Dictionary<string,double>();
        var focused=new HashSet<string>(profile?.PlayerFocusedSkills??[]);
        foreach(var skill in CombatBuilds.Skills)
        {
            var specialized=cls.Specialization==(combat.Contains(skill)?"Combat":magic.Contains(skill)?"Magic":"Stealth");
            var start=GS("iAVDSkillStart",5)+race.Skills.GetValueOrDefault(skill)+(cls.Skills.Contains(skill)?GS("iAVDMajorSkillBonus",20):0)+(specialized?GS("iAVDSpecializationBonus",5):0);
            startingSkills[skill]=Math.Clamp(start,0,100);
            stats[skill]=Math.Clamp(Math.Round(skill==primarySkill?start+(100-start)*blend:cls.Skills.Contains(skill)&&focused.Contains(skill)?start+(100-start)*focusedBlend:cls.Skills.Contains(skill)||specialized?start+(Math.Max(start,progression.SecondarySkillAtMastery)-start)*blend+Math.Max(0,level-progression.MasteryLevel)*progression.SupportingSkillPerLevelAfterMastery:start+(level-1)*progression.UntrainedSkillPerLevel),0,100);
        }
        // Major-skill estimates must fit ten increases per completed character level.
        var reserved=cls.Skills.Where(s=>s==primarySkill||focused.Contains(s)).Sum(s=>stats[s]-startingSkills[s]);
        var supporting=cls.Skills.Where(s=>s!=primarySkill&&!focused.Contains(s)).ToArray();
        var remaining=Math.Max(0,Math.Floor(level-1)*GS("iLevelUpSkillCount",10)-reserved);
        var requested=supporting.Sum(s=>Math.Max(0,stats[s]-startingSkills[s]));
        if(requested>remaining)
        {
            foreach(var skill in supporting)stats[skill]=startingSkills[skill]+Math.Floor((stats[skill]-startingSkills[skill])*remaining/requested);
            var leftover=(int)(remaining-supporting.Sum(s=>stats[s]-startingSkills[s]));
            foreach(var skill in supporting)if(leftover>0 && stats[skill]<100){stats[skill]++;leftover--;}
        }
        var health=stats["Endurance"]*GS("fPCBaseHealthMult",2)*GS("fStatsHealthStartMult",1);
        health+=levelHealth;
        stats["Health"]=Math.Round(health+race.Bonuses.GetValueOrDefault("Health")+(sign?.Bonuses.GetValueOrDefault("Health")??0),2);
        foreach(var key in race.Bonuses.Keys.Concat(sign?.Bonuses.Keys.AsEnumerable()??[]).Distinct().Where(k=>!stats.ContainsKey(k)))stats[key]=race.Bonuses.GetValueOrDefault(key)+(sign?.Bonuses.GetValueOrDefault(key)??0);
        stats["Fatigue"]=stats["Strength"]+stats["Endurance"]+stats["Agility"]+stats["Willpower"];
        stats["FatigueRegen"]=GS("fFatigueReturnBase",10)+stats["Endurance"]*GS("fFatigueReturnMult",0);
        return new(stats,["Level 1: loaded race, sex, class bonuses and passive attribute/health abilities.",$"Higher levels: two fixed attributes and the third selection optionally alternates; capped choices redirect to supporting attributes. Native +{GS("iLevelUp01Mult",2)} requires a related skill gain; untouched attributes and Luck stay +1. Skill mastery is estimated at {progression.MasteryLevel} for the primary and {progression.FocusedMasteryLevel} for the other heavily focused skills. Remaining major-skill growth fits the native leveling budget.","Active powers, equipment bonuses and runtime effects are not applied."]);
    }
}