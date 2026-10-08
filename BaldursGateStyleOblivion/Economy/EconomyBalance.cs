using System.Text.Json;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Enhancements;
namespace BaldursGateStyleOblivion.Economy;

public sealed record EconomyEntry(string Key, int Count, int Level = 1, bool ScaleCoins = false);
public sealed record EconomyPool(string Key, double ChanceNone, bool UseAll, bool Each, EconomyEntry[] Entries);
public sealed record EconomySource(string Key, string Name, int Tier, string Category, string Kind, EconomyEntry[] Entries);
public sealed record EconomyMerchant(string Key, string Name, string Profile, int Gold, int Mercantile, string[] Buys);
public sealed record EconomySpell(string Key, string Name, string School, double BaseCost, bool Manual);
public sealed class EconomyCatalog
{
    public EconomyItem[] Items { get; set; } = [];
    public EconomyPool[] Pools { get; set; } = [];
    public EconomySource[] Sources { get; set; } = [];
    public EconomyMerchant[] Merchants { get; set; } = [];
    public EconomySpell[] Spells { get; set; } = [];
    public Dictionary<string, double> SpellSettings { get; set; } = new();
}

public sealed class EconomyMerchantProfile
{
    public int Gold { get; set; } = 600;
    public int Mercantile { get; set; } = 40;
}
public sealed class EconomyItemOverride
{
    public bool Preserve { get; set; }
    public uint? Value { get; set; }
    public double? Prestige { get; set; }
    public string Reason { get; set; } = "Individual price exception.";
}
public sealed class EconomyOuting
{
    public int Enemies { get; set; } = 3;
    public int Containers { get; set; } = 2;
    public int BossContainers { get; set; }
    public int Ingredients { get; set; }
    public int HealingPotions { get; set; } = 2;
    public int Poisons { get; set; }
    public double RepairWear { get; set; } = .08;
    public double ChargeSpent { get; set; }
    public double Hours { get; set; } = 2;
}
public sealed class EconomySettings
{
    public bool Enabled { get; set; } = true;
    public double CoinLootMultiplier { get; set; } = .75;
    public Dictionary<string, double> Materials { get; set; } = new() { ["Iron"] = 1, ["Steel"] = 1.9, ["Silver"] = 2.5, ["Dwarven"] = 4, ["Elven"] = 6, ["Orcish"] = 6, ["Glass"] = 11, ["Ebony"] = 18, ["Daedric"] = 30, ["Amber"] = 12, ["Madness"] = 19, ["Fur"] = .45, ["Leather"] = .7, ["Chainmail"] = 3, ["Mithril"] = 5 };
    public Dictionary<string, double> Weapons { get; set; } = new() { ["Dagger"] = 12, ["Shortsword"] = 28, ["Longsword"] = 45, ["Claymore"] = 80, ["Waraxe"] = 40, ["Battleaxe"] = 75, ["Mace"] = 45, ["Warhammer"] = 90, ["Bow"] = 55 };
    public Dictionary<string, double> ArmorSlots { get; set; } = new() { ["Hair"] = 35, ["Head"] = 0, ["UpperBody"] = 95, ["LowerBody"] = 55, ["Hand"] = 25, ["Foot"] = 35, ["Shield"] = 60 };
    public Dictionary<string, double> Categories { get; set; } = new() { ["Weapon"] = 1, ["Armor"] = 1, ["Shield"] = 1, ["Clothing"] = 1, ["Jewelry"] = 1, ["Potion"] = .85, ["Ingredient"] = 1, ["Apparatus"] = 1, ["Soul gem"] = 1, ["Scroll"] = 1, ["Book"] = 1, ["Valuable"] = 1, ["Clutter"] = 1, ["Ammo"] = 1, ["Sigil stone"] = 1 };
    public double EnchantmentDamageValue { get; set; } = 9;
    public double EnchantmentBonusValue { get; set; } = 18;
    public double EnchantmentDefenseValue { get; set; } = 14;
    public double ScrollEffectMultiplier { get; set; } = .2;
    public double AmmoBaseValue { get; set; } = .45;
    public double AmmoEffectMultiplier { get; set; } = .2;
    public double[] Prestige { get; set; } = [1, 1, 1, 1.05, 1.08, 1.12, 1.2, 1.3, 1.4, 1.5, 1.6];
    public double[] EmptySoulGems { get; set; } = [0, 8, 18, 40, 80, 160];
    public double[] FilledSoulGems { get; set; } = [0, 25, 55, 110, 220, 400];
    public Dictionary<string, EconomyMerchantProfile> Merchants { get; set; } = new()
    {
        ["General Store"] = new() { Gold = 600, Mercantile = 35 },
        ["Poor Blacksmith"] = new() { Gold = 400, Mercantile = 30 },
        ["Professional Blacksmith"] = new() { Gold = 1000, Mercantile = 50 },
        ["Luxury Merchant"] = new() { Gold = 1800, Mercantile = 65 },
        ["Alchemist"] = new() { Gold = 600, Mercantile = 40 },
        ["Expert Alchemist"] = new() { Gold = 1200, Mercantile = 60 },
        ["Mage Vendor"] = new() { Gold = 1000, Mercantile = 50 },
        ["Jeweler"] = new() { Gold = 1600, Mercantile = 60 },
        ["Guild Vendor"] = new() { Gold = 900, Mercantile = 40 },
        ["Specialist"] = new() { Gold = 1000, Mercantile = 50 },
        ["Expert Specialist"] = new() { Gold = 1600, Mercantile = 65 },
        ["Fence"] = new() { Gold = 1400, Mercantile = 60 },
        ["RareGoods"] = new() { Gold = 2000, Mercantile = 70 }
    };
    public Dictionary<string, EconomyMerchantProfile> MerchantOverrides { get; set; } = new();
    public Dictionary<string, EconomyItemOverride> ItemOverrides { get; set; } = new();
    public Dictionary<string, double> GameSettings { get; set; } = new() { ["fRepairCostMult"] = .55, ["fRechargeGoldMult"] = .1, ["fTrainingCostMult"] = 8, ["fSpellmakingGoldMult"] = 3, ["fEnchantmentGoldMult"] = 8, ["fBarterSellBase"] = -20, ["fBarterBuyBase"] = 300, ["fBarterSellMult"] = 5, ["fBarterBuyMult"] = 9.9, ["iPerkExtraBarterGoldMaster"] = 500 };
    public Dictionary<string, EconomyOuting> Outings { get; set; } = new()
    {
        ["Short outing"] = new(),
        ["Dungeon delve"] = new() { Enemies = 6, Containers = 4, HealingPotions = 3, Poisons = 1, RepairWear = .15, ChargeSpent = 250, Hours = 4 },
        ["Treasure hunt"] = new() { Enemies = 3, Containers = 2, BossContainers = 1, HealingPotions = 3, Poisons = 1, RepairWear = .1, ChargeSpent = 150, Hours = 3 },
        ["Gathering trip"] = new() { Enemies = 0, Containers = 0, Ingredients = 30, HealingPotions = 0, RepairWear = 0, Hours = 2 }
    };
}
public sealed record EconomyItem(string Key, string Name, string? EditorID, string Kind, string Material, string Class, string Slots, int Tier, uint Value, double Weight, double Damage, double Armor, uint Durability, bool Enchanted, bool Unique, bool Preserve, string Reason, EnhancementEffect[] Effects, int ChargedHits = 0, int SoulCapacity = 0, int Soul = 0,bool Lootable=true);
public sealed record EconomyPrice(string Key, uint Value, double Equipment, double Enchantment, double Prestige, bool Preserved, string Reason);
public static class EconomyBalance
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static EconomySettings Load(string path) => Parse(File.ReadAllText(path));
    public static EconomySettings Parse(string json) { var s = JsonSerializer.Deserialize<EconomySettings>(json, Options) ?? throw new ArgumentException("Economy settings missing."); Validate(s); return s; }
    public static void Validate(EconomySettings s)
    {
        void Range(double v, double lo, double hi, string name) => CombatConfiguration.Range(v, lo, hi, name);
        if (s.Materials is null || s.Weapons is null || s.ArmorSlots is null || s.Categories is null || s.Merchants is null || s.MerchantOverrides is null || s.ItemOverrides is null || s.Outings is null || s.GameSettings is null) throw new ArgumentException("Economy tables cannot be null.");
        Range(s.CoinLootMultiplier, 0, 3, "Coin loot multiplier");
        foreach (var v in s.Materials.Values) Range(v, .1, 100, "Material price"); foreach (var v in s.Weapons.Values) Range(v, 1, 1000, "Weapon baseline price"); foreach (var v in s.ArmorSlots.Values) Range(v, 0, 1000, "Armor slot price"); foreach (var v in s.Categories.Values) Range(v, .1, 5, "Category multiplier");
        foreach (var v in new[] { s.EnchantmentDamageValue, s.EnchantmentBonusValue, s.EnchantmentDefenseValue }) Range(v, 0, 100, "Effect price"); Range(s.ScrollEffectMultiplier, .01, 1, "Scroll effect multiplier"); Range(s.AmmoBaseValue, .1, 10, "Arrow base price"); Range(s.AmmoEffectMultiplier, .01, 1, "Arrow effect share");
        foreach (var table in new[] { s.Prestige, s.EmptySoulGems, s.FilledSoulGems }) if (table is null) throw new ArgumentException("Economy tier tables missing.");
        if (s.Prestige.Length != 11 || s.EmptySoulGems.Length != 6 || s.FilledSoulGems.Length != 6) throw new ArgumentException("Prestige requires 11 tiers; soul gems require 6 capacities.");
        foreach (var v in s.Prestige) Range(v, 1, 5, "Prestige premium"); foreach (var v in s.EmptySoulGems.Concat(s.FilledSoulGems)) Range(v, 0, 10000, "Soul gem price");
        foreach (var p in s.Merchants.Values.Concat(s.MerchantOverrides.Values)) { if (p is null) throw new ArgumentException("Merchant profile missing."); Range(p.Gold, 1, 10000, "Merchant transaction gold"); Range(p.Mercantile, 0, 100, "Merchant Mercantile"); }
        foreach (var p in s.ItemOverrides.Values) { if (p is null) throw new ArgumentException("Price override missing."); if (p.Value.HasValue) Range(p.Value.Value, 0, 100000, "Item value"); if (p.Prestige.HasValue) Range(p.Prestige.Value, 1, 5, "Item prestige"); }
        foreach (var p in s.Outings.Values) { if (p is null) throw new ArgumentException("Outing preset missing."); foreach (var n in new[] { p.Enemies, p.Containers, p.BossContainers, p.HealingPotions, p.Poisons }) Range(n, 0, 30, "Outing count"); Range(p.Ingredients, 0, 100, "Gathered ingredients"); Range(p.RepairWear, 0, 1, "Wear fraction"); Range(p.ChargeSpent, 0, 10000, "Charge spent"); Range(p.Hours, .1, 24, "Outing time"); }
        foreach (var p in s.GameSettings) { if (!new EconomySettings().GameSettings.ContainsKey(p.Key)) throw new ArgumentException("Unsupported economy game setting: " + p.Key); Range(p.Value, p.Key == "fBarterSellBase" ? -100 : 0, 1000, p.Key); if (p.Key.StartsWith('i') && p.Value != Math.Round(p.Value)) throw new ArgumentException("Integer economy setting required."); }
    }
    public static int Coins(int count, EconomySettings s) => count <= 0 ? count : Math.Max(1, (int)Math.Round(count * s.CoinLootMultiplier, MidpointRounding.AwayFromZero));
    public static double EffectValue(EnhancementEffect e, EconomySettings s) => e.Scripted ? 0 : EnhancementBalance.Family(e.Code) switch
    {
        "Damage" or "Absorb health" => EnhancementBalance.Total(e) * s.EnchantmentDamageValue,
        "Recovery" => EnhancementBalance.Total(e) * 3,
        "Bonus" or "Permanent stat damage" => e.Magnitude * s.EnchantmentBonusValue,
        "Shield" or "Resistance" => e.Magnitude * s.EnchantmentDefenseValue,
        "Reflection / absorption" or "Chameleon" => e.Magnitude * 25,
        "Resource" => e.Magnitude * 2,
        "Weakness" => e.Magnitude,
        "Control" => Math.Max(1, e.Duration) * 45,
        _ => e.Code switch { "STRP" => 70, "FTHR" or "BRDN" => e.Magnitude * 2, "TURN" or "CALM" or "FRNZ" or "RALY" or "DSPL" => e.Magnitude * 2, "DTCT" => e.Magnitude, "INVI" => 60, "NEYE" or "WABR" or "WAWA" => 40, _ => 15 }
    };
    public static EconomyPrice Price(EconomyItem item, EconomySettings s)
    {
        s.ItemOverrides.TryGetValue(item.Key, out var manual);
        if (item.Kind == "Currency" || manual?.Preserve == true || item.Preserve && manual?.Value is null) return new(item.Key, item.Value, item.Value, 0, 1, true, manual?.Reason ?? item.Reason);
        if (manual?.Value is { } exact) return new(item.Key, exact, exact, 0, 1, false, manual.Reason);
        double equipment = item.Value; var effect = 0d; var reason = "Existing value retained with category adjustment.";
        var material = s.Materials.GetValueOrDefault(item.Material, 1);
        if (item.Kind == "Weapon" && s.Weapons.TryGetValue(item.Class, out var basePrice)) { equipment = basePrice * material; reason = "Weapon class manufacturing baseline × material scarcity."; }
        else if (item.Kind == "Weapon" && item.Class == "Staff") { equipment = 120 + item.Tier * 25; reason = "Staff construction plus repeat-use effect power."; }
        else if (item.Kind is "Armor" or "Shield" && item.Armor > 0) { equipment = item.Slots.Split(',', StringSplitOptions.TrimEntries).Sum(slot => s.ArmorSlots.GetValueOrDefault(slot)) * material; reason = "Slot coverage × material scarcity."; }
        else if (item.Kind is "Clothing" or "Jewelry") { equipment = item.Enchanted ? Math.Clamp(item.Value, item.Kind == "Jewelry" ? 20 : 10, item.Kind == "Jewelry" ? 150 : 80) : item.Value; reason = "Garment/jewelry baseline plus bounded magic and prestige."; }
        else if (item.Kind == "Soul gem") { equipment = s.EmptySoulGems[item.SoulCapacity]; if (item.Soul > 0) equipment = Math.Max(equipment, s.FilledSoulGems[item.Soul]); reason = "Soul capacity and contained soul; portable recharge/enchanting utility."; }
        else if (item.Kind == "Apparatus") { equipment = new double[] { 30, 80, 220, 500, 1000 }[Math.Clamp(item.Tier, 0, 4)]; reason = "Apparatus rank and production utility."; }
        else if (item.Kind == "Sigil stone") { equipment = 100 + item.Effects.Sum(e => EffectValue(e, s)) * .5; reason = "Transferable enchantment utility and discovery scarcity."; }
        else if (item.Kind == "Ammo" && item.Material != "Unknown") { equipment = s.AmmoBaseValue * material; reason = "Arrow material and single-use enchantment; no reusable weapon premium."; }
        else if (item.Kind == "Scroll") { equipment = 8; reason = "Single-use effect utility; no repeat-use weapon premium."; }
        else if (item.Kind == "Book" && item.Class == "Skill book") { equipment = Math.Max(60, item.Value); reason = "Skill-book utility premium."; }
        if (item.Enchanted && item.Kind is not "Potion" and not "Ingredient") { effect = item.Effects.Sum(e => EffectValue(e, s)); effect *= item.Kind == "Scroll" ? s.ScrollEffectMultiplier : item.Kind == "Ammo" ? s.AmmoEffectMultiplier : Math.Clamp(item.ChargedHits / 35d, 1, 1.25); }
        var prestige = manual?.Prestige ?? (item.Unique ? s.Prestige[Math.Clamp(item.Tier, 0, 10)] : 1);
        var value = (uint)Math.Clamp(Math.Round((equipment + effect) * prestige * s.Categories.GetValueOrDefault(item.Kind, 1), MidpointRounding.AwayFromZero), item.Value == 0 && !item.Enchanted ? 0 : 1, 100000);
        return new(item.Key, value, equipment, effect, prestige, false, reason + (item.Unique ? " Lore-tier prestige premium; not a combat multiplier." : ""));
    }
}
