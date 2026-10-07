using System.Text.Json;
using System.Text.Json.Serialization;
using BaldursGateStyleOblivion.Combat;

namespace BaldursGateStyleOblivion.Enhancements;

public sealed record EnhancementEffect(string Code,string ActorValue,double Magnitude,double Duration,bool Hostile=false,bool Scripted=false);
public sealed class EffectBudget
{
    public double Damage { get; set; } = 6;
    public double Bonus { get; set; } = 5;
    public double Resistance { get; set; } = 6;
    public double Resource { get; set; } = 20;
    public double Control { get; set; } = 1;
}
public sealed class ArtifactRule
{
    public string Name { get; set; } = "";
    public int Tier { get; set; } = 8;
    public bool Preserve { get; set; }
    public string Material { get; set; } = "Daedric";
    public string? Class { get; set; }
    public bool NormalizePhysical { get; set; } = true;
    public bool BalanceScriptedEquipment { get; set; }
    public double PhysicalPower { get; set; } = 1;
    public double EnchantmentPower { get; set; } = 1;
    public int ChargedHits { get; set; } = 60;
    public uint? ChargeCapacity { get; set; }
    public double? Damage { get; set; }
    public double? Armor { get; set; }
    public double? Weight { get; set; }
    public double? Speed { get; set; }
    public double? Reach { get; set; }
    public uint? Durability { get; set; }
    public uint? Value { get; set; }
    public EnhancementEffect[]? Effects { get; set; }
    public string[] Sources { get; set; } = [];
    public string Reason { get; set; } = "Retain native effect identities; exceptional physical equipment and bounded enchantment.";
}
public sealed class EnchantmentSettings
{
    public bool Enabled { get; set; } = true;
    public bool RestrictCustomEnchanting { get; set; } = true;
    public string[] ExcludedCustomEffects { get; set; } = ["WKMA","WKFI","WKFR","WKSH","WKPO","DRHE","PARA","CHML","REDG","RFLC","SABS","ABSP"];
    public Dictionary<string,double> CustomConstantCaps { get; set; } = new(){["FOAT"]=4,["FOSK"]=4,["FOSP"]=15,["FOHE"]=15,["SHLD"]=5,["FISH"]=5,["FRSH"]=5,["LISH"]=5,["RSMA"]=6,["RSFI"]=8,["RSFR"]=8,["RSSH"]=8};
    public Dictionary<string,double> SlotWeights { get; set; } = new(){["UpperBody"]=1,["LowerBody"]=.7,["Head"]=.6,["Hair"]=.6,["Hand"]=.4,["Foot"]=.4,["Shield"]=.8,["LeftRing"]=.6,["RightRing"]=.6,["Amulet"]=.8};
    public EffectBudget[] Tiers { get; set; } = [];
    public double SigilArmorMultiplier { get; set; } = 1.25;
    public double[] StaffDamage { get; set; } = [12,20,20,36,62,100,100,100,145,200,280];
    public int ChargedHits { get; set; } = 35;
    public Dictionary<string,ArtifactRule> Artifacts { get; set; } = new();
    public Dictionary<string,int> ItemTiers { get; set; } = new();
    public string[] PreserveItems { get; set; } = [];
    public Dictionary<string,int> EquipmentChances { get; set; } = new(){["Poor"]=1,["Common"]=2,["Professional"]=3,["Military"]=2,["HighQuality"]=3,["Elite"]=4,["HighDremora"]=8};
}
public sealed class AlchemySettings
{
    public bool Enabled { get; set; } = true;
    public double CraftedValueMultiplier { get; set; } = .12;
    public int MaximumActivePotions { get; set; } = 2;
    public double[] ApparatusQualities { get; set; } = [10,22,38,52,65];
    public double[] RecoveryBudgets { get; set; } = [40,65,100,145,190];
    public double[] PoisonBudgets { get; set; } = [25,40,65,95,135];
    public double[] BuffCaps { get; set; } = [8,12,16,20,25];
    public double[] ResourceCaps { get; set; } = [15,25,35,50,65];
    public int MaximumDuration { get; set; } = 90;
    public int MaximumControlSeconds { get; set; } = 3;
    public double WeaknessCap { get; set; } = 20;
    public int CommonIngredientValueFloor { get; set; } = 5;
    public int RareIngredientValue { get; set; } = 30;
    public int MerchantIngredientCount { get; set; } = 8;
    public int MerchantRareIngredientCount { get; set; } = 2;
    public int MerchantPotionCount { get; set; } = 3;
    public Dictionary<string,string> IngredientReplacements { get; set; } = new(){["WKMA"]="WKFI",["CHML"]="INVI",["REDG"]="SHLD",["RFLC"]="RSMA",["SABS"]="FOSP"};
    public Dictionary<string,uint> IngredientValues { get; set; } = new();
    public Dictionary<string,uint> PotionValues { get; set; } = new();
    public Dictionary<string,int> PotionRanks { get; set; } = new();
    public string[] PreserveItems { get; set; } = [];
}
public static class EnhancementConfiguration
{
    public static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public static T Load<T>(string path) where T:class=>Parse<T>(File.ReadAllText(path));
    public static T Parse<T>(string text) where T:class
    {
        var value=JsonSerializer.Deserialize<T>(text,Options)??throw new ArgumentException("Settings missing.");
        Validate(value);return value;
    }
    public static void Range(double value,double min,double max,string name)=>CombatConfiguration.Range(value,min,max,name);
    public static void Validate(object settings)
    {
        if(settings is EnchantmentSettings e)
        {
            Range(e.SigilArmorMultiplier,.1,3,"Sigil armor multiplier");
            if(e.StaffDamage is null||e.StaffDamage.Length!=11)throw new ArgumentException("Staff damage needs eleven tiers.");foreach(var v in e.StaffDamage)Range(v,1,1000,"Staff damage");
            if(e.Tiers is null||e.Tiers.Length!=11||e.Tiers.Any(t=>t is null)||e.Artifacts is null||e.ItemTiers is null||e.CustomConstantCaps is null||e.SlotWeights is null||e.EquipmentChances is null||e.ExcludedCustomEffects is null||e.PreserveItems is null)throw new ArgumentException("Enchantment tables require eleven tiers and non-null collections.");
            foreach(var b in e.Tiers){Range(b.Damage,1,500,"Damage budget");Range(b.Bonus,1,100,"Bonus budget");Range(b.Resistance,1,80,"Resistance budget");Range(b.Resource,1,500,"Resource budget");Range(b.Control,0,10,"Control budget");}
            Range(e.ChargedHits,1,1000,"Charged hits");
            foreach(var x in e.CustomConstantCaps)Range(x.Value,0,80,"Custom constant cap");foreach(var x in e.SlotWeights)Range(x.Value,.1,2,"Slot weight");foreach(var x in e.EquipmentChances)Range(x.Value,0,100,"Equipment chance");foreach(var x in e.ItemTiers)Range(x.Value,0,10,"Item tier");
            foreach(var pair in e.Artifacts)
            {
                var a=pair.Value??throw new ArgumentException("Artifact rule missing.");Range(a.Tier,0,10,"Artifact tier");Range(a.PhysicalPower,.1,1.15,"Artifact physical power");Range(a.EnchantmentPower,.1,3,"Artifact enchantment power");Range(a.ChargedHits,1,1000,"Artifact charged hits");if(a.ChargeCapacity.HasValue)Range(a.ChargeCapacity.Value,100,65535,"Artifact charge capacity");
                foreach(var v in new[]{a.Damage,a.Armor,a.Weight,a.Speed,a.Reach})if(v.HasValue)Range(v.Value,0,655,"Artifact physical value");if(a.Armor>85)throw new ArgumentException("Artifact armor must be <=85.");
                if(a.Effects is not null)foreach(var fx in a.Effects){if(fx is null||string.IsNullOrWhiteSpace(fx.Code))throw new ArgumentException("Artifact effect missing.");Range(fx.Magnitude,0,1000,"Effect magnitude");Range(fx.Duration,0,300,"Effect duration");}
            }
        }
        else if(settings is AlchemySettings a)
        {
            if(a.ApparatusQualities is null||a.RecoveryBudgets is null||a.PoisonBudgets is null||a.BuffCaps is null||a.ResourceCaps is null||a.IngredientReplacements is null||a.IngredientValues is null||a.PotionValues is null||a.PotionRanks is null||a.PreserveItems is null)throw new ArgumentException("Alchemy tables missing.");
            foreach(var table in new[]{a.ApparatusQualities,a.RecoveryBudgets,a.PoisonBudgets,a.BuffCaps,a.ResourceCaps}){if(table.Length!=5)throw new ArgumentException("Alchemy tables require five ranks.");foreach(var v in table)Range(v,1,1000,"Alchemy rank value");}
            foreach(var v in a.ApparatusQualities)Range(v,1,100,"Apparatus quality");Range(a.CraftedValueMultiplier,.01,1,"Crafted value multiplier");Range(a.MaximumActivePotions,1,4,"Active potions");Range(a.MaximumDuration,1,300,"Potion duration");Range(a.MaximumControlSeconds,1,10,"Poison control");Range(a.WeaknessCap,0,50,"Weakness cap");Range(a.CommonIngredientValueFloor,1,100,"Ingredient floor");Range(a.RareIngredientValue,1,1000,"Rare ingredient threshold");
            foreach(var v in new[]{a.MerchantIngredientCount,a.MerchantRareIngredientCount,a.MerchantPotionCount})Range(v,1,100,"Merchant count");foreach(var p in a.PotionRanks)Range(p.Value,0,4,"Potion rank");
        }
    }
}