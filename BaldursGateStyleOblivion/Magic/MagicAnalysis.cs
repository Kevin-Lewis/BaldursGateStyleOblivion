namespace BaldursGateStyleOblivion.Magic;

public sealed record SpellEffectInfo(string Code,string Name,string School,string Family,double Magnitude,double Duration,double Area,string Range,string ActorValue,double? BaseCost,bool NoMagnitude,bool NoDuration,bool NoArea,bool Hostile,string? Script);
public sealed record SpellInfo(string FormKey,string? EditorID,string Name,string SourcePlugin,string Type,string Mastery,int SkillRequirement,bool ManualCost,double StoredCost,string Flags,SpellEffectInfo[] Effects,string[] ActorUsers,string[] Vendors,bool Test);
public sealed record SpellAssessment(SpellInfo Spell,string School,double? CalculatedBaseCost,double? BaseCost,double Damage,double Healing,double ShieldPercent,double ControlSeconds,double SummonSeconds,string[] Utility,string[] Warnings)
{
    public List<string> Outliers { get; set; } = [];
}
public sealed record CastingEstimate(bool Eligible,double? Cost,double? CastsFromPool,double DamagePerCast,double HealingPerCast,double? DamagePerMagicka,double? BurstDamage,double? MagickaLimitedDamagePerSecond);

