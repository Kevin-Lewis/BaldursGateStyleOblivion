
namespace BaldursGateStyleOblivion.Combat;

public sealed class Fighter
{
    public string Name { get; set; } = "Combatant";
    public bool IsCreature { get; set; }
    public double? NaturalAttackDamage { get; set; }
    public double CreaturePowerAttackMultiplier { get; set; } = 1;
    public double NaturalAttackSpeed { get; set; } = 1;
    public string? ActorKey { get; set; }
    public int Tier { get; set; } = 3;
    public bool UseTierTargets { get; set; }
    public string Weapon { get; set; } = "000C0C:Oblivion.esm";
    public string[] Armor { get; set; } = [];
    public string Style { get; set; } = "Aggressive Fighter";
    public double Health { get; set; } = 140;
    public double Strength { get; set; } = 50;
    public double Endurance { get; set; } = 50;
    public double WeaponPower { get; set; } = 1;
    public double WeaponSkill { get; set; } = 50;
    public double LightArmorSkill { get; set; } = 50;
    public double HeavyArmorSkill { get; set; } = 50;
    public double BlockSkill { get; set; } = 50;
    public double Luck { get; set; } = 50;
    public double MaxFatigue { get; set; } = 200;
    public double StartingFatiguePercent { get; set; } = 100;
    public double FatigueRegen { get; set; } = 5;
    public double ConditionPercent { get; set; } = 100;
    public double DamageMultiplier { get; set; } = 1;
    public double ResistNormalWeapons { get; set; }
}
public sealed class CombatScenario
{
    public Fighter Player { get; set; } = new();
    public Fighter Enemy { get; set; } = new();
    public int EnemyCount { get; set; } = 1;
    public double Seconds { get; set; } = 60;
    public bool UseNativeResources { get; set; } = true;
}
public sealed record CombatPoint(double Seconds, double PlayerHealth, double EnemyHealth, double PlayerFatigue, double EnemyFatigue, int EnemiesAlive);
public readonly record struct CombatHitFactors(double BaseWeaponDamage, double WeaponMultiplier, double StrengthFactor,
    double SkillFactor, double ConditionFactor, double FatigueFactor, double ArmorFactor,
    double ResistanceFactor, double OutgoingMultiplier, double BenchmarkMultiplier)
{
    public double Damage => BaseWeaponDamage * WeaponMultiplier * StrengthFactor * SkillFactor * ConditionFactor
        * FatigueFactor * ArmorFactor * ResistanceFactor * OutgoingMultiplier * BenchmarkMultiplier;
}
public sealed record CombatSummary(double PlayerCleanHit, double EnemyCleanHit, double PlayerArmor, double EnemyArmor,
    double PlayerBlockReduction, double EnemyBlockReduction, double? PlayerTimeToDefeat, double? EnemyTimeToDefeat,
    double PlayerHealthRemaining, double EnemyHealthRemaining, double PlayerFatigueRemaining, double EnemyFatigueRemaining,
    CombatPoint[] Timeline, CombatHitFactors PlayerHitFactors, CombatHitFactors EnemyHitFactors);

