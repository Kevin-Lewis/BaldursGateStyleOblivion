using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
namespace BaldursGateStyleOblivion.Creation;
public sealed record CreationEffect(int Index,string Code,string ActorValue,double Magnitude,double Duration,string Range,double Area,bool Scripted);
public sealed record CreationAbility(string Key,string EditorID,string Name,string Type,double Cost,CreationEffect[] Effects);
public sealed class CreationEffectEdit
{
    public string Code { get; set; } = "";
    public string ActorValue { get; set; } = "";
    public double? Magnitude { get; set; }
    public double? Duration { get; set; }
}
public sealed class CreationSpellEdit
{
    public string Reason { get; set; } = "";
    public Dictionary<int,CreationEffectEdit> Effects { get; set; } = new();
}
public sealed class CreationRaceEdit
{
    public Dictionary<string,double> Male { get; set; } = new();
    public Dictionary<string,double> Female { get; set; } = new();
    public Dictionary<string,double> Skills { get; set; } = new();
}
public sealed class CreationClassEdit
{
    public string? Specialization { get; set; }
    public string[]? Attributes { get; set; }
    public string[]? Skills { get; set; }
}
public sealed class CreationSettings
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string,CreationRaceEdit> Races { get; set; } = new();
    public Dictionary<string,CreationClassEdit> Classes { get; set; } = new();
    public Dictionary<string,CreationSpellEdit> Spells { get; set; } = new();
}
public static class CreationBalance
{
    public static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    public static readonly string[] Attributes=["Strength","Intelligence","Willpower","Agility","Speed","Endurance","Personality","Luck"];
    public static CreationSettings Load(string path)=>Parse(File.ReadAllText(path));
    public static CreationSettings Parse(string text){var s=JsonSerializer.Deserialize<CreationSettings>(text,Options)??throw new ArgumentException("Character creation settings missing.");Validate(s);return s;}
    public static void Validate(CreationSettings s,CreationCatalog? catalog=null)
    {
        if(s.Races is null||s.Classes is null||s.Spells is null)throw new ArgumentException("Character creation tables missing.");
        void Values(Dictionary<string,double>? values,IEnumerable<string> keys,double max,string name){if(values is null)throw new ArgumentException(name+" missing.");foreach(var p in values)if(!keys.Contains(p.Key)||!double.IsFinite(p.Value)||p.Value<0||p.Value>max||p.Value!=Math.Floor(p.Value))throw new ArgumentException("Invalid "+name+": "+p.Key);}
        foreach(var p in s.Races){var r=p.Value??throw new ArgumentException("Race edit missing.");Values(r.Male,Attributes,100,"male attribute");Values(r.Female,Attributes,100,"female attribute");Values(r.Skills,CombatBuilds.Skills,25,"racial skill bonus");if(catalog is not null){var race=catalog.Races.FirstOrDefault(r=>r.Key==p.Key)??throw new ArgumentException("Unknown playable race.");if(r.Skills.Keys.Any(k=>!race.Skills.ContainsKey(k)))throw new ArgumentException("Only existing racial skill bonuses can be edited.");}}
        foreach(var p in s.Classes)
        {
            var c=p.Value??throw new ArgumentException("Class edit missing.");if(c.Specialization is not null&&!new[]{"Combat","Magic","Stealth"}.Contains(c.Specialization))throw new ArgumentException("Unknown specialization.");
            if(c.Attributes is not null&&(c.Attributes.Length!=2||c.Attributes.Distinct().Count()!=2||c.Attributes.Any(a=>!Attributes.Contains(a))))throw new ArgumentException("Choose two distinct favored attributes.");
            if(c.Skills is not null&&(c.Skills.Length!=7||c.Skills.Distinct().Count()!=7||c.Skills.Any(a=>!CombatBuilds.Skills.Contains(a))))throw new ArgumentException("Choose seven distinct major skills.");
            if(catalog is not null&&!catalog.Classes.Any(c=>c.Name==p.Key))throw new ArgumentException("Unknown playable class.");
        }
        var abilities=catalog?.Races.SelectMany(r=>r.Abilities??[]).Concat(catalog.Birthsigns.SelectMany(s=>s.Abilities??[])).DistinctBy(a=>a.Key).ToDictionary(a=>a.Key);
        foreach(var p in s.Spells)
        {
            var spell=p.Value??throw new ArgumentException("Ability edit missing.");if(spell.Effects is null)throw new ArgumentException("Ability effects missing.");
            if(abilities is not null&&!abilities.ContainsKey(p.Key))throw new ArgumentException("Ability is not attached to a playable race or birthsign.");
            foreach(var e in spell.Effects)
            {
                if(e.Value is null||e.Key<0||string.IsNullOrWhiteSpace(e.Value.Code)||string.IsNullOrWhiteSpace(e.Value.ActorValue))throw new ArgumentException("Ability effect identity missing.");
                foreach(var v in new[]{e.Value.Magnitude,e.Value.Duration})if(v.HasValue&&(!double.IsFinite(v.Value)||v.Value<0||v.Value>1000||v.Value!=Math.Floor(v.Value)))throw new ArgumentException("Ability values must be whole numbers between 0 and 1000.");
                if(abilities is not null)
                {
                    var ability=abilities[p.Key];
                    var f=ability.Effects.FirstOrDefault(f=>f.Index==e.Key);
                    // Vanilla Nord Shield uses Health; UOP corrects this unused selector to DefendBonus.
                    var shieldMetadata=f?.Code=="SHLD" && (f.ActorValue is "Health" or "DefendBonus") && (e.Value.ActorValue is "Health" or "DefendBonus");
                    if(f is null||f.Code!=e.Value.Code||(!shieldMetadata&&f.ActorValue!=e.Value.ActorValue)||f.Scripted)
                        throw new ArgumentException($"Preserve native effect identities and scripts: {ability.Name} ({p.Key}), effect {e.Key}; expected {e.Value.Code}/{e.Value.ActorValue}, found {f?.Code??"missing"}/{f?.ActorValue??"missing"}{(f?.Scripted==true?" (scripted)":"")}.");
                    if(ability.Type=="Ability"&&e.Value.Duration is >0)throw new ArgumentException("Passive abilities must remain constant.");
                }
            }
        }
    }
    public static CreationAbility Apply(CreationAbility source,CreationSettings settings)
    {
        if(!settings.Enabled||!settings.Spells.TryGetValue(source.Key,out var rule))return source;
        return source with{Effects=source.Effects.Select(f=>rule.Effects.TryGetValue(f.Index,out var edit)?f with{Magnitude=edit.Magnitude??f.Magnitude,Duration=edit.Duration??f.Duration}:f).ToArray()};
    }
    public static Dictionary<string,double> Bonuses(IEnumerable<CreationAbility> abilities)
    {
        var result=new Dictionary<string,double>();
        foreach(var f in abilities.Where(a=>a.Type=="Ability").SelectMany(a=>a.Effects))
        {
            var key=f.Code switch{"FOAT" or "FRAT"=>f.ActorValue,"FOHE"=>"Health","FOSP"=>"Magicka","STMA"=>"StuntedMagicka","RSMA" or "WKMA"=>"ResistMagic","RSFI" or "WKFI"=>"ResistFire","RSFR" or "WKFR"=>"ResistFrost","RSSH" or "WKSH"=>"ResistShock","RSPO"=>"ResistPoison","RSDI"=>"ResistDisease","SABS"=>"SpellAbsorption","REDG"=>"ReflectDamage","RFLC"=>"ReflectSpell",_=>null};
            if(key is not null)result[key]=result.GetValueOrDefault(key)+(key=="StuntedMagicka"?1:f.Magnitude*(f.Code.StartsWith("WK")?-1:1));
        }
        return result;
    }
    public static CreationCatalog Apply(CreationCatalog source,CreationSettings settings)
    {
        Validate(settings,source);if(!settings.Enabled)return source;
        Dictionary<string,double> Merge(Dictionary<string,double> original,Dictionary<string,double>? edits){var result=new Dictionary<string,double>(original);foreach(var p in edits??[])result[p.Key]=p.Value;return result;}
        return source with
        {
            Races=source.Races.Select(r=>{settings.Races.TryGetValue(r.Key,out var edit);var abilities=(r.Abilities??[]).Select(a=>Apply(a,settings)).ToArray();return r with{Male=Merge(r.Male,edit?.Male),Female=Merge(r.Female,edit?.Female),Skills=Merge(r.Skills,edit?.Skills),Abilities=abilities,Bonuses=r.Abilities is null?r.Bonuses:Bonuses(abilities)};}).ToArray(),
            Classes=source.Classes.Select(c=>settings.Classes.TryGetValue(c.Name,out var edit)?c with{Specialization=edit.Specialization??c.Specialization,Attributes=edit.Attributes??c.Attributes,Skills=edit.Skills??c.Skills}:c).ToArray(),
            Birthsigns=source.Birthsigns.Select(s=>{var abilities=(s.Abilities??[]).Select(a=>Apply(a,settings)).ToArray();return s with{Abilities=abilities,Bonuses=s.Abilities is null?s.Bonuses:Bonuses(abilities)};}).ToArray()
        };
    }
    public static double IncomingMagic(Dictionary<string,double> stats,string element)=>Math.Max(0,1-Math.Clamp(stats.GetValueOrDefault("ResistMagic"),-100,100)/100)*Math.Max(0,1-Math.Clamp(stats.GetValueOrDefault("Resist"+element),-100,100)/100)*(1-Math.Clamp(stats.GetValueOrDefault("SpellAbsorption"),0,100)/100);
}