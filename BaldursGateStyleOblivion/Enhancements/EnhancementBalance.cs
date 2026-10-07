using BaldursGateStyleOblivion.Combat;
namespace BaldursGateStyleOblivion.Enhancements;

public static class EnhancementBalance
{
    public static string Family(string code)=>code switch
    {
        "FIDG" or "FRDG" or "SHDG" or "DGHE"=>"Damage",
        "ABHE"=>"Absorb health",
        "DGAT" or "DGSK"=>"Permanent stat damage",
        "REHE" or "RESP" or "REFA"=>"Recovery",
        "FOAT" or "FOSK" or "DRAT" or "ABAT" or "DRSK" or "ABSK"=>"Bonus",
        "FOHE" or "FOSP" or "FOFA" or "ABSP" or "ABFA" or "DGFA" or "DGSP" or "DRFA" or "DRSP"=>"Resource",
        "SHLD" or "FISH" or "FRSH" or "LISH"=>"Shield",
        "RSMA" or "RSFI" or "RSFR" or "RSSH" or "RSNW" or "RSPO" or "RSPA"=>"Resistance",
        "REDG" or "RFLC" or "SABS"=>"Reflection / absorption",
        "WKMA" or "WKFI" or "WKFR" or "WKSH" or "WKPO"=>"Weakness",
        "PARA" or "SLNC"=>"Control",
        "CHML"=>"Chameleon",
        "DRHE"=>"Temporary health drain",
        _=>"Utility"
    };
    public static double Total(EnhancementEffect e)=>e.Magnitude*Math.Max(1,e.Duration);
    public static double Damage(IEnumerable<EnhancementEffect> effects)=>effects.Where(e=>Family(e.Code) is "Damage" or "Absorb health").Sum(Total);
    public static double Recovery(IEnumerable<EnhancementEffect> effects,string code="REHE")=>effects.Where(e=>e.Code==code).Sum(Total);
    private static EnhancementEffect LimitTotal(EnhancementEffect effect,double cap)
    {
        if(Total(effect)<=cap||effect.Magnitude==0)return effect;
        var magnitude=Math.Max(1,Math.Floor(Math.Min(effect.Magnitude,cap)));
        var duration=effect.Duration<=1?effect.Duration:Math.Max(1,Math.Floor(cap/magnitude));
        return effect with{Magnitude=magnitude,Duration=duration};
    }
    public static EnhancementEffect[] Enchant(EnhancementEffect[] source,EnchantmentSettings settings,int tier,string slots,bool constant,double power=1,bool staff=false)
    {
        if(source.Any(e=>e.Scripted))return source;
        var budget=settings.Tiers[tier];var damageBudget=staff?settings.StaffDamage[tier]:budget.Damage;var weight=constant?Math.Min(1.5,slots.Split(',',StringSplitOptions.TrimEntries).Where(s=>s!="RightRing"||!slots.Contains("LeftRing",StringComparison.Ordinal)).Sum(s=>settings.SlotWeights.GetValueOrDefault(s,0))):1;
        if(weight==0)weight=1;
        double Cap(EnhancementEffect e)=>Family(e.Code) switch
        {
            "Damage"=>damageBudget,"Absorb health"=>damageBudget*.5,
            "Bonus" or "Permanent stat damage"=>budget.Bonus*weight,"Resource"=>budget.Resource*weight,
            "Shield" or "Resistance"=>budget.Resistance*weight,
            "Reflection / absorption"=>budget.Resistance*.5*weight,
            "Weakness"=>Math.Min(25,budget.Resistance),"Chameleon"=>Math.Min(15,budget.Bonus*weight),
            "Temporary health drain"=>damageBudget,
            _=>double.PositiveInfinity
        }*power;
        var effects=source.Select(e=>
        {
            var family=Family(e.Code);
            if(family=="Control")return e with{Duration=Math.Floor(Math.Min(e.Duration,budget.Control*power)),Magnitude=Math.Min(e.Magnitude,100)};
            var total=family is "Damage" or "Absorb health" or "Temporary health drain"?Total(e):e.Magnitude;
            var limit=Cap(e);if(family is "Damage" or "Absorb health" or "Temporary health drain")return LimitTotal(e,limit);
            var mag=total>limit?e.Magnitude*limit/total:e.Magnitude;
            return e with{Magnitude=Math.Floor(mag),Duration=constant?0:e.Duration};
        }).ToArray();
        // A three-element enchantment shares one damage allowance, rather than receiving three allowances.
        foreach(var group in new[]{"Damage","Bonus","Resource","Defense"})
        {
            bool Included(EnhancementEffect e)=>group switch{"Damage"=>Family(e.Code) is "Damage" or "Absorb health","Defense"=>Family(e.Code) is "Shield" or "Resistance" or "Reflection / absorption",_=>Family(e.Code)==group};
            var cap=group switch{"Damage"=>damageBudget*power,"Bonus"=>budget.Bonus*weight*power,"Resource"=>budget.Resource*weight*power,_=>budget.Resistance*weight*power};
            var total=effects.Where(Included).Sum(e=>group=="Damage"?Total(e)*(e.Code=="ABHE"?2:1):e.Magnitude);
            if(total>cap)effects=effects.Select(e=>Included(e)?group=="Damage"?LimitTotal(e,Total(e)*cap/total):e with{Magnitude=Math.Floor(e.Magnitude*cap/total)}:e).ToArray();
        }
        return effects;
    }
    public static EnhancementEffect[] Sigil(EnhancementEffect[] source,EnchantmentSettings settings,int tier,int[] armorIndices)
    {
        if(source.Any(e=>e.Scripted))return source;
        var result=source.ToArray();
        foreach(var armor in new[]{false,true})
        {
            var indices=Enumerable.Range(0,source.Length).Where(i=>armorIndices.Contains(i)==armor).ToArray();
            var effects=Enchant(indices.Select(i=>source[i]).ToArray(),settings,tier,armor?"LeftRing":"Weapon",armor,armor?settings.SigilArmorMultiplier:1);
            for(var i=0;i<indices.Length;i++)
            {
                var effect=effects[i];if(armor&&settings.CustomConstantCaps.TryGetValue(effect.Code,out var cap))effect=effect with{Magnitude=Math.Floor(Math.Min(effect.Magnitude,cap*settings.SigilArmorMultiplier))};result[indices[i]]=effect;
            }
        }
        return result;
    }
    public static EnhancementEffect[] Potion(EnhancementEffect[] source,AlchemySettings settings,int rank)
    {
        if(source.Any(e=>e.Scripted))return source;
        var effects=source.Select(e=>
        {
            var family=Family(e.Code);var duration=Math.Min(e.Duration,settings.MaximumDuration);var magnitude=e.Magnitude;
            if(family=="Control")duration=Math.Min(duration,settings.MaximumControlSeconds);
            if(family=="Weakness")magnitude=Math.Min(magnitude,settings.WeaknessCap);
            if(family is "Bonus" or "Shield" or "Resistance" or "Reflection / absorption" or "Chameleon")magnitude=Math.Min(magnitude,settings.BuffCaps[rank]);
            if(family=="Resource")magnitude=Math.Min(magnitude,settings.ResourceCaps[rank]);
            if(family=="Permanent stat damage")return LimitTotal(e,settings.BuffCaps[rank]);
            if(family=="Temporary health drain")magnitude=Math.Min(magnitude,settings.PoisonBudgets[rank]*.5);
            return e with{Magnitude=Math.Floor(magnitude),Duration=duration};
        }).ToArray();
        foreach(var group in new[]{"Recovery","Damage"})
        {
            bool Included(EnhancementEffect e)=>group=="Recovery"?Family(e.Code)=="Recovery":Family(e.Code) is "Damage" or "Absorb health";
            var total=effects.Where(Included).Sum(Total);var cap=group=="Recovery"?settings.RecoveryBudgets[rank]:settings.PoisonBudgets[rank];
            if(total>cap)effects=effects.Select(e=>Included(e)?LimitTotal(e,Total(e)*cap/total):e).ToArray();
        }
        return effects;
    }
    public static uint PotionValue(EnhancementEffect[] effects,int rank)
    {
        var usefulness=effects.Sum(e=>Family(e.Code) switch{"Damage" or "Absorb health"=>Total(e)*.45,"Recovery"=>Total(e)*.3,"Control"=>e.Duration*12,"Utility"=>Math.Max(5,e.Duration*.2),_=>e.Magnitude*Math.Max(1,e.Duration)*.03});
        return (uint)Math.Clamp(Math.Round((5+usefulness)*(1+rank*.15)),1,10000);
    }
    public static string[] Warnings(EnhancementEffect[] effects,bool constant=false)
    {
        var warnings=new List<string>();
        if(effects.Any(e=>e.Scripted))warnings.Add("Scripted effect retained; power cannot be inferred from magnitude alone.");
        if(effects.Any(e=>Family(e.Code)=="Weakness")&&effects.Any(e=>Family(e.Code) is "Damage" or "Absorb health"))warnings.Add("Weakness/damage sequence needs native repeated-hit testing; totals exclude amplification.");
        if(effects.Any(e=>Family(e.Code)=="Reflection / absorption"))warnings.Add("Reflection/absorption changes opponent interactions; do not interpret it as ordinary armor.");
        if(effects.Any(e=>e.Code=="DRHE"))warnings.Add("Drain Health is temporary; excluded from permanent damage totals.");
        if(constant)warnings.Add("Evaluate the complete equipped loadout; per-item budgets do not enforce a runtime equipment cap.");
        if(effects.Any(e=>Family(e.Code)=="Control"))warnings.Add("Control immunity and AI delivery are not simulated.");
        return warnings.ToArray();
    }
    public static PhysicalItem ArtifactPhysical(PhysicalItem source,CombatSettings combat,ArtifactRule rule)
    {
        var result=source;
        if(rule.NormalizePhysical&&source.Kind!="Clothing")
        {
            var itemClass=rule.Class??source.Class;if(rule.Class is null&&source.Kind=="Weapon"&&!combat.Gameplay.WeaponBaselines.ContainsKey(itemClass))itemClass=source.NativeType switch{"BladeOneHand"=>"Longsword","BladeTwoHand"=>"Claymore","BluntOneHand"=>"Mace","BluntTwoHand"=>"Warhammer",_=>itemClass};
            result=PhysicalBalance.Propose(source with{Material=rule.Material,Class=itemClass,Protected=false},combat);
            result=result with{Damage=Math.Round(result.Damage*combat.Gameplay.WeaponPower(rule.Tier)*rule.PhysicalPower),Armor=Math.Min(85,result.Armor*rule.PhysicalPower),Speed=source.Speed,Reach=source.Reach};
        }
        return result with{Damage=rule.Damage??result.Damage,Armor=rule.Armor??result.Armor,Weight=rule.Weight??result.Weight,Speed=rule.Speed??result.Speed,Reach=rule.Reach??result.Reach,Durability=rule.Durability??result.Durability,Value=rule.Value??result.Value};
    }
    public static double CraftedValue(double skill,double luck,double mortarQuality,double multiplier)
    {
        EnhancementConfiguration.Range(skill,0,100,"Alchemy skill");EnhancementConfiguration.Range(luck,0,100,"Luck");EnhancementConfiguration.Range(mortarQuality,0,100,"Mortar quality");EnhancementConfiguration.Range(multiplier,.01,1,"Value multiplier");
        return Math.Floor((Math.Clamp(skill+.4*(luck-50),0,100)+mortarQuality*.25)*multiplier);
    }
}