using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaldursGateStyleOblivion.Combat;

public sealed class ItemModifiers
{
    public double Damage { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public double Reach { get; set; } = 1;
    public double Weight { get; set; } = 1;
    public double Durability { get; set; } = 1;
    public double Armor { get; set; } = 1;
}

public sealed class CombatItemOverride
{
    public string? Material { get; set; }
    public string? Class { get; set; }
    public bool Preserve { get; set; }
    public bool NormalizeProtected { get; set; }
    public ItemModifiers Modifiers { get; set; } = new();
}

public sealed class TierCurve
{
    public int Knee { get; set; } = 6;
    public double Base { get; set; } = 1;
    public double LinearGain { get; set; } = .2;
    public double RareGrowth { get; set; } = 1.45;
    public double At(int tier) => (Base + LinearGain * Math.Min(tier, Knee)) * Math.Pow(RareGrowth, Math.Max(0, tier - Knee));
}

// These are scenario assumptions, not native combat-style probabilities or AI predictions.
public sealed class StyleAssumptions
{
    public double AttacksPerSecond { get; set; } = .8;
    public double ContactRate { get; set; } = .75;
    public double BlockUptime { get; set; } = .25;
    public double PowerAttackShare { get; set; } = .15;
}

public sealed class QuickTierProfile
{
    public string WeaponMaterial { get; set; } = "Iron";
    public string ArmorMaterial { get; set; } = "Iron";
    public string LightArmorMaterial { get; set; } = "Leather";
}

public sealed class QuickClassProfile
{
    public string Build { get; set; } = "Balanced";
    public string Equipment { get; set; } = "Sword & shield";
}
public sealed class CombatSettings
{
    public GameplaySettings Gameplay { get; set; } = new();
    public Dictionary<string, QuickClassProfile> QuickClasses { get; set; } = new()
    {
        ["Warrior"] = new(),
        ["Knight"] = new() { Build = "Defensive" },
        ["Barbarian"] = new() { Build = "Berserker", Equipment = "Two-handed / light armor" },
        ["Rogue"] = new() { Build = "Skirmisher", Equipment = "Dagger / light armor" },
        ["Scout"] = new() { Build = "Skirmisher", Equipment = "Sword / light armor" },
        ["Spellsword"] = new(),
        ["Battlemage"] = new() { Equipment = "Two-handed / heavy armor" }
    };
    public Dictionary<int, QuickTierProfile> QuickTiers { get; set; } = CombatConfiguration.DefaultQuickTiers();
    public Dictionary<string, ItemModifiers> Materials { get; set; } = new();
    public Dictionary<string, ItemModifiers> WeaponClasses { get; set; } = new();
    public Dictionary<string, ItemModifiers> ArmorClasses { get; set; } = new();
    public Dictionary<string, CombatItemOverride> ItemOverrides { get; set; } = new();
    public Dictionary<string, StyleAssumptions> Styles { get; set; } = new();
    public TierCurve OffenseTarget { get; set; } = new();
    public TierCurve HealthTarget { get; set; } = new() { Base = 80, LinearGain = 20, RareGrowth = 1.12 };
    public double AttackFatigueBase { get; set; } = 3;
    public double AttackFatigueWeight { get; set; } = .1;
    public double BlockFatigueBase { get; set; } = 3;
    public double BlockFatigueWeight { get; set; } = .1;
    public double PowerAttackFatigueMult { get; set; } = 2.5;
    public double PowerAttackDamageMult { get; set; } = 2.5;
}

public static class CombatConfiguration
{
    public static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static Dictionary<int, QuickTierProfile> DefaultQuickTiers()
    {
        string[] weapons = ["Iron", "Iron", "Iron", "Steel", "Steel", "Dwarven", "Elven", "Glass", "Ebony", "Daedric", "Daedric"];
        string[] armor = ["Fur", "Iron", "Iron", "Steel", "Steel", "Dwarven", "Orcish", "Ebony", "Daedric", "Daedric", "Daedric"];
        string[] lightArmor = ["Fur", "Leather", "Leather", "Chainmail", "Chainmail", "Mithril", "Elven", "Glass", "Glass", "Glass", "Glass"];
        return Enumerable.Range(0, 11).ToDictionary(tier => tier, tier => new QuickTierProfile
        { WeaponMaterial = weapons[tier], ArmorMaterial = armor[tier], LightArmorMaterial = lightArmor[tier] });
    }
    public static CombatSettings Load(string path) => Parse(File.ReadAllText(path));
    public static CombatSettings Parse(string text)
    {
        var settings = JsonSerializer.Deserialize<CombatSettings>(text, Options) ?? throw new ArgumentException("Empty combat configuration.");
        Validate(settings); return settings;
    }
    public static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"{name} must be between {min} and {max}.");
    }
    public static void Validate(CombatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settings.Gameplay); settings.Gameplay.Validate();
        if (settings.Materials is null || settings.WeaponClasses is null || settings.ArmorClasses is null || settings.Styles is null || settings.ItemOverrides is null)
            throw new ArgumentException("Combat tables cannot be null.");
        if (settings.QuickTiers is null || settings.QuickTiers.Count != 11 || Enumerable.Range(0, 11).Any(tier => !settings.QuickTiers.ContainsKey(tier)))
            throw new ArgumentException("Quick build profiles must cover tiers 0–10.");
        foreach (var profile in settings.QuickTiers.Values)
        {
            if (profile is null || !settings.Materials.ContainsKey(profile.WeaponMaterial) || !settings.Materials.ContainsKey(profile.ArmorMaterial) || !settings.Materials.ContainsKey(profile.LightArmorMaterial))
                throw new ArgumentException("Quick build profiles require known weapon and armor materials.");
        }
        if (settings.QuickClasses is null || settings.QuickClasses.Count == 0) throw new ArgumentException("Quick classes are required.");
        foreach (var profile in settings.QuickClasses.Values)
        {
            if (profile is null || !new[] { "Balanced", "Defensive", "Berserker", "Skirmisher" }.Contains(profile.Build)
                || !new[] { "Sword & shield", "Two-handed / heavy armor", "Two-handed / light armor", "Sword / light armor", "Dagger / light armor", "Mace & shield", "Warhammer / heavy armor", "Axe & shield" }.Contains(profile.Equipment))
                throw new ArgumentException("Unknown quick class build or equipment preset.");
        }
        settings.ItemOverrides = new(settings.ItemOverrides, StringComparer.OrdinalIgnoreCase);
        void Modifiers(ItemModifiers? value)
        {
            if (value is null) throw new ArgumentException("Modifiers cannot be null.");
            foreach (var property in typeof(ItemModifiers).GetProperties()) Range((double)property.GetValue(value)!, .05, 10, property.Name);
        }
        foreach (var table in new[] { settings.Materials, settings.WeaponClasses, settings.ArmorClasses })
            foreach (var pair in table) { if (string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("A profile needs a name."); Modifiers(pair.Value); }
        foreach (var pair in settings.ItemOverrides)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(pair.Key, @"^[0-9A-Fa-f]{6,8}:[^:]+\.(esm|esp)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new ArgumentException($"Invalid item FormKey: {pair.Key}");
            var rule = pair.Value ?? throw new ArgumentException("Item overrides cannot be null."); Modifiers(rule.Modifiers);
            if (rule.Material is not null && !settings.Materials.ContainsKey(rule.Material)) throw new ArgumentException("Unknown material override.");
            if (rule.Class is not null && !settings.WeaponClasses.ContainsKey(rule.Class) && !settings.ArmorClasses.ContainsKey(rule.Class)) throw new ArgumentException("Unknown class override.");
        }
        foreach (var pair in settings.Styles)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) throw new ArgumentException("Invalid style.");
            Range(pair.Value.AttacksPerSecond, .05, 5, "Attack cadence");
            Range(pair.Value.ContactRate, 0, 1, "Contact rate"); Range(pair.Value.BlockUptime, 0, 1, "Blocking uptime");
            Range(pair.Value.PowerAttackShare, 0, 1, "Power attack share");
        }
        if (settings.Styles.Count == 0) throw new ArgumentException("At least one style is required.");
        foreach (var curve in new[] { settings.OffenseTarget, settings.HealthTarget })
        {
            if (curve is null) throw new ArgumentException("Tier curves cannot be null.");
            Range(curve.Knee, 0, 10, "Rare-tier threshold"); Range(curve.Base, .01, 10000, "Curve base");
            Range(curve.LinearGain, 0, 1000, "Linear gain"); Range(curve.RareGrowth, 1, 3, "Rare growth");
        }
        Range(settings.AttackFatigueBase, 0, 100, "Attack fatigue base"); Range(settings.AttackFatigueWeight, 0, 10, "Attack fatigue weight");
        Range(settings.BlockFatigueBase, 0, 100, "Block fatigue base"); Range(settings.BlockFatigueWeight, 0, 10, "Block fatigue weight");
        Range(settings.PowerAttackFatigueMult, 1, 10, "Power attack fatigue"); Range(settings.PowerAttackDamageMult, 1, 10, "Power attack damage");
    }
}

