using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaldursGateStyleOblivion.Magic;

public sealed class CasterProfile
{
    public string Build { get; set; } = "Mage";
    public string[] ClassNames { get; set; } = [];
    public string[] Slots { get; set; } = [];
    public string Description { get; set; } = "";
}
public sealed class MagicRank
{
    public string Name { get; set; } = "Novice";
    public int RequiredSkill { get; set; }
    public int MinimumTier { get; set; }
    public double Damage { get; set; }
    public double Healing { get; set; }
    public double Shield { get; set; }
    public double Duration { get; set; }
    public double ParalyzeDuration { get; set; } = 2;
    public double SilenceDuration { get; set; } = 5;
    public double SummonDuration { get; set; }
    public double BaseCost { get; set; }
}
public sealed class SpellSlot
{
    public string School { get; set; } = "Destruction";
    public string[] EffectCodes { get; set; } = [];
    public string Range { get; set; } = "Target";
    public string Metric { get; set; } = "Damage";
    public int MinimumSkill { get; set; }
    public double CostMultiplier { get; set; } = 1;
}
public sealed class CasterOverride
{
    public bool Preserve { get; set; }
    public string? Profile { get; set; }
    public double? Magicka { get; set; }
    public bool AllowRareSpells { get; set; }
}
public sealed class MagicSettings
{
    public bool Enabled { get; set; } = true;
    public bool AddToSpellVendors { get; set; } = true;
    public bool ReplaceOrdinaryCombatSpells { get; set; } = true;
    public double NPCMagickaPerIntelligence { get; set; } = 2;
    public double MaximumPoolFraction { get; set; } = .5;
    public double CastInterval { get; set; } = 1.5;
    public Dictionary<string,double> GameSettings { get; set; } = new();
    public Dictionary<string,CasterProfile> Profiles { get; set; } = new();
    public Dictionary<string,SpellSlot> Slots { get; set; } = new();
    public MagicRank[] Ranks { get; set; } = [];
    public Dictionary<string,CasterOverride> ActorOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public static class MagicConfiguration
{
    public static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public static MagicSettings Load(string path)=>Parse(File.ReadAllText(path));
    public static MagicSettings Parse(string text)
    {
        var settings=JsonSerializer.Deserialize<MagicSettings>(text,Options)??throw new ArgumentException("Magic settings missing.");
        Validate(settings);return settings;
    }
    public static void Validate(MagicSettings settings)
    {
        void Range(double value,double min,double max,string field){if(!double.IsFinite(value)||value<min||value>max)throw new ArgumentException($"{field} must be {min}–{max}.");}
        Range(settings.NPCMagickaPerIntelligence,.5,10,"NPC base magicka / Intelligence");
        Range(settings.MaximumPoolFraction,.05,1,"Maximum spell cost / pool");Range(settings.CastInterval,.5,10,"Casting interval");
        if(settings.GameSettings is null)throw new ArgumentException("Magic engine settings are required.");
        foreach(var pair in settings.GameSettings)
        {
            if(!new[]{"fPCBaseMagickaMult","fNPCBaseMagickaMult","fMagickaReturnBase","fMagickaReturnMult","fMagicCasterSkillCostBase","fMagicCasterSkillCostMult"}.Contains(pair.Key))throw new ArgumentException("Unsupported magic engine setting: "+pair.Key);
            Range(pair.Value,0,10,pair.Key);
        }
        if(settings.Profiles is null||settings.Slots is null||settings.Ranks is null||settings.ActorOverrides is null||settings.Ranks.Length==0)throw new ArgumentException("Magic tables are required.");
        if(settings.Ranks.Select(r=>r.Name).Distinct().Count()!=settings.Ranks.Length)throw new ArgumentException("Rank names must be unique.");
        foreach(var rank in settings.Ranks)
        {
            if(rank is null||string.IsNullOrWhiteSpace(rank.Name))throw new ArgumentException("Rank name missing.");
            Range(rank.RequiredSkill,0,100,"Required skill");if(!new[]{0,25,50,75,100}.Contains(rank.RequiredSkill))throw new ArgumentException("Required skill must match a native mastery threshold (0/25/50/75/100).");Range(rank.MinimumTier,0,10,"Minimum tier");
            foreach(var value in new[]{rank.Damage,rank.Healing,rank.Shield})Range(value,0,1000,"Spell magnitude");
            Range(rank.ParalyzeDuration,1,30,"Paralysis duration");Range(rank.SilenceDuration,1,120,"Silence duration");Range(rank.Shield,0,85,"Shield magnitude");Range(rank.Duration,1,300,"Duration");Range(rank.SummonDuration,1,300,"Summon duration");Range(rank.BaseCost,1,10000,"Base cost");
        }
        foreach(var slot in settings.Slots.Values)
        {
            if(slot is null||!CasterKits.Schools.Contains(slot.School)||slot.EffectCodes is null||slot.EffectCodes.Length!=settings.Ranks.Length)throw new ArgumentException("Each slot needs a school and one effect code per rank.");
            if(slot.Range is not "Self" and not "Touch" and not "Target"||slot.Metric is not "Damage" and not "Healing" and not "Shield" and not "Summon" and not "Paralyze" and not "Silence" and not "Invisibility")throw new ArgumentException("Unsupported slot range or metric.");
            Range(slot.MinimumSkill,0,100,"Slot minimum skill");if(!new[]{0,25,50,75,100}.Contains(slot.MinimumSkill))throw new ArgumentException("Slot minimum skill must match a native mastery threshold.");
            var school=slot.Metric switch{"Damage"=>"Destruction","Healing"=>"Restoration","Shield"=>"Alteration","Summon"=>"Conjuration",_=>"Illusion"};
            if(slot.School!=school||slot.EffectCodes.Any(code=>slot.Metric switch{"Damage"=>code is not "FIDG" and not "FRDG" and not "SHDG" and not "DGHE","Healing"=>code!="REHE","Shield"=>code!="SHLD","Summon"=>code is null||!code.StartsWith("Z",StringComparison.Ordinal),"Paralyze"=>code!="PARA","Silence"=>code!="SLNC",_=>code!="INVI"}))throw new ArgumentException("Slot effect codes and school must match its metric.");Range(slot.CostMultiplier,.1,10,"Slot cost multiplier");
        }
        foreach(var profile in settings.Profiles.Values)
            if(profile is null||string.IsNullOrWhiteSpace(profile.Build)||profile.Slots is null||profile.ClassNames is null||profile.Slots.Distinct().Count()!=profile.Slots.Length||profile.Slots.Any(s=>!settings.Slots.ContainsKey(s)))throw new ArgumentException("Invalid caster profile or spell slots.");
        foreach(var rule in settings.ActorOverrides.Values)
            if(rule is null||rule.Profile is not null&&!settings.Profiles.ContainsKey(rule.Profile)||rule.Magicka is {} magicka&&(!double.IsFinite(magicka)||magicka<1||magicka>10000))throw new ArgumentException("Unknown actor caster profile.");
    }
}