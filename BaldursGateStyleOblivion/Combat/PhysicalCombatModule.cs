using System.Reflection;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Combat.Tests")]

namespace BaldursGateStyleOblivion.Combat;

internal static class PhysicalCombatModule
{
    internal static string Material(string? editorID)
    {
        var match = Regex.Match(editorID ?? "", "Daedric|Ebony|Glass|Orcish|Elven|Dwarven|Silver|Steel|Iron|Mithril|Chainmail|Leather|Fur|Amber|Madness", RegexOptions.IgnoreCase);
        return match.Success ? char.ToUpperInvariant(match.Value[0]) + match.Value[1..].ToLowerInvariant() : "Unknown";
    }
    internal static string WeaponClass(IWeaponGetter weapon)
    {
        var match = Regex.Match(weapon.EditorID ?? "", "Dagger|Shortsword|Longsword|Claymore|Waraxe|Battleaxe|Mace|Warhammer", RegexOptions.IgnoreCase);
        return match.Success ? char.ToUpperInvariant(match.Value[0]) + match.Value[1..].ToLowerInvariant() : weapon.Data?.Type.ToString() ?? "Unknown";
    }
    internal static PhysicalItem? ReadItem(IMajorRecordGetter record, HashSet<FormKey> artifacts)
    {
        string? reason = artifacts.Contains(record.FormKey) ? "Protected unique or artifact" : null;
        switch (record)
        {
            case IWeaponGetter weapon when weapon.Data is not null:
                if (!weapon.Enchantment.IsNull) reason ??= "Enchanted item: enchantment balance pending";
                if (!weapon.Script.IsNull || (weapon.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0) reason = "Scripted or quest item";
                var data = weapon.Data;
                return new(weapon.FormKey.ToString(), weapon.EditorID, weapon.Name?.ToString() ?? weapon.EditorID ?? weapon.FormKey.ToString(),
                    "Weapon", WeaponClass(weapon), Material(weapon.EditorID), data.Damage, data.Speed, data.Reach, data.Weight, data.Health, 0,
                    data.Value, "", false, !weapon.Enchantment.IsNull, reason is not null, reason, data.Type.ToString(), data.Flags.ToString().Contains("IgnoresNormalWeaponResistance", StringComparison.Ordinal));
            case IArmorGetter armor when armor.Data is not null:
                if (!armor.Enchantment.IsNull) reason ??= "Enchanted item: enchantment balance pending";
                if (!armor.Script.IsNull || (armor.MajorRecordFlagsRaw & (int)OblivionMajorRecord.OblivionMajorRecordFlag.QuestItemPersistentReference) != 0) reason = "Scripted or quest item";
                var shield = armor.ClothingFlags?.BipedFlags.HasFlag(BipedFlag.Shield) == true;
                var heavy = ArmorFlags.IsHeavy(armor);
                return new(armor.FormKey.ToString(), armor.EditorID, armor.Name?.ToString() ?? armor.EditorID ?? armor.FormKey.ToString(),
                    shield ? "Shield" : "Armor", shield ? heavy ? "Heavy Shield" : "Light Shield" : heavy ? "Heavy Armor" : "Light Armor",
                    Material(armor.EditorID), 0, 0, 0, armor.Data.Weight, armor.Data.Health, armor.Data.ArmorValue, armor.Data.Value,
                    armor.ClothingFlags?.BipedFlags.ToString() ?? "", heavy, !armor.Enchantment.IsNull, reason is not null, reason);
            default: return null;
        }
    }
    internal static string[] Changed(PhysicalItem source, PhysicalItem target)
    {
        var fields = new List<string>();
        if (source.Damage != target.Damage) fields.Add("Data.Damage");
        if (source.Speed != target.Speed) fields.Add("Data.Speed");
        if (source.Reach != target.Reach) fields.Add("Data.Reach");
        if (source.Weight != target.Weight) fields.Add("Data.Weight");
        if (source.Durability != target.Durability) fields.Add("Data.Health");
        if (Math.Round(source.Armor*100) != Math.Round(target.Armor*100)) fields.Add("Data.ArmorValue");
        return fields.ToArray();
    }
    internal static void Apply(Weapon target, PhysicalItem proposal)
    {
        target.Data!.Damage = (ushort)proposal.Damage; target.Data.Speed = (float)proposal.Speed;
        target.Data.Reach = (float)proposal.Reach; target.Data.Weight = (float)proposal.Weight; target.Data.Health = proposal.Durability;
    }
    internal static void Apply(Armor target, PhysicalItem proposal)
    { target.Data!.ArmorValue = (float)proposal.Armor; target.Data.Weight = (float)proposal.Weight; target.Data.Health = proposal.Durability; }

    private static Dictionary<string, object?> Scalars(object? value) => value?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum))
        .ToDictionary(p => p.Name, p => p.PropertyType.IsEnum ? p.GetValue(value)?.ToString() : p.GetValue(value)) ?? new();

    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, PatcherRun run)
    {
        var path = string.IsNullOrWhiteSpace(run.Settings.CombatConfigurationFile) ? Path.Combine(run.DataDirectory, "combat.json")
            : Path.GetFullPath(run.Settings.CombatConfigurationFile, run.DataDirectory);
        if (!File.Exists(path) && string.IsNullOrWhiteSpace(run.Settings.CombatConfigurationFile)) path = Path.Combine(AppContext.BaseDirectory, "combat.json");
        var settings = CombatConfiguration.Load(path);
        if (!run.Settings.EnablePhysicalCombatBalance || run.Settings.ReportOnly)
            run.Log($"Combat writes disabled: EnablePhysicalCombatBalance={run.Settings.EnablePhysicalCombatBalance}, ReportOnly={run.Settings.ReportOnly}.");
        var artifacts = RewardRecords.Protected(RewardConfiguration.Load(RewardRecords.PathFor(run)));
        var records = new Dictionary<FormKey, IMajorRecordGetter>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)))
            if (listing.Enabled && listing.Mod is not null)
                foreach (var record in listing.Mod.EnumerateMajorRecords()) records.TryAdd(record.FormKey, record);
        // Earlier modules may have fixed actors and replaced inventories; use those effective records.
        foreach (var record in state.PatchMod.EnumerateMajorRecords()) records[record.FormKey] = record;
        var items = records.Values.Where(r => !r.IsDeleted && run.Includes(r.FormKey.ModKey)).Select(r => ReadItem(r, artifacts))
            .Where(item => item is not null).Cast<PhysicalItem>().OrderBy(item => item.FormKey, StringComparer.Ordinal).ToArray();
        var changes = new Dictionary<FormKey, string[]>();
        var plans = new List<object>();
        foreach (var item in items)
        {
            var target = PhysicalBalance.Propose(item, settings); var fields = Changed(item, target);
            if (fields.Length == 0) continue;
            var key = FormKey.Factory(item.FormKey);
            plans.Add(new { item.FormKey, Before = item, After = target, Fields = fields });
            if (run.Settings.EnablePhysicalCombatBalance && !run.Settings.ReportOnly)
            {
                if (records[key] is IWeaponGetter weapon) Apply(state.PatchMod.Weapons.GetOrAddAsOverride(weapon), target);
                else if (records[key] is IArmorGetter armor) Apply(state.PatchMod.Armors.GetOrAddAsOverride(armor), target);
                changes[key] = fields;
                records[key] = target.Kind == "Weapon" ? state.PatchMod.Weapons[key] : state.PatchMod.Armors[key];
            }
        }
        var playableRaces=new Dictionary<FormKey,bool>();
        foreach(var listing in state.LoadOrder.PriorityOrder.Where(l=>l.Enabled && l.Mod is not null && run.IsInputPlugin(l.ModKey)))
            foreach(var pair in PlayableRaces.Read(Path.Combine(state.DataFolderPath.ToString(),listing.ModKey.ToString()),listing.Mod!))playableRaces.TryAdd(pair.Key,pair.Value);
        var originalGameSettings = records.Values.OfType<IGameSettingFloatGetter>().Where(r=>!r.IsDeleted && r.EditorID is not null && r.Data.HasValue).ToDictionary(r=>r.EditorID!,r=>(double)r.Data!.Value);
        foreach(var setting in records.Values.OfType<IGameSettingIntGetter>().Where(r=>!r.IsDeleted && r.EditorID is not null && r.Data.HasValue)) originalGameSettings[setting.EditorID!]=setting.Data!.Value;
        foreach(var change in GameplayCombatModule.Run(state, records, profiles, settings, run)) changes[change.Key]=change.Value;
        var actors = records.Values.Where(r => !r.IsDeleted && run.Includes(r.FormKey.ModKey) && r is INpcGetter or ICreatureGetter)
            .OrderBy(r => r.FormKey.ToString(), StringComparer.Ordinal).Select(r =>
            {
                var npc = r as INpcGetter; var creature = r as ICreatureGetter;
                var auto = npc?.Configuration?.Flags.HasFlag(Npc.NpcFlag.AutoCalcStats) == true;
                var scaledCreature = creature?.Configuration?.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset) == true;
                var inventory = npc is not null ? npc.Items.Cast<IItemEntryGetter>() : creature!.Items.Cast<IItemEntryGetter>();
                return new { FormKey = r.FormKey.ToString(), r.EditorID, Name = npc?.Name?.ToString() ?? creature?.Name?.ToString(),
                    Tier = profiles.GetValueOrDefault(r.FormKey)?.Tier?.Value,
                    Level = npc?.Configuration?.LevelOffset ?? creature?.Configuration?.LevelOffset,
                    AutoCalculated = auto || scaledCreature,
                    StatsStatus = auto || scaledCreature ? "Engine-calculated: enter observed stats for scenarios" : "Stored base stats: racial, spell and runtime bonuses need review",
                    Stats = Scalars(npc is not null ? npc.Stats : creature!.Data),
                    Inventory = inventory.Select(i => new { Item = i.Item.FormKey.ToString(), i.Count }).ToArray(),
                    CombatStyle = npc?.CombatStyle.FormKeyNullable?.ToString() ?? creature?.CombatStyle.FormKeyNullable?.ToString() };
            }).ToArray();
        var styles = records.Values.OfType<ICombatStyleGetter>().Where(r => !r.IsDeleted && run.Includes(r.FormKey.ModKey))
            .OrderBy(r => r.FormKey.ToString(), StringComparer.Ordinal)
            .Select(r => new { FormKey = r.FormKey.ToString(), r.EditorID, Data = Scalars(r.Data), Advanced = Scalars(r.Advanced) }).ToArray();
        var gameSettings = records.Values.OfType<IGameSettingFloatGetter>().Where(r => !r.IsDeleted && r.EditorID is not null && r.Data.HasValue &&
            Regex.IsMatch(r.EditorID, "^f(Damage|Armor|MaxArmor|Block|Fatigue|PowerAttack|WeaponFatigue|Perk|Difficulty|PCBaseHealth)", RegexOptions.IgnoreCase))
            .OrderBy(r => r.EditorID, StringComparer.Ordinal).ToDictionary(r => r.EditorID!, r => (double)r.Data!.Value);
        run.WriteReport(".physical-combat.json", new { Schema = 1, GeneratedUtc = DateTime.UtcNow, run.Settings.ReportOnly,
            Applied = run.Settings.EnablePhysicalCombatBalance && !run.Settings.ReportOnly, Items = items.Concat(records.Values.OfType<IWeaponGetter>().Where(r=>r.EditorID?.StartsWith("BGSOCombatTier",StringComparison.Ordinal)==true).Select(r=>ReadItem(r,artifacts)!)).ToArray(), Actors = actors, NativeStyles = styles,
            ConfigurationHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(path)))), CandidateFile = Path.GetFullPath(state.OutputPath.ToString()), CharacterCreation = CharacterCreation.Read(records,originalGameSettings,playableRaces), OriginalGameSettings = originalGameSettings, GameSettings = gameSettings, MechanicsFallbacks = CombatAnalysis.Defaults.Where(p => !gameSettings.ContainsKey(p.Key)).ToDictionary(), Plans = plans, ConfigurationFile = path,
            Notes = new[] { "Items are the baseline before this module; earlier patch modules are included when writes are enabled.",
                "Balanced actors have explicit base stats. Racial abilities, spells and runtime script bonuses may still modify them.",
                "Gameplay configuration writes equipment, explicit actor stats, native combat styles and engine settings. Benchmark tier curves remain optional analysis targets." } }, CombatConfiguration.Options);
        run.Log($"Physical combat: {items.Length} items, {actors.Length} actors, {styles.Length} native styles; {plans.Count} proposals, {changes.Count} applied.");
        return changes;
    }
}