public static class CombatAnalysis
{
    public static IReadOnlyDictionary<string, double> Defaults { get; } = new Dictionary<string, double>
    {
        ["fActorLuckSkillMult"] = .4, ["iActorLuckSkillBase"] = 20, ["fDamageWeaponMult"] = .5, ["fDamageStrengthBase"] = .75, ["fDamageStrengthMult"] = .5,
        ["fDamageSkillBase"] = .2, ["fDamageSkillMult"] = 1.5, ["fDamageWeaponConditionBase"] = .5,
        ["fDamageWeaponConditionMult"] = .5, ["fFatigueBase"] = 1, ["fFatigueMult"] = .5,
        ["fArmorRatingBase"] = .35, ["fArmorRatingMax"] = 1, ["fMaxArmorRating"] = 85,
        ["fBlockSkillBase"] = 0, ["fBlockSkillMult"] = 1, ["fBlockAmountWeaponMult"] = .5, ["fBlockMax"] = .75,
        ["fArmorRatingConditionBase"] = 0, ["fArmorRatingConditionMult"] = 1,
        ["fFatigueAttackWeaponBase"] = 7, ["fFatigueAttackWeaponMult"] = .1, ["fFatigueReturnBase"] = 10, ["fFatigueReturnMult"] = 0,
        ["fPowerAttackFatiguePenalty"] = 5, ["fDamagePowerAttackBonus"] = 2.5
    };
    public static void Validate(CombatScenario scenario, CombatSettings settings, IReadOnlyDictionary<string, PhysicalItem> items)
    {
        if (scenario.Player is null || scenario.Enemy is null) throw new ArgumentException("Choose both combatants.");
        CombatConfiguration.Range(scenario.EnemyCount, 1, 12, "Enemy count"); CombatConfiguration.Range(scenario.Seconds, 1, 300, "Scenario duration");
        foreach (var fighter in new[] { scenario.Player, scenario.Enemy })
        {
            if(fighter.NaturalAttackDamage.HasValue) { if(!fighter.IsCreature)throw new ArgumentException("Natural attacks require a creature."); CombatConfiguration.Range(fighter.NaturalAttackDamage.Value,0,65535,"Natural attack damage"); }
            CombatConfiguration.Range(fighter.CreaturePowerAttackMultiplier,1,10,"Creature power attack multiplier");
            CombatConfiguration.Range(fighter.NaturalAttackSpeed,.1,5,"Creature attack speed assumption");
            CombatConfiguration.Range(fighter.Tier, 0, 10, "Tier"); CombatConfiguration.Range(fighter.Health, 1, 100000, "Health");
            CombatConfiguration.Range(fighter.MaxFatigue, 1, 10000, "Fatigue"); CombatConfiguration.Range(fighter.FatigueRegen, 0, 100, "Fatigue regeneration");
            foreach (var value in new[] { fighter.Strength, fighter.Endurance, fighter.WeaponSkill, fighter.LightArmorSkill, fighter.HeavyArmorSkill, fighter.BlockSkill, fighter.Luck }) CombatConfiguration.Range(value, 0, 200, "Attribute or skill");
            CombatConfiguration.Range(fighter.ConditionPercent, 1, 125, "Equipment condition");
            CombatConfiguration.Range(fighter.StartingFatiguePercent, 0, 100, "Starting fatigue");
            CombatConfiguration.Range(fighter.DamageMultiplier, .01, 20, "Outgoing damage multiplier");
            CombatConfiguration.Range(fighter.WeaponPower, 1, 20, "Weapon variant power");
            CombatConfiguration.Range(fighter.ResistNormalWeapons, 0, 100, "Normal weapon resistance");
            if (!settings.Styles.ContainsKey(fighter.Style)) throw new ArgumentException("Unknown combat style assumption.");
            if (!items.TryGetValue(fighter.Weapon, out var weapon) || weapon.Kind != "Weapon" || weapon.Class == "Bow" || weapon.NativeType is "Bow" or "Staff")
                throw new ArgumentException("Choose a melee weapon. Bows and hand-to-hand need separate models.");
            if (fighter.Armor is null || fighter.Armor.Length > 10 || fighter.Armor.Distinct().Count() != fighter.Armor.Length) throw new ArgumentException("Invalid armor loadout.");
            var slots = new HashSet<string>();
            foreach (var key in fighter.Armor)
            {
                if (!items.TryGetValue(key, out var armor) || armor.Kind == "Weapon") throw new ArgumentException("Unknown armor or shield.");
                foreach (var slot in armor.Slots.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (!slots.Add(slot)) throw new ArgumentException($"Overlapping armor slot: {slot}");
                if (armor.Kind == "Shield" && (weapon.NativeType is "BladeTwoHand" or "BluntTwoHand" || weapon.Class is "Claymore" or "Battleaxe" or "Warhammer" or "BladeTwoHand" or "BluntTwoHand"))
                    throw new ArgumentException("A two-handed weapon cannot use a shield.");
            }
        }
    }

    // Continuous expected-pressure model. Contact, cadence and blocking are editable assumptions.
    public static CombatSummary Run(CombatScenario scenario, CombatSettings settings, IReadOnlyDictionary<string, PhysicalItem> items,
        IReadOnlyDictionary<string, double> gameSettings, bool proposed)
    {
        Validate(scenario, settings, items);
        var effectiveSettings = new Dictionary<string,double>(gameSettings);
        if(proposed && settings.Gameplay.BalanceGameSettings) foreach(var pair in settings.Gameplay.GameSettings) effectiveSettings[pair.Key]=pair.Value;
        double GS(string name) => effectiveSettings.GetValueOrDefault(name, Defaults[name]);
        var fighters = new[] { scenario.Player, scenario.Enemy };
        var weapons = fighters.Select(f => proposed ? PhysicalBalance.Propose(items[f.Weapon], settings) : items[f.Weapon]).ToArray();
        var loadouts = fighters.Select(f => f.Armor.Select(key => proposed ? PhysicalBalance.Propose(items[key], settings) : items[key]).ToArray()).ToArray();
        var styles = fighters.Select(f => settings.Styles[f.Style]).ToArray();
        double Skill(double value, Fighter f) => Math.Clamp(value + GS("fActorLuckSkillMult") * f.Luck - GS("iActorLuckSkillBase"), 0, 100);
        var armor = fighters.Select((f, i) => Math.Clamp(loadouts[i].Sum(item => item.Armor *
            (GS("fArmorRatingBase") + Skill(item.Heavy ? f.HeavyArmorSkill : f.LightArmorSkill, f) / 100 *
             (GS("fArmorRatingMax") - GS("fArmorRatingBase")))) * (GS("fArmorRatingConditionBase") + GS("fArmorRatingConditionMult") * f.ConditionPercent / 100),
            0, GS("fMaxArmorRating"))).ToArray();
        var block = fighters.Select((f, i) => Math.Clamp((GS("fBlockSkillBase") + Skill(f.BlockSkill, f) / 100 * GS("fBlockSkillMult")) *
            (loadouts[i].Any(item => item.Kind == "Shield") ? 1 : GS("fBlockAmountWeaponMult")), 0, GS("fBlockMax"))).ToArray();
        var maxHealth = fighters.Select(f => proposed && f.UseTierTargets ? settings.HealthTarget.At(f.Tier) : f.Health).ToArray();
        var health = (double[])maxHealth.Clone();
        var fatigue = fighters.Select(f => f.MaxFatigue * f.StartingFatiguePercent / 100).ToArray();
        CombatHitFactors Factors(int i, double fatigueValue)
        {
            var f = fighters[i]; var weapon = weapons[i]; var defender = 1 - i;
            if(f.NaturalAttackDamage is {} natural)
                return new(natural,1,1,1,1,Math.Max(0,GS("fFatigueBase")-GS("fFatigueMult")*(1-fatigueValue/f.MaxFatigue)),1-armor[defender]/100,1-fighters[defender].ResistNormalWeapons/100,f.DamageMultiplier,1);
            var resistance = weapon.Enchanted || weapon.IgnoresNormalWeaponResistance ? 0 : fighters[defender].ResistNormalWeapons;
            var tierMult = proposed && f.UseTierTargets ? settings.OffenseTarget.At(f.Tier) / settings.OffenseTarget.At(3) : 1;
            return new(Math.Round(weapon.Damage * (proposed ? f.WeaponPower : 1), MidpointRounding.AwayFromZero), GS("fDamageWeaponMult"),
                GS("fDamageStrengthBase") + Math.Clamp(f.Strength, 0, 100) / 100 * GS("fDamageStrengthMult"),
                GS("fDamageSkillBase") + Skill(f.WeaponSkill, f) / 100 * GS("fDamageSkillMult"),
                GS("fDamageWeaponConditionBase") + f.ConditionPercent / 100 * GS("fDamageWeaponConditionMult"),
                Math.Max(0, GS("fFatigueBase") - GS("fFatigueMult") * (1 - fatigueValue / f.MaxFatigue)),
                1 - armor[defender] / 100, 1 - resistance / 100, f.DamageMultiplier, tierMult);
        }
        double Hit(int i, double fatigueValue) => Factors(i, fatigueValue).Damage;
        var cleanHits = new[] { Hit(0, fighters[0].MaxFatigue), Hit(1, fighters[1].MaxFatigue) };
        var timeline = new List<CombatPoint>(); double? playerDefeat = null; double? enemyDefeat = null;
        var enemyIndex = 0; var stepCount = (int)Math.Ceiling(scenario.Seconds * 10);
        void Point(double time) => timeline.Add(new(Math.Round(time, 2), health[0], health[1] + Math.Max(0, scenario.EnemyCount - enemyIndex - 1) * maxHealth[1], fatigue[0], fatigue[1], scenario.EnemyCount - enemyIndex));
        Point(0);
        for (var step = 1; step <= stepCount; step++)
        {
            var dt = Math.Min(.1, scenario.Seconds - (step - 1) * .1); var alive = scenario.EnemyCount - enemyIndex;
            var attackRates = new[] { styles[0].AttacksPerSecond * (fighters[0].NaturalAttackDamage.HasValue?fighters[0].NaturalAttackSpeed:weapons[0].Speed), styles[1].AttacksPerSecond * (fighters[1].NaturalAttackDamage.HasValue?fighters[1].NaturalAttackSpeed:weapons[1].Speed) * alive };
            var damage = new double[2]; var spend = new double[2];
            for (var i = 0; i < 2; i++)
            {
                var defender = 1 - i; var power = styles[i].PowerAttackShare;
                damage[i] = Hit(i, fatigue[i]) * attackRates[i] * styles[i].ContactRate *
                    (1 + power * ((fighters[i].IsCreature?fighters[i].CreaturePowerAttackMultiplier:scenario.UseNativeResources ? GS("fDamagePowerAttackBonus") : settings.PowerAttackDamageMult) - 1)) * (1 - styles[defender].BlockUptime * block[defender]) * dt;
                var ownAttackRate = attackRates[i]/(i==1?alive:1);
                spend[i] = ownAttackRate * ((scenario.UseNativeResources ? GS("fFatigueAttackWeaponBase") : settings.AttackFatigueBase) + (fighters[i].NaturalAttackDamage.HasValue?0:weapons[i].Weight) * (scenario.UseNativeResources ? GS("fFatigueAttackWeaponMult") : settings.AttackFatigueWeight)) *
                    (1 + power * (scenario.UseNativeResources ? GS("fPowerAttackFatiguePenalty") : settings.PowerAttackFatigueMult - 1));
                var incomingRate = attackRates[defender] / (i == 1 ? alive : 1);
                var blockCost=settings.BlockFatigueBase + (fighters[defender].NaturalAttackDamage.HasValue?0:weapons[defender].Weight) * settings.BlockFatigueWeight;
                // This first-pass GMST profile makes novice block cost constant. Mastery is engine behavior.
                if(scenario.UseNativeResources && effectiveSettings.GetValueOrDefault("fFatigueBlockBase")==1
                    && effectiveSettings.GetValueOrDefault("fFatigueBlockMult")==0 && effectiveSettings.GetValueOrDefault("fFatigueBlockSkillMult")==0)
                    blockCost=fighters[i].BlockSkill>=50?0:effectiveSettings.GetValueOrDefault("fFatigueBlockSkillBase",8);
                spend[i] += incomingRate * styles[defender].ContactRate * styles[i].BlockUptime * blockCost;
            }
            health[0] = Math.Max(0, health[0] - damage[1]); health[1] -= damage[0];
            for (var i = 0; i < 2; i++) fatigue[i] = Math.Clamp(fatigue[i] + ((scenario.UseNativeResources ? GS("fFatigueReturnBase") + fighters[i].Endurance * GS("fFatigueReturnMult") : fighters[i].FatigueRegen) - spend[i]) * dt, 0, fighters[i].MaxFatigue);
            if (health[0] <= 0) playerDefeat = step * .1;
            if (health[1] <= 0)
            {
                enemyIndex++;
                if (enemyIndex == scenario.EnemyCount) { health[1] = 0; enemyDefeat = step * .1; }
                else { health[1] = maxHealth[1]; fatigue[1] = fighters[1].MaxFatigue * fighters[1].StartingFatiguePercent / 100; }
            }
            if (step % 10 == 0 || playerDefeat.HasValue || enemyDefeat.HasValue || step == stepCount) Point(Math.Min(step * .1, scenario.Seconds));
            if (playerDefeat.HasValue || enemyDefeat.HasValue) break;
        }
        return new(cleanHits[0], cleanHits[1], armor[0], armor[1], block[0], block[1], playerDefeat, enemyDefeat,
            health[0], health[1] + Math.Max(0, scenario.EnemyCount - enemyIndex - 1) * maxHealth[1], fatigue[0], fatigue[1], timeline.ToArray(), Factors(0, fighters[0].MaxFatigue), Factors(1, fighters[1].MaxFatigue));
    }
}