public static class MagicAnalysis
{
    public static string Family(string code) => code switch
    {
        "FIDG" or "FRDG" or "SHDG" or "DGHE" => "Damage",
        "ABHE" => "Absorb health",
        "REHE" => "Healing",
        "SHLD" or "FISH" or "FRSH" or "SHSH" => "Defense",
        "PARA" or "SLNC" => "Control",
        "CALM" or "DEMO" or "FRNZ" or "COCR" or "COHU" => "Level-limited control",
        "DRHE" => "Temporary health drain",
        "WKFI" or "WKFR" or "WKSH" or "WKMA" or "WKPO" or "WKNW" => "Weakness",
        "RFLC" or "REDG" or "SABS" or "RSMA" or "RSFI" or "RSFR" or "RSSH" or "RSNW" or "RSPA" or "RSPO" or "RSDI" => "Resistance / reflection",
        "SEFF" => "Scripted",
        _ when code.StartsWith("Z",StringComparison.Ordinal) => "Summon",
        _ when code.StartsWith("BA",StringComparison.Ordinal)||code.StartsWith("BW",StringComparison.Ordinal) => "Bound equipment",
        _ => "Utility / stats"
    };
    public static double EffectCost(SpellEffectInfo effect,IReadOnlyDictionary<string,double> settings)
    {
        double GS(string key,double fallback)=>settings.GetValueOrDefault(key,fallback);
        var magnitude=effect.NoMagnitude?1:Math.Max(1,effect.Magnitude);
        var duration=effect.NoDuration?1:Math.Max(1,effect.Duration);
        var area=effect.NoArea?1:Math.Max(1,effect.Area*GS("fMagicAreaBaseCostMult",.15));
        return effect.BaseCost!.Value*Math.Pow(magnitude,GS("fMagicCostScale",1.28))*duration*GS("fMagicDurMagBaseCostMult",.1)*area*(effect.Range=="Target"?GS("fMagicRangeTargetCostMult",1.5):1);
    }
    public static SpellAssessment Assess(SpellInfo spell,IReadOnlyDictionary<string,double> settings)
    {
        var warnings=new List<string>();
        var complete=spell.Effects.Length>0&&spell.Effects.All(e=>e.BaseCost.HasValue&&e.Script is null&&e.Family!="Scripted");
        var costs=spell.Effects.Select(e=>e.BaseCost.HasValue?EffectCost(e,settings):0).ToArray();
        var school=spell.Effects.Length==0?"Unknown":spell.Effects[Array.IndexOf(costs,costs.Max())].School;
        double? calculated=complete?costs.Sum():null;
        var cost=spell.ManualCost?spell.StoredCost:calculated;
        if(!complete)warnings.Add("Effect cost/power incomplete: missing definition, scripted effect or empty spell.");
        if(spell.Effects.Select(e=>e.School).Distinct().Count()>1)warnings.Add("Mixed schools: governing school and runtime cost need calibration.");
        if(spell.Effects.Any(e=>e.Area>0))warnings.Add("Area is radius; affected targets and friendly fire are not inferred.");
        if(spell.Effects.Any(e=>e.Family=="Weakness"))warnings.Add("Weakness interactions depend on effect order, repeat casts and resistance; no bonus damage assumed.");
        if(spell.Effects.Any(e=>e.Family=="Temporary health drain"))warnings.Add("Drain Health is temporary, not sustained damage; it can still kill at a threshold.");
        if(spell.Effects.Any(e=>e.Family=="Level-limited control"))warnings.Add("Control needs target-level/immunity checks; magnitude 25 and armor effectiveness require special review.");
        if(spell.Effects.Any(e=>e.Family=="Summon"))warnings.Add("Summon power depends on the summoned actor, not just duration or spell cost.");
        if(spell.Effects.Any(e=>e.Family=="Defense"&&e.Code!="SHLD"))warnings.Add("Elemental shields provide armor and elemental resistance; caps and overlap matter.");
        if(spell.Effects.Any(e=>e.Family=="Resistance / reflection"))warnings.Add("Resistance, reflection and absorption are distinct mechanics, not flat health or DPS.");
        if(spell.Type!="Spell")warnings.Add("Ability, power, disease or other special record: excluded from ordinary spell comparisons.");
        var damage=spell.Effects.Where(e=>(e.Family is "Damage" or "Absorb health")&&e.Range!="Self").Sum(e=>e.Magnitude*(e.NoDuration?1:Math.Max(1,e.Duration)));
        var healing=spell.Effects.Where(e=>e.Family=="Healing").Sum(e=>e.Magnitude*(e.NoDuration?1:Math.Max(1,e.Duration)));
        double Maximum(string family)=>spell.Effects.Where(e=>e.Family==family).Select(e=>e.Duration).DefaultIfEmpty().Max();
        var assessment=new SpellAssessment(spell,school,calculated,cost,damage,healing,spell.Effects.Where(e=>e.Family=="Defense").Sum(e=>e.Magnitude),Maximum("Control"),Maximum("Summon"),spell.Effects.Where(e=>e.Family is not "Damage" and not "Healing").Select(e=>$"{e.Name}: {e.Family}").Distinct().ToArray(),warnings.ToArray());
        if(spell.Type=="Spell"&&!spell.Test&&cost==0)assessment.Outliers.Add("Zero base cost: review intended access and exception status.");
        if(spell.Type=="Spell"&&!spell.Test&&spell.ManualCost&&calculated>0&&cost>0&&(cost/calculated<.5||cost/calculated>2))assessment.Outliers.Add($"Manual cost is {cost/calculated:0.00}× the effect-cost estimate.");
        return assessment;
    }
    public static CastingEstimate Cast(SpellAssessment spell,double skill,double pool,double regeneration,IReadOnlyDictionary<string,double> settings,double effectiveness=1)
    {
        var factor=settings.GetValueOrDefault("fMagicCasterSkillCostBase",.2)+(1-Math.Clamp(skill,0,100)/100)*settings.GetValueOrDefault("fMagicCasterSkillCostMult",1.2);
        double? cost=spell.BaseCost.HasValue?Math.Max(0,spell.BaseCost.Value*factor):null;
        double? casts=cost>0?Math.Floor(pool/cost.Value):null;
        var damage=spell.Damage*effectiveness;var healing=spell.Healing*effectiveness;
        return new(spell.Spell.Type=="Spell"&&skill>=spell.Spell.SkillRequirement,cost,casts,damage,healing,cost>0?damage/cost:null,casts*damage,cost>0?regeneration*damage/cost:null);
    }
    public static void FindOutliers(SpellAssessment[] spells)
    {
        var regular=spells.Where(s=>s.Spell.Type=="Spell"&&!s.Spell.Test&&s.BaseCost>0&&!s.Spell.Effects.Any(e=>e.Script is not null||e.Family=="Scripted")).ToArray();
        foreach(var group in regular.Where(s=>s.Spell.Effects.Length==1&&s.Spell.Effects[0].Family is "Damage" or "Healing").GroupBy(s=>$"{s.Spell.Effects[0].Code}|{s.Spell.Effects[0].Range}|{s.Spell.Effects[0].Duration}|{s.Spell.Effects[0].Area}|{s.Spell.Effects[0].ActorValue}|{s.Spell.Vendors.Length>0}"))
            foreach(var spell in group)
            {
                var output=spell.Damage+spell.Healing;
                var better=group.FirstOrDefault(other=>other!=spell&&other.Spell.SkillRequirement<=spell.Spell.SkillRequirement&&other.BaseCost<=spell.BaseCost&&(other.Damage+other.Healing)>=output&&((other.Damage+other.Healing)>output||other.BaseCost<spell.BaseCost));
                if(better is not null)spell.Outliers.Add($"Numerically dominated by {better.Spell.Name} ({better.Spell.FormKey}) with the same effect/range/duration/area; check availability before changing.");
            }
    }
}
