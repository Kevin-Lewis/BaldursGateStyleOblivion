using System.Text.Json;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Core;
using BaldursGateStyleOblivion.Discovery;
using BaldursGateStyleOblivion.Combat;
using BaldursGateStyleOblivion.Enhancements;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;
namespace BaldursGateStyleOblivion.Economy;

internal static class EconomyModule
{
    internal static string PathFor(PatcherRun run) => EnhancementModule.PathFor(run, "economy", run.Settings.EconomyConfigurationFile);
    private static EconomyEntry[] Inventory(IMajorRecordGetter record, bool scaleCoins) => record switch
    {
        INpcGetter n => n.Items.Select(i => new EconomyEntry(i.Item.FormKey.ToString(), i.Count ?? 1, ScaleCoins: scaleCoins)).ToArray(),
        ICreatureGetter c => c.Items.Select(i => new EconomyEntry(i.Item.FormKey.ToString(), i.Count ?? 1, ScaleCoins: scaleCoins)).Concat(c.DeathItem.IsNull ? [] : new[] { new EconomyEntry(c.DeathItem.FormKey.ToString(), 1) }).ToArray(),
        IContainerGetter c => c.Items.Select(i => new EconomyEntry(i.Item.FormKey.ToString(), (int)i.Count, ScaleCoins: scaleCoins)).ToArray(),
        _ => []
    };
    private static string[] Buys(INpcGetter npc)
    {
        var services = npc.AIData?.BuySellServices.ToString() ?? ""; var kinds = new List<string>();
        foreach (var p in new[] { ("Weapons", "Weapon"), ("Armor", "Armor"), ("Clothing", "Clothing"), ("Books", "Book"), ("Ingredients", "Ingredient"), ("Potions", "Potion"), ("Apparatus", "Apparatus"), ("Miscellaneous", "Valuable") }) if (services.Contains(p.Item1)) kinds.Add(p.Item2);
        if (kinds.Contains("Armor")) kinds.Add("Shield"); if (kinds.Contains("Clothing")) kinds.Add("Jewelry"); if (kinds.Contains("Weapon")) kinds.Add("Ammo"); if (kinds.Contains("Book")) kinds.Add("Scroll"); if (kinds.Contains("Valuable")) kinds.AddRange(["Clutter", "Soul gem"]); if (services.Contains("MagicItems")) kinds.Add("Enchanted"); return kinds.ToArray();
    }
    internal static EconomyItem? Read(IMajorRecordGetter record, Dictionary<FormKey, IMajorRecordGetter> records, HashSet<FormKey> protectedKeys, EnchantmentSettings enchant, IReadOnlyDictionary<string, IMagicEffectGetter> definitions)
    {
        var key = record.FormKey.ToString(); enchant.Artifacts.TryGetValue(key, out var rule); var physical = PhysicalCombatModule.ReadItem(record, protectedKeys);
        string Name() => record switch { IWeaponGetter r => r.Name?.ToString(), IArmorGetter r => r.Name?.ToString(), IClothingGetter r => r.Name?.ToString(), IPotionGetter r => r.Name?.ToString(), IIngredientGetter r => r.Name?.ToString(), IAlchemicalApparatusGetter r => r.Name?.ToString(), ISoulGemGetter r => r.Name?.ToString(), IBookGetter r => r.Name?.ToString(), IMiscellaneousGetter r => r.Name?.ToString(), IAmmunitionGetter r => r.Name?.ToString(), ISigilStoneGetter r => r.Name?.ToString(), _ => null } ?? record.EditorID ?? key;
        string kind, material = rule?.Material ?? physical?.Material ?? PhysicalCombatModule.Material(record.EditorID), cls = rule?.Class ?? physical?.Class ?? "", slots = physical?.Slots ?? ""; uint value; double weight; var capacity = 0; var soul = 0; EnhancementEffect[] effects = [];
        FormKey link = record switch { IWeaponGetter r => r.Enchantment.FormKey, IArmorGetter r => r.Enchantment.FormKey, IClothingGetter r => r.Enchantment.FormKey, IBookGetter r => r.Enchantment.FormKey, IAmmunitionGetter r => r.Enchantment.FormKey, _ => FormKey.Null };
        var magic = records.GetValueOrDefault(link) as IEnchantmentGetter; if (magic is not null) effects = EnhancementModule.Read(magic.Effects, definitions);
        var scripted = record switch { IWeaponGetter r => !r.Script.IsNull, IArmorGetter r => !r.Script.IsNull, IClothingGetter r => !r.Script.IsNull, IPotionGetter r => !r.Script.IsNull, IIngredientGetter r => !r.Script.IsNull, IAlchemicalApparatusGetter r => !r.Script.IsNull, ISoulGemGetter r => !r.Script.IsNull, IBookGetter r => !r.Script.IsNull, IMiscellaneousGetter r => !r.Script.IsNull, ISigilStoneGetter r => !r.Script.IsNull, _ => false };
        var quest = (record.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0;
        var tier = rule?.Tier ?? EnhancementModule.Tier(record.EditorID, material);
        switch (record)
        {
            case IWeaponGetter r when r.Data is not null: kind = "Weapon"; value = r.Data.Value; weight = r.Data.Weight; if (r.Data.Type.ToString() == "Staff") { cls = "Staff"; kind = "Weapon"; } break;
            case IArmorGetter r when r.Data is not null: kind = physical!.Kind; value = r.Data.Value; weight = r.Data.Weight; break;
            case IClothingGetter r when r.Data is not null: slots = r.ClothingFlags?.BipedFlags.ToString() ?? ""; kind = slots.Contains("Ring") || slots.Contains("Amulet") ? "Jewelry" : "Clothing"; value = r.Data.Value; weight = r.Data.Weight; break;
            case IPotionGetter r when r.Data is not null: kind = "Potion"; value = r.Data.Value; weight = r.Weight ?? 0; effects = EnhancementModule.Read(r.Effects, definitions); cls = effects.Length > 0 && effects.All(e => e.Hostile) ? "Poison" : "Potion"; break;
            case IIngredientGetter r when r.Data is not null: kind = "Ingredient"; value = r.Data.Value; weight = r.Weight ?? 0; effects = EnhancementModule.Read(r.Effects, definitions); break;
            case IAlchemicalApparatusGetter r when r.Data is not null: kind = "Apparatus"; value = r.Data.Value; weight = r.Data.Weight; tier = EnhancementModule.Rank(r.EditorID); cls = r.Data.Type.ToString(); break;
            case ISigilStoneGetter r when r.Data is not null: kind = "Sigil stone"; value = r.Data.Value; weight = r.Data.Weight; effects = EnhancementModule.Read(r.Effects, definitions); break;
            case ISoulGemGetter r when r.Data is not null: kind = "Soul gem"; value = r.Data.Value; weight = r.Data.Weight; capacity = Math.Clamp(Convert.ToInt32(r.MaximumCapacity), 0, 5); soul = Math.Clamp(Convert.ToInt32(r.ContainedSoul), 0, 5); break;
            case IBookGetter r when r.Data is not null: kind = r.Data.Flags.ToString().Contains("Scroll") ? "Scroll" : "Book"; value = (uint)Math.Max(0, r.Data.Value); weight = r.Data.Weight; if (r.Data.Teaches is not null) cls = "Skill book"; break;
            case IMiscellaneousGetter r when r.Data is not null: kind = record.FormKey == FormKey.Factory("00000F:Oblivion.esm") ? "Currency" : r.Data.Value >= 20 ? "Valuable" : "Clutter"; value = (uint)Math.Max(0, r.Data.Value); weight = r.Data.Weight; break;
            case IAmmunitionGetter r when r.Data is not null: kind = "Ammo"; value = r.Data.Value; weight = r.Data.Weight; break;
            default: return null;
        }
        var flags=record switch{IArmorGetter r=>r.ClothingFlags?.GeneralFlags,IClothingGetter r=>r.ClothingFlags?.GeneralFlags,_=>null};
        var lootable=flags is null||((int)flags.Value&((int)EquipmentFlag.NonPlayable>>16))==0;
        var preserve = !lootable||rule?.Preserve == true || protectedKeys.Contains(record.FormKey) && rule is null || (scripted || quest) && rule is null || record.EditorID?.StartsWith("Test", StringComparison.OrdinalIgnoreCase) == true || value == 0;
        if (kind == "Weapon" && cls != "Staff" && (!new EconomySettings().Weapons.ContainsKey(cls) || !new EconomySettings().Materials.ContainsKey(material))) preserve = true;
        if (kind is "Armor" or "Shield" && material == "Unknown") preserve = true;
        var hits = 0; if (record is IWeaponGetter charged && magic?.Data?.EnchantCost > 0) hits = (charged.EnchantmentPoints ?? 0) / (int)magic.Data.EnchantCost;
        return new(key, Name(), record.EditorID, kind, material, cls, slots, tier, value, weight, physical?.Damage ?? 0, physical?.Armor ?? 0, physical?.Durability ?? 0, magic is not null, protectedKeys.Contains(record.FormKey) || rule is not null, preserve, preserve ? "Quest/script/unclassified/zero-value exception; original price retained." : "", effects, hits, capacity, soul,lootable);
    }
    private static void SetValue(IMajorRecord record, uint value)
    {
        switch (record) { case Weapon r: r.Data!.Value = value; break; case Armor r: r.Data!.Value = value; break; case Clothing r: r.Data!.Value = value; break; case Potion r: r.Data!.Value = value; r.Data.Flags |= IngredientFlag.ManualValue; break; case Ingredient r: r.Data!.Value = value; r.Data.Flags |= IngredientFlag.ManualValue; break; case AlchemicalApparatus r: r.Data!.Value = value; break; case SigilStone r: r.Data!.Value = value; break; case SoulGem r: r.Data!.Value = value; break; case Book r: r.Data!.Value = value; break; case Miscellaneous r: r.Data!.Value = (int)value; break; case Ammunition r: r.Data!.Value = value; break; }
    }
    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run)
    {
        var path = PathFor(run); var settings = EconomyBalance.Load(path); var write = settings.Enabled && !run.Settings.ReportOnly; var changes = new Dictionary<FormKey, string[]>();
        var records = new Dictionary<FormKey, IMajorRecordGetter>(); foreach (var l in state.LoadOrder.PriorityOrder.Where(l => l.Enabled && l.Mod is not null && run.IsInputPlugin(l.ModKey))) foreach (var r in l.Mod!.EnumerateMajorRecords()) records.TryAdd(r.FormKey, r); foreach (var r in state.PatchMod.EnumerateMajorRecords()) records[r.FormKey] = r;
        var reward = RewardConfiguration.Load(RewardRecords.PathFor(run)); var protectedKeys = RewardRecords.Protected(reward); var enchant = EnhancementConfiguration.Load<EnchantmentSettings>(EnhancementModule.PathFor(run, "enchantments", run.Settings.EnchantmentConfigurationFile));
        var definitions = records.Values.OfType<IMagicEffectGetter>().Where(e => e.EditorID is not null).ToDictionary(e => e.EditorID!);
        var items = records.Values.Where(r => !r.IsDeleted && (run.Includes(r.FormKey.ModKey) || r.FormKey.ModKey == state.PatchMod.ModKey)).Select(r => Read(r, records, protectedKeys, enchant, definitions)).OfType<EconomyItem>().OrderBy(r => r.Key).ToArray(); var originalValues = items.ToDictionary(i => i.Key, i => i.Value); var prices = items.Select(i => settings.Enabled ? EconomyBalance.Price(i, settings) : new EconomyPrice(i.Key, i.Value, i.Value, 0, 1, true, "Economy disabled.")).ToArray();
        foreach (var p in prices.Where(p => !p.Preserved && p.Value != originalValues[p.Key])) if (write) { var key = FormKey.Factory(p.Key); var copy = records[key].DeepCopy(); SetValue(copy, p.Value); switch (copy) { case Weapon r: state.PatchMod.Weapons.GetOrAddAsOverride(r).DeepCopyIn(r); break; case Armor r: state.PatchMod.Armors.GetOrAddAsOverride(r).DeepCopyIn(r); break; case Clothing r: state.PatchMod.Clothes.GetOrAddAsOverride(r).DeepCopyIn(r); break; case Potion r: state.PatchMod.Potions.GetOrAddAsOverride(r).DeepCopyIn(r); break; case Ingredient r: state.PatchMod.Ingredients.GetOrAddAsOverride(r).DeepCopyIn(r); break; case AlchemicalApparatus r: state.PatchMod.GetTopLevelGroup<AlchemicalApparatus>().GetOrAddAsOverride(r).DeepCopyIn(r); break; case SigilStone r: state.PatchMod.SigilStones.GetOrAddAsOverride(r).DeepCopyIn(r); break; case SoulGem r: state.PatchMod.SoulGems.GetOrAddAsOverride(r).DeepCopyIn(r); break; case Book r: state.PatchMod.Books.GetOrAddAsOverride(r).DeepCopyIn(r); break; case Miscellaneous r: state.PatchMod.GetTopLevelGroup<Miscellaneous>().GetOrAddAsOverride(r).DeepCopyIn(r); break; case Ammunition r: state.PatchMod.Ammunitions.GetOrAddAsOverride(r).DeepCopyIn(r); break; } changes[key] = ["Data.Value"]; }
        var itemMap = items.ToDictionary(i => i.Key); var pools = records.Values.OfType<ILeveledItemGetter>().Where(r => !r.IsDeleted).Select(r => new EconomyPool(r.FormKey.ToString(), r.ChanceNone?.Value ?? 0, r.Flags?.HasFlag(LeveledFlag.UseAll) == true, r.Flags?.HasFlag(LeveledFlag.CalculateForEachItemInCount) == true, r.Entries.Select(e => new EconomyEntry(e.Reference.FormKey.ToString(), e.Count ?? 1, e.Level, r.EditorID?.StartsWith("BGSO_Loot", StringComparison.Ordinal) == true)).ToArray())).ToArray();
        var sources = new List<EconomySource>(); if (File.Exists(run.ReportPath(".world-loot.json")))
        {
            using var d = JsonDocument.Parse(File.ReadAllText(run.ReportPath(".world-loot.json"))); foreach (var row in d.RootElement.GetProperty("Records").EnumerateArray())
            {
                if (row.GetProperty("Plans").EnumerateArray().Any(p => p.GetProperty("Reason").GetString() == "Helper/player-storage/reward container retained.")) continue;
                var key = FormKey.Factory(row.GetProperty("FormKey").GetString()!); var source = records.GetValueOrDefault(key); var kind = row.GetProperty("Kind").GetString()!; var category = row.GetProperty("Category").GetString()!;
                if (category is "Household" or "Merchant" or "Special" || row.GetProperty("Tier").ValueKind != JsonValueKind.Number || Regex.IsMatch(source?.EditorID ?? "", "^Test|(?<!NO)SUMMON", RegexOptions.IgnoreCase)) continue;
                if (source is IPlacedObjectGetter placed) source = records.GetValueOrDefault(placed.Base.FormKey);
                if (source is null) continue; var entries = Inventory(source, source.EditorID?.StartsWith("BGSO_Loot", StringComparison.Ordinal) == true); if (entries.Length == 0) continue;
                sources.Add(new(key.ToString(), row.GetProperty("Name").GetString() ?? source.EditorID ?? key.ToString(), row.GetProperty("Tier").GetInt32(), category, kind, entries));
            }
        }
        var coins = new List<object>(); foreach (var pool in pools) for (var index = 0; index < pool.Entries.Length; index++)
        {
            var entry = pool.Entries[index]; if (!entry.ScaleCoins || itemMap.GetValueOrDefault(entry.Key)?.Kind != "Currency") continue;
            var count = settings.Enabled ? Math.Min(short.MaxValue, EconomyBalance.Coins(entry.Count, settings)) : entry.Count; coins.Add(new { Owner = pool.Key, Index = index, Before = entry.Count, After = count });
            if (write && count != entry.Count) { var list = state.PatchMod.LeveledItems.GetOrAddAsOverride((ILeveledItemGetter)records[FormKey.Factory(pool.Key)]); list.Entries[index].Count = (short)count; changes[list.FormKey] = ["Entries.Count"]; }
        }
        var currency = FormKey.Factory("00000F:Oblivion.esm");
        foreach (var container in records.Values.OfType<IContainerGetter>().Where(c => c.EditorID?.StartsWith("BGSO_Loot", StringComparison.Ordinal) == true).ToArray())
        {
            var entries = container.Items.Select((item, index) => (Key: item.Item.FormKey, Count: item.Count, Index: index)).Where(e => e.Key == currency).ToArray();
            foreach (var entry in entries)
            {
                var count = settings.Enabled ? EconomyBalance.Coins((int)entry.Count, settings) : (int)entry.Count; coins.Add(new { Owner = container.FormKey.ToString(), entry.Index, Before = entry.Count, After = count });
                if (!write || count == entry.Count) continue;
                var copy = state.PatchMod.Containers.GetOrAddAsOverride(container); copy.Items[entry.Index].Count = (uint)count; changes[copy.FormKey] = ["Items.Count"];
            }
        }
        var merchants = new List<EconomyMerchant>(); if (File.Exists(run.ReportPath(".merchant-stock.json")))
        {
            using var d = JsonDocument.Parse(File.ReadAllText(run.ReportPath(".merchant-stock.json"))); foreach (var row in d.RootElement.GetProperty("Merchants").EnumerateArray())
            {
                var key = FormKey.Factory(row.GetProperty("FormKey").GetString()!); if (records.GetValueOrDefault(key) is not INpcGetter npc || npc.Configuration is null || npc.IsDeleted || !run.Includes(key.ModKey)) continue; var profile = row.GetProperty("Profile").GetString()!; var buys = Buys(npc); merchants.Add(new(key.ToString(), npc.Name?.ToString() ?? npc.EditorID ?? key.ToString(), profile, npc.Configuration.BarterGold, npc.Stats?.Mercantile ?? 0, buys)); var policy = settings.MerchantOverrides.GetValueOrDefault(key.ToString()) ?? settings.Merchants.GetValueOrDefault(profile); if (policy is null || !write) continue;
                var copy = state.PatchMod.Npcs.GetOrAddAsOverride(npc); copy.Configuration!.BarterGold = (ushort)policy.Gold; if (copy.Stats is not null) copy.Stats.Mercantile = (byte)Math.Max(copy.Stats.Mercantile, policy.Mercantile); changes[key] = changes.GetValueOrDefault(key, []).Concat(new[] { "Configuration.BarterGold", "Stats.Mercantile" }).ToArray();
            }
        }
        var engine = new List<object>(); foreach (var pair in settings.GameSettings) { var source = records.Values.OfType<IGameSettingGetter>().FirstOrDefault(g => g.EditorID == pair.Key); double? before = source is IGameSettingFloatGetter floating ? floating.Data : source is IGameSettingIntGetter integer ? integer.Data : null; engine.Add(new { EditorID = pair.Key, Before = before, After = pair.Value }); if (!write || before == pair.Value) continue; IGameSetting copy; if (pair.Key.StartsWith('i')) { var n = source is null ? state.PatchMod.GameSettings.AddNewInt() : (GameSettingInt)state.PatchMod.GameSettings.GetOrAddAsOverride(source); n.Data = (int)pair.Value; copy = n; } else { var f = source is null ? state.PatchMod.GameSettings.AddNewFloat() : (GameSettingFloat)state.PatchMod.GameSettings.GetOrAddAsOverride(source); f.Data = (float)pair.Value; copy = f; } copy.EditorID = pair.Key; changes[copy.FormKey] = ["Data"]; }
        var spells = new List<EconomySpell>(); var spellSettings = new Dictionary<string, double>(); if (File.Exists(run.ReportPath(".magic-analysis.json"))) { using var d = JsonDocument.Parse(File.ReadAllText(run.ReportPath(".magic-analysis.json"))); spellSettings = d.RootElement.GetProperty("GameSettings").Deserialize<Dictionary<string, double>>()!; foreach (var row in d.RootElement.GetProperty("Spells").EnumerateArray()) { var spell = row.GetProperty("Spell"); if (spell.GetProperty("Type").GetString() != "Spell" || spell.GetProperty("Test").GetBoolean()) continue; spells.Add(new(spell.GetProperty("FormKey").GetString()!, spell.GetProperty("Name").GetString() ?? "Spell", row.GetProperty("School").GetString() ?? "", row.GetProperty("BaseCost").ValueKind == JsonValueKind.Number ? row.GetProperty("BaseCost").GetDouble() : spell.GetProperty("StoredCost").GetDouble(), spell.GetProperty("ManualCost").GetBoolean())); } }
        var scriptedGold = QuestRewards.ScriptContexts(records.Values.Where(r => !r.IsDeleted)).SelectMany(context => ScriptDiscovery.Scan(context.Fields.SourceCode)
            .Where(signal => Regex.IsMatch(signal.Source, @"\bAddItem(?:NS)?\s+Gold(?:001)?\b", RegexOptions.IgnoreCase))
            .Select(signal => new { Owner = context.Owner.FormKey.ToString(), context.Owner.EditorID, context.Context, signal.Line, signal.Source, Status = "Authored script payout retained; review individually." })).ToArray();
        var catalog = new EconomyCatalog { Items = items, Pools = pools, Sources = sources.DistinctBy(s => s.Key).ToArray(), Merchants = merchants.ToArray(), Spells = spells.ToArray(), SpellSettings = spellSettings };
        run.WriteReport(".economy.json", new { Schema = 1, Applied = write, ConfigurationFile = path, Settings = settings, Catalog = catalog, Prices = prices, CoinChanges = coins, EngineSettings = engine, ScriptedGold = scriptedGold, Notes = new[] { "Inputs are winning records after equipment, magic and alchemy balance; Current means before this economy pass.", "Merchant gold is a per-transaction limit, not a depleting wallet. Merchant skill is a floor; trainer requirements retained.", "Coin scaling affects generated adventure loot only. Currency value, quest scripts, authored payouts and protected items remain intact.", "Spell purchase costs remain tied to native magicka costs, skill, Luck and bargaining; economy does not change spell power or casting costs.", "Outings sample actual static pools with counts/empty chances. Carrying, condition, bargaining, consumption and travel remain editable assumptions." } }, EconomyBalance.Options);
        run.Log($"Economy: {items.Length} item prices, {merchants.Count} merchant profiles, {coins.Count} generated gold entries; {changes.Count} writes."); return changes;
    }
}

