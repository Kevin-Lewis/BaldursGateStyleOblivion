namespace BaldursGateStyleOblivion.Combat;

public sealed class WeaponBaseline
{
    public double Damage { get; set; } = 12;
    public double Speed { get; set; } = 1;
    public double Reach { get; set; } = 1;
    public double Weight { get; set; } = 12;
    public uint Durability { get; set; } = 300;
}
public sealed class ActorTierStats
{
    public double Health { get; set; }
    public double Attribute { get; set; }
    public double Specialty { get; set; }
    public double? Focused { get; set; }
    public double? SupportingAttribute { get; set; }
    public double Secondary { get; set; }
    public double Untrained { get; set; } = 5;
    public double NaturalDamage { get; set; }
}
public sealed class ActorBuild
{
    public double Health { get; set; } = 1;
    public double Strength { get; set; } = 1;
    public double Endurance { get; set; } = 1;
    public double Agility { get; set; } = 1;
    public double Willpower { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public string Style { get; set; } = "Aggressive Fighter";
    public double SkillMultiplier { get; set; } = 1;
    public string[] SupportingSkills { get; set; } = ["Block", "LightArmor", "Restoration"];
    public string[] PlayerFocusedSkills { get; set; } = ["Block","HeavyArmor"];
    public string? PrimarySkill { get; set; }
    public string[] RotatingAttributes { get; set; } = ["Agility", "Speed"];
    public string[] FocusedAttributes { get; set; } = ["Strength", "Endurance"];
    public double Intelligence { get; set; } = .9;
    public double Personality { get; set; } = 1;
    public string[] Skills { get; set; } = ["Blade", "Blunt", "HeavyArmor"];
}
public sealed class CreatureBuild
{
    public double Health { get; set; } = 1;
    public double Damage { get; set; } = 1;
    public double Speed { get; set; } = 1;
}
public sealed class ActorCombatOverride
{
    public bool Preserve { get; set; }
    public string? Build { get; set; }
    public double? Health { get; set; }
}
public sealed class NativeStyleProfile
{
    public byte AttackChance { get; set; } = 65;
    public byte BlockChance { get; set; } = 30;
    public byte PowerAttackChance { get; set; } = 15;
    public byte DodgeChance { get; set; } = 20;
    public double IdleMin { get; set; } = .2;
    public double IdleMax { get; set; } = .6;
    public double HoldMin { get; set; } = .3;
    public double HoldMax { get; set; } = .8;
}
public sealed class GameplaySettings
{
    public bool NormalizeEquipment { get; set; }
    public bool BalanceEnchantedPhysicalStats { get; set; }
    public bool MatchCreatureAttacksToWarrior { get; set; } = true;
    public bool BalanceActors { get; set; }
    public bool BalanceGameSettings { get; set; }
    public PlayerProgression PlayerProgression { get; set; } = new();
    public double RareWeaponGrowth { get; set; } = 1.5;
    public Dictionary<string, WeaponBaseline> WeaponBaselines { get; set; } = new();
    public Dictionary<string, double> ArmorSlots { get; set; } = new();
    public Dictionary<int, ActorTierStats> ActorTiers { get; set; } = new();
    public Dictionary<string, ActorBuild> ActorBuilds { get; set; } = new();
    public Dictionary<string, CreatureBuild> CreatureBuilds { get; set; } = new();
    public Dictionary<string, ActorCombatOverride> ActorOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, NativeStyleProfile> NativeStyles { get; set; } = new();
    public Dictionary<string, double> GameSettings { get; set; } = new();
    public double WeaponPower(int tier) => Math.Pow(RareWeaponGrowth, Math.Max(0, tier - 6));
    public static readonly HashSet<string> AllowedGameSettings = new(StringComparer.Ordinal)
    {
        "fDamageWeaponMult", "fDamageStrengthBase", "fDamageStrengthMult", "fDamageSkillBase", "fDamageSkillMult",
        "fDamageWeaponConditionBase", "fDamageWeaponConditionMult", "fArmorRatingBase", "fArmorRatingMax",
        "fArmorRatingConditionBase", "fArmorRatingConditionMult", "fMaxArmorRating", "fBlockSkillBase", "fBlockSkillMult",
        "fBlockMax", "fBlockAmountWeaponMult", "fFatigueBase", "fFatigueMult", "fFatigueAttackWeaponBase",
        "fFatigueAttackWeaponMult", "fFatigueReturnBase", "fFatigueReturnMult", "fFatigueRunBase", "fFatigueRunMult",
        "fFatigueBlockBase", "fFatigueBlockMult", "fFatigueBlockSkillBase", "fFatigueBlockSkillMult",
        "fPowerAttackFatiguePenalty", "fDamagePowerAttackBonus", "fDamagePowerAttackStandBonus", "fDamagePowerAttackForwardBonus",
        "fDamagePowerAttackBackBonus", "fDamagePowerAttackSideBonus", "fDifficultyDamageMultiplier", "fPCBaseHealthMult",
        "fStatsHealthStartMult", "fStatsHealthLevelMult",
        "iLevelUp01Mult", "iLevelUp02Mult", "iLevelUp03Mult", "iLevelUp04Mult", "iLevelUp05Mult",
        "iLevelUp06Mult", "iLevelUp07Mult", "iLevelUp08Mult", "iLevelUp09Mult", "iLevelUp10Mult",
        "fPerkLightArmorMasterRatingMult", "fPerkHeavyArmorExpertSpeedMult", "fPerkHeavyArmorMasterSpeedMult",
        "fPerkLightArmorExpertSpeedMult", "fPerkAthleticsNoviceFatigueMult", "fPerkAthleticsApprenticeFatigueMult",
        "fPerkAthleticsJourneymanFatigueMult", "fPerkAthleticsExpertFatigueMult", "fPerkAthleticsMasterFatigueMult"
    };
    public void Validate()
    {
        var progression=PlayerProgression ?? throw new ArgumentException("Player progression settings missing.");
        CombatConfiguration.Range(progression.MasteryLevel,2,100,"Player mastery level");
        CombatConfiguration.Range(progression.FocusedMasteryLevel,progression.MasteryLevel,100,"Focused mastery level");
        CombatConfiguration.Range(progression.SupportingSkillPerLevelAfterMastery,0,10,"Later supporting skill growth");
        CombatConfiguration.Range(progression.SecondarySkillAtMastery,0,100,"Supporting skill target");
        CombatConfiguration.Range(progression.UntrainedSkillPerLevel,0,10,"Untrained skill growth");
        if (WeaponBaselines is null || ArmorSlots is null || ActorTiers is null || ActorBuilds is null || CreatureBuilds is null
            || ActorOverrides is null || NativeStyles is null || GameSettings is null) throw new ArgumentException("Gameplay tables cannot be null.");
        CombatConfiguration.Range(RareWeaponGrowth, 1, 3, "Rare weapon growth");
        foreach (var pair in GameSettings)
        {
            if (!AllowedGameSettings.Contains(pair.Key)) throw new ArgumentException($"Unsupported engine setting {pair.Key}.");
            CombatConfiguration.Range(pair.Value, 0, 100, pair.Key);
            if(pair.Key.StartsWith("i") && pair.Value!=Math.Truncate(pair.Value))throw new ArgumentException($"Integer engine setting {pair.Key} requires a whole number.");
        }
        foreach (var value in WeaponBaselines.Values)
        {
            if (value is null) throw new ArgumentException("Weapon baseline missing.");
            CombatConfiguration.Range(value.Damage, 1, 1000, "Weapon damage"); CombatConfiguration.Range(value.Speed, .1, 3, "Weapon speed");
            CombatConfiguration.Range(value.Reach, .1, 3, "Weapon reach"); CombatConfiguration.Range(value.Weight, .1, 200, "Weapon weight");
            CombatConfiguration.Range(value.Durability, 1, 100000, "Weapon durability");
        }
        foreach (var value in ArmorSlots.Values) CombatConfiguration.Range(value, 0, 100, "Armor slot coverage");
        if (BalanceActors && (ActorTiers.Count != 11 || Enumerable.Range(0,11).Any(tier=>!ActorTiers.ContainsKey(tier))))
            throw new ArgumentException("Actor stat tiers must cover 0–10.");
        foreach (var value in ActorTiers.Values)
        {
            if (value is null) throw new ArgumentException("Actor tier missing.");
            CombatConfiguration.Range(value.Health, 1, 10000, "Tier health"); CombatConfiguration.Range(value.Attribute, 1, 100, "Tier attribute");
            if(value.Focused.HasValue)CombatConfiguration.Range(value.Focused.Value,0,100,"Focused skill");
            if(value.SupportingAttribute.HasValue)CombatConfiguration.Range(value.SupportingAttribute.Value,1,100,"Supporting attribute");
            CombatConfiguration.Range(value.Untrained, 0, 100, "Untrained skill"); CombatConfiguration.Range(value.Specialty, 0, 100, "Specialty skill"); CombatConfiguration.Range(value.Secondary, 0, 100, "Secondary skill");
            CombatConfiguration.Range(value.NaturalDamage, 0, 1000, "Natural damage");
        }
        foreach (var value in ActorBuilds.Values)
        {
            if (value is null || value.PlayerFocusedSkills is null || value.RotatingAttributes is null || value.FocusedAttributes is null || value.Skills is null || value.SupportingSkills is null || !NativeStyles.ContainsKey(value.Style)) throw new ArgumentException("Actor build/style missing.");
            foreach (var factor in new[] { value.Health, value.Strength, value.Endurance, value.Agility, value.Willpower, value.Speed, value.Intelligence, value.Personality })
                CombatConfiguration.Range(factor, .25, 3, "Build factor");
            if(value.PlayerFocusedSkills.Length>2 || value.PlayerFocusedSkills.Distinct().Count()!=value.PlayerFocusedSkills.Length || value.PlayerFocusedSkills.Any(s=>!CombatBuilds.Skills.Contains(s)))throw new ArgumentException("Choose up to two other heavily focused player skills.");
            if(value.PrimarySkill is not null && !value.Skills.Contains(value.PrimarySkill))throw new ArgumentException("Primary skill must be a class specialty.");
            if(value.RotatingAttributes.Length!=2 || value.RotatingAttributes.Concat(value.FocusedAttributes).Distinct().Count()!=4 || value.RotatingAttributes.Any(a=>!new[]{"Strength","Endurance","Agility","Willpower","Speed","Intelligence","Personality"}.Contains(a)))throw new ArgumentException("Choose two distinct rotating attributes outside the fixed pair.");
            if(value.FocusedAttributes.Length!=2 || value.FocusedAttributes.Distinct().Count()!=2 || value.FocusedAttributes.Any(a=>!new[]{"Strength","Endurance","Agility","Willpower","Speed","Intelligence","Personality"}.Contains(a)))throw new ArgumentException("Choose two focused NPC attributes.");
            CombatConfiguration.Range(value.SkillMultiplier,0,2,"Class skill multiplier");
            if(value.Skills.Concat(value.SupportingSkills).Any(skill=>!CombatBuilds.Skills.Contains(skill))) throw new ArgumentException("Unsupported specialty skill.");
        }
        foreach (var value in CreatureBuilds.Values)
        {
            if(value is null) throw new ArgumentException("Creature build missing.");
            foreach(var factor in new[]{value.Health,value.Damage,value.Speed}) CombatConfiguration.Range(factor,.1,10,"Creature factor");
        }
        foreach (var value in NativeStyles.Values)
        {
            if(value is null) throw new ArgumentException("Native style missing.");
            foreach(var chance in new[]{value.AttackChance,value.BlockChance,value.PowerAttackChance,value.DodgeChance}) CombatConfiguration.Range(chance,0,100,"Style chance");
            CombatConfiguration.Range(value.IdleMin,0,10,"Idle minimum");CombatConfiguration.Range(value.IdleMax,value.IdleMin,10,"Idle maximum");
            CombatConfiguration.Range(value.HoldMin,0,10,"Hold minimum");CombatConfiguration.Range(value.HoldMax,value.HoldMin,10,"Hold maximum");
        }
        ActorOverrides=new(ActorOverrides,StringComparer.OrdinalIgnoreCase);
        foreach(var pair in ActorOverrides)
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(pair.Key,@"^[0-9A-Fa-f]{6,8}:[^:]+\.(esm|esp)$")) throw new ArgumentException("Invalid actor FormKey.");
            if(pair.Value is null || pair.Value.Build is not null && !ActorBuilds.ContainsKey(pair.Value.Build)) throw new ArgumentException("Unknown actor build override.");
            if(pair.Value.Health.HasValue) CombatConfiguration.Range(pair.Value.Health.Value,1,100000,"Actor health override");
        }
    }
}
