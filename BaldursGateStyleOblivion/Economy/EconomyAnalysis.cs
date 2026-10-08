namespace BaldursGateStyleOblivion.Economy;

public sealed class EconomyScenario
{
    public int Tier { get; set; } = 2;
    public double Level { get; set; } = 5;
    public string Class { get; set; } = "Warrior";
    public string Category { get; set; } = "Bandit";
    public string Outing { get; set; } = "Short outing";
    public string Merchant { get; set; } = "";
    public string Target { get; set; } = "Steel kit";
    public double? CarryWeight { get; set; }
    public double? SellFactor { get; set; }
    public double? BuyFactor { get; set; }
    public double Condition { get; set; } = .9;
    public double? RepairWear { get; set; }
    public int? HealingPotions { get; set; }
    public int? Poisons { get; set; }
    public double? ChargeSpent { get; set; }
    public double QuestGold { get; set; }
    public double OtherExpenses { get; set; }
}
public sealed record EconomySale(string Name,int Count,double Proceeds,double Weight);
public sealed record EconomyCost(string Name,double Gold);
public sealed record EconomyTrip(double Gross, double Coins, double Expenses, double Net, double LootWeight, double UnsoldValue, int UnknownBranches, double CapLoss);
public sealed record EconomyResult(double MedianProfit, double AverageProfit, double LowProfit, double WindfallProfit, double MedianGross, double MedianExpenses, double MedianCoins, double MedianWeight, double MedianUnsold, double AverageCapLoss, int UnknownBranches, double PurchasePrice, double? OutingsToPurchase, EconomyTrip[] Samples, string[] Sources, string[] Notes,EconomySale[] Cargo,EconomyCost[] Costs);
public static class EconomyAnalysis
{
    public static EconomyResult Run(EconomyCatalog catalog, EconomySettings settings, EconomyScenario scenario, double carry, double sell, double buy, double gearValue, double targetValue, int seed = 1729, int samples = 256, double playerMercantile = 0)
    {
        EconomyBalance.Validate(settings); if (samples is < 1 or > 10000) throw new ArgumentException("Loot sample count must be between 1 and 10000.");
        void Range(double v, double min, double max, string name) => BaldursGateStyleOblivion.Combat.CombatConfiguration.Range(v, min, max, name);
        Range(scenario.Tier, 0, 10, "Tier"); Range(scenario.Level, 1, 100, "Level"); Range(carry, 0, 1000, "Loot carrying capacity"); Range(sell, .01, 1, "Sale factor"); Range(buy, 1, 5, "Purchase factor"); Range(scenario.Condition, .01, 1.25, "Loot condition"); Range(scenario.QuestGold, 0, 100000, "Quest gold"); Range(scenario.OtherExpenses, 0, 100000, "Other expenses");
        if (!settings.Outings.TryGetValue(scenario.Outing, out var outing)) throw new ArgumentException("Choose an outing preset.");
        var items = catalog.Items.ToDictionary(i => i.Key); var prices = catalog.Items.ToDictionary(i => i.Key, i => settings.Enabled ? EconomyBalance.Price(i, settings).Value : i.Value); var pools = catalog.Pools.ToDictionary(p => p.Key);
        var merchant = catalog.Merchants.FirstOrDefault(m => m.Key == scenario.Merchant) ?? catalog.Merchants.FirstOrDefault(m => m.Profile == "General Store" && m.Buys.Length >= 5) ?? throw new ArgumentException("No selling merchant in catalogue.");
        var policy = settings.MerchantOverrides.GetValueOrDefault(merchant.Key) ?? settings.Merchants.GetValueOrDefault(merchant.Profile); var gold = settings.Enabled ? policy?.Gold ?? merchant.Gold : merchant.Gold;
        var merchantSkill = settings.Enabled ? Math.Max(merchant.Mercantile, policy?.Mercantile ?? 0) : merchant.Mercantile; if (merchantSkill >= 100) gold += (int)settings.GameSettings.GetValueOrDefault("iPerkExtraBarterGoldMaster", 500);
        EconomySource[] Group(string kind, string category)
        {
            var candidates = catalog.Sources.Where(s => s.Kind == kind && s.Category == category).ToArray(); if (candidates.Length == 0) return [];
            var tier = candidates.Select(s => s.Tier).Distinct().OrderBy(t => Math.Abs(t - scenario.Tier)).ThenBy(t => t).First(); return candidates.Where(s => s.Tier == tier).ToArray();
        }
        var enemies = Group("ActorLoot", scenario.Category); var containers = Group("Container", scenario.Category); var bosses = Group("Container", "Boss");
        var used = new HashSet<string>(); var random = new Random(seed); var result = new List<EconomyTrip>();var manifests=new List<EconomySale[]>();
        var healing = catalog.Items.Where(i => i.Kind == "Potion" && !i.Preserve && i.Effects.Any(e => e.Code == "REHE") && i.Effects.All(e => !e.Hostile)).OrderBy(i => Math.Abs(i.Effects.Where(e => e.Code == "REHE").Sum(e => e.Magnitude * Math.Max(1, e.Duration)) - (40 + scenario.Tier * 8))).ThenBy(i => i.Value).FirstOrDefault();
        var poison = catalog.Items.Where(i => i.Kind == "Potion" && !i.Preserve && i.Class == "Poison").OrderBy(i => Math.Abs(i.Effects.Sum(e => e.Magnitude * Math.Max(1, e.Duration)) - (15 + scenario.Tier * 5))).FirstOrDefault();
        var ingredients = catalog.Items.Where(i => i.Kind == "Ingredient" && !i.Preserve && i.Value < 30).ToArray();
        var wear = scenario.RepairWear ?? outing.RepairWear; var healCount = scenario.HealingPotions ?? outing.HealingPotions; var poisonCount = scenario.Poisons ?? outing.Poisons; var charge = scenario.ChargeSpent ?? outing.ChargeSpent;
        Range(wear, 0, 1, "Repair wear"); Range(healCount, 0, 30, "Healing potions"); Range(poisonCount, 0, 30, "Poisons"); Range(charge, 0, 10000, "Charge use");
        var expenses = (healing is null ? 0 : prices[healing.Key] * healCount * buy) + (poison is null ? 0 : prices[poison.Key] * poisonCount * buy) + gearValue * wear * settings.GameSettings.GetValueOrDefault("fRepairCostMult", .9) + charge * settings.GameSettings.GetValueOrDefault("fRechargeGoldMult", 1) + scenario.OtherExpenses;
        for (var sample = 0; sample < samples; sample++)
        {
            var found = new Dictionary<string, int>();var manifest=new List<EconomySale>(); var unknown = 0; var visits = 0;
            void Roll(string key, int count, bool scaleCoins, HashSet<string> path)
            {
                if (count <= 0) return; if (++visits > 20000) { unknown++; return; }
                if (items.TryGetValue(key, out var item)) { if (item.Unique||!item.Lootable) return; var amount = item.Kind == "Currency" && scaleCoins && settings.Enabled ? EconomyBalance.Coins(count, settings) : count; found[key] = Math.Min(100000, found.GetValueOrDefault(key) + amount); return; }
                if (!pools.TryGetValue(key, out var pool) || !path.Add(key) || path.Count > 40) { unknown++; return; }
                if (pool.Entries.Any(e => e.Level > 1)) { unknown++; path.Remove(key); return; }
                var repetitions = pool.Each ? Math.Min(count, 1000) : 1; var multiplier = pool.Each ? 1 : count;
                for (var n = 0; n < repetitions; n++)
                {
                    if (random.NextDouble() < pool.ChanceNone || pool.Entries.Length == 0) continue;
                    var entries = pool.UseAll ? pool.Entries : new[] { pool.Entries[random.Next(pool.Entries.Length)] };
                    foreach (var e in entries) Roll(e.Key, (int)Math.Min(100000, (long)e.Count * multiplier), e.ScaleCoins, path);
                }
                path.Remove(key);
            }
            void Draw(EconomySource[] sources, int count) { if (sources.Length == 0) { unknown += count; return; } for (var n = 0; n < count; n++) { var source = sources[random.Next(sources.Length)]; used.Add(source.Name + " (tier " + source.Tier + ")"); foreach (var e in source.Entries) Roll(e.Key, e.Count, e.ScaleCoins, []); } }
            Draw(enemies, outing.Enemies); Draw(containers, outing.Containers); Draw(bosses, outing.BossContainers);
            if (ingredients.Length > 0) for (var n = 0; n < outing.Ingredients; n++) { var key = ingredients[random.Next(ingredients.Length)].Key; found[key] = found.GetValueOrDefault(key) + 1; }
            var coins = found.Where(p => items[p.Key].Kind == "Currency").Sum(p => (double)p.Value) + scenario.QuestGold; var gross = 0d; var weight = 0d; var unsold = 0d; var capLoss = 0d;
            double Sale(EconomyItem i) => prices[i.Key] * sell * (i.Kind is "Weapon" or "Armor" or "Shield" ? Math.Min(1, scenario.Condition) : 1);
            var cargo = found.Where(p => items[p.Key].Kind != "Currency").OrderByDescending(p => Sale(items[p.Key]) / Math.Max(.01, items[p.Key].Weight));
            foreach (var p in cargo)
            {
                var item = items[p.Key]; var offer = Sale(item); var units = item.Weight <= 0 ? p.Value : Math.Min(p.Value, (int)Math.Floor(Math.Max(0, carry - weight) / item.Weight)); weight += units * item.Weight;
                var accepted = playerMercantile >= 50 && merchant.Buys.Length > 0 || merchant.Buys.Contains(item.Kind) || item.Enchanted && merchant.Buys.Contains("Enchanted");
                if (!accepted) { unsold += units * offer; continue; }
                gross += units * Math.Min(offer, gold);if(units>0)manifest.Add(new(item.Name,units,units*Math.Min(offer,gold),units*item.Weight)); capLoss += units * Math.Max(0, offer - gold);
            }
            manifests.Add(manifest.OrderByDescending(s=>s.Proceeds).Take(10).ToArray());
            result.Add(new(gross + coins, coins, expenses, gross + coins - expenses, weight, unsold, unknown, capLoss));
        }
        double Quantile(Func<EconomyTrip, double> selector, double q) { var sorted = result.Select(selector).Order().ToArray(); return sorted[(int)Math.Round((sorted.Length - 1) * q)]; }
        var median = Quantile(r => r.Net, .5); var purchase = targetValue * buy;
        return new(median, result.Average(r => r.Net), Quantile(r => r.Net, .1), Quantile(r => r.Net, .9), Quantile(r => r.Gross, .5), Quantile(r => r.Expenses, .5), Quantile(r => r.Coins, .5), Quantile(r => r.LootWeight, .5), Quantile(r => r.UnsoldValue, .5), result.Average(r => r.CapLoss), result.Max(r => r.UnknownBranches), purchase, median > 0 ? purchase / median : null, result.ToArray(), used.Order().Take(12).ToArray(),
            [$"{samples} reproducible loot samples from the last built static inventories; source tiers describe world danger, not player-level scaling.", "The nearest available source tier is used when an exact tier/category is absent. Unique one-time equipment is excluded from repeatable earnings.", "Buy/sell factors are editable estimates, not an exact native haggle formula. Condition sale adjustment, repair disposition and consumption require calibration.", "Merchant gold caps each item; proceeds assume an acceptable offer can be negotiated within that cap. Sold stacks can be split; gold does not deplete.", "Carrying selects high sale-value/weight cargo. Unsold goods reflect the selected merchant's buying services. Dynamic/unresolved branches are excluded and counted.", "Outing results assume success. Quest gold, travel time, missed loot and extra expenses are explicit assumptions, not predicted quest payouts."],manifests[result.FindIndex(r=>r.Net==median)],[new("Healing potions",healing is null?0:prices[healing.Key]*healCount*buy),new("Poisons",poison is null?0:prices[poison.Key]*poisonCount*buy),new("Paid repairs",gearValue*wear*settings.GameSettings.GetValueOrDefault("fRepairCostMult",.9)),new("Paid recharge",charge*settings.GameSettings.GetValueOrDefault("fRechargeGoldMult",1)),new("Other",scenario.OtherExpenses)]);
    }
}