public sealed record PhysicalItem(string FormKey, string? EditorID, string Name, string Kind, string Class, string Material,
    double Damage, double Speed, double Reach, double Weight, uint Durability, double Armor, uint Value,
    string Slots, bool Heavy, bool Enchanted, bool Protected, string? PreservationReason, string NativeType = "", bool IgnoresNormalWeaponResistance = false);

public static class PhysicalBalance
{
    public static PhysicalItem Propose(PhysicalItem source, CombatSettings settings)
    {
        var rule = settings.ItemOverrides.GetValueOrDefault(source.FormKey);
        if (rule?.Preserve == true || source.Protected && rule?.NormalizeProtected != true
            && !(settings.Gameplay.BalanceEnchantedPhysicalStats && source.PreservationReason == "Enchanted item: enchantment balance pending")) return source;
        if (source.EditorID?.StartsWith("BGSOCombatTier", StringComparison.Ordinal) == true) return source;
        var material = rule?.Material ?? source.Material; var itemClass = rule?.Class ?? source.Class;
        var tables = source.Kind == "Weapon" ? settings.WeaponClasses : settings.ArmorClasses;
        if (rule?.Class is not null && !tables.ContainsKey(rule.Class)) throw new ArgumentException($"Invalid class {rule.Class} for {source.Kind} {source.FormKey}.");
        var modifiers = new[] { settings.Materials.GetValueOrDefault(material) ?? new(), tables.GetValueOrDefault(itemClass) ?? new(), rule?.Modifiers ?? new() };
        double Factor(Func<ItemModifiers, double> field) => modifiers.Aggregate(1d, (value, entry) => value * field(entry));
        var baseline = source;
        if (settings.Gameplay.NormalizeEquipment && settings.Materials.ContainsKey(material) && material != "Unknown")
        {
            if (source.Kind == "Weapon" && settings.Gameplay.WeaponBaselines.TryGetValue(itemClass, out var weapon))
                baseline = source with { Damage = weapon.Damage, Speed = weapon.Speed, Reach = weapon.Reach, Weight = weapon.Weight, Durability = weapon.Durability };
            else if (source.Kind != "Weapon")
            {
                var rating = source.Slots.Split(',', StringSplitOptions.TrimEntries).Sum(slot => settings.Gameplay.ArmorSlots.GetValueOrDefault(slot));
                if (rating > 0) baseline = source with { Armor = rating, Weight = rating * (source.Heavy ? .9 : .35), Durability = (uint)(rating * (source.Heavy ? 20 : 10)) };
            }
        }
        // Match native field precision so the editor and patcher agree exactly.
        double Integer(double value, double max) => Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, max);
        float ArmorValue(double value)
        {
            var hundredths=Integer(value*100,ushort.MaxValue);var result=(float)(hundredths/100);
            // The native writer truncates a floating-point value back to integer hundredths.
            if((double)result*100<hundredths)result=MathF.BitIncrement(result);
            return result;
        }
        return source with { Material = material, Class = itemClass,
            Damage = Integer(baseline.Damage * Factor(m => m.Damage), ushort.MaxValue),
            Armor = ArmorValue(baseline.Armor * Factor(m => m.Armor)),
            Speed = (float)(baseline.Speed * Factor(m => m.Speed)), Reach = (float)(baseline.Reach * Factor(m => m.Reach)),
            Weight = (float)(baseline.Weight * Factor(m => m.Weight)), Durability = (uint)Integer(baseline.Durability * Factor(m => m.Durability), uint.MaxValue) };
    }
}
