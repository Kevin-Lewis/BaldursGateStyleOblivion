using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Modules;

internal static class EquipmentDistributionModule
{
    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, PatcherRun run)
    {
        var path = Path.Combine(run.DataDirectory, "equipment.json");
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "equipment.json");
        if (!string.IsNullOrWhiteSpace(run.Settings.EquipmentConfigurationFile))
            path = Path.GetFullPath(run.Settings.EquipmentConfigurationFile, run.DataDirectory);
        run.Log($"Equipment configuration: {path}");
        var settings = EquipmentConfiguration.Load(path);
        var winners = new Dictionary<FormKey, (IMajorRecordGetter Record, ModKey Plugin)>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)))
            if (listing.Enabled && listing.Mod is not null)
                foreach (var record in listing.Mod.EnumerateMajorRecords()) winners.TryAdd(record.FormKey, (record, listing.ModKey));
        var records = winners.Where(pair => !pair.Value.Record.IsDeleted).ToDictionary(pair => pair.Key, pair => pair.Value.Record);
        var locations = ActorLocations(records);
        var builder = new EquipmentPoolBuilder(records, settings, state.PatchMod.ModKey, state.PatchMod.ModHeader.Stats.NextFormID, run.Includes);
        var changes = new Dictionary<FormKey, string[]>();
        var rows = new List<object>();
        var modifiedActors = new HashSet<FormKey>();
        var processedRoots = 0;
        var retainedRoots = 0;
        foreach (var actor in records.Values.Where(record => record is INpcGetter or ICreatureGetter)
                     .Where(record => run.Includes(record.FormKey.ModKey)).OrderBy(record => record.FormKey.ToString(), StringComparer.Ordinal))
        {
            var profile = profiles.GetValueOrDefault(actor.FormKey);
            var tier = profile?.Dimensions.GetValueOrDefault(nameof(ActorValues.EquipmentTier))?.Selected.Value as int? ?? profile?.Tier?.Value;
            var evidence = Evidence(actor, profile, records, locations.GetValueOrDefault(actor.FormKey, []));
            var selected = EquipmentConfiguration.Select(settings, actor.FormKey.ToString(), actor.FormKey.ModKey.ToString(), evidence, tier);
            var race = actor is INpcGetter raceActor ? records.GetValueOrDefault(raceActor.Race.FormKey)?.EditorID ?? "" : "";
            var preferredMaterial = settings.RaceMaterials.GetValueOrDefault(race, "");
            var inventory = actor is INpcGetter npc ? npc.Items.Cast<IItemEntryGetter>().ToArray() : ((ICreatureGetter)actor).Items.Cast<IItemEntryGetter>().ToArray();

            var plans = new List<object>();
            for (var index = 0; index < inventory.Length; index++)
            {
                var item = inventory[index];
                var key = item.Item.FormKey;
                if (records.GetValueOrDefault(key) is not ILeveledItemGetter list) continue;
                string reason;
                var target = key;
                try
                {
                    var guard = Guard(actor, key, records);
                    if (selected.Preserve || guard is not null) throw new InvalidDataException(guard ?? "Individual actor preservation override.");
                    var built = builder.Build(key, selected.Profile, preferredMaterial); target = built.Key; reason = built.Reason;
                    processedRoots++;
                    if (!run.Settings.ReportOnly)
                    {
                        if (actor is INpcGetter source) state.PatchMod.Npcs.GetOrAddAsOverride(source).Items[index].Item.SetTo(target);
                        else state.PatchMod.Creatures.GetOrAddAsOverride((ICreatureGetter)actor).Items[index].Item.SetTo(target);
                        modifiedActors.Add(actor.FormKey);
                        changes[actor.FormKey] = ["Items.Reference"];
                    }
                }
                catch (InvalidDataException exception) { reason = exception.Message; retainedRoots++; }
                plans.Add(new { Original = key.ToString(), list.EditorID, item.Count, Planned = target.ToString(),
                    Profile = settings.ListOverrides.GetValueOrDefault(key.ToString())?.Profile ?? selected.Profile,
                    Status = target == key ? "Preserved" : run.Settings.ReportOnly ? "Planned" : "Modified", Reason = reason });
            }
            if (plans.Count > 0) rows.Add(new
            {
                FormKey = actor.FormKey.ToString(), actor.EditorID, Name = actor is INpcGetter n ? n.Name?.ToString() : ((ICreatureGetter)actor).Name?.ToString(),
                SourcePlugin = actor.FormKey.ModKey.ToString(), WinningOverridePlugin = winners[actor.FormKey].Plugin.ToString(),
                PowerTier = profile?.Tier?.Value, EquipmentTier = tier, Profile = selected.Profile, Rule = selected.Rule,
                Race = race, PreferredMaterial = preferredMaterial, Evidence = evidence, Inventories = plans,
                FixedEquipment = inventory.Where(item => EquipmentPoolBuilder.IsEquipment(records.GetValueOrDefault(item.Item.FormKey)))
                    .Select(item => new { Reference = item.Item.FormKey.ToString(), item.Count, Reason = "Existing fixed equipment preserved." }).ToArray()
            });
        }
        if (!run.Settings.ReportOnly)
        {
            foreach (var list in builder.Patch.LeveledItems)
            {
                state.PatchMod.LeveledItems.Add(list); changes[list.FormKey] = ["NewRecord"];
            }
            state.PatchMod.ModHeader.Stats.NextFormID = builder.Patch.ModHeader.Stats.NextFormID;
        }
        var generated = builder.Patch.LeveledItems.Select(list => new
        {
            FormKey = list.FormKey.ToString(), list.EditorID, list.Flags, ChanceNone = list.ChanceNone.ToString(),
            Entries = list.Entries!.Select(entry => new { entry.Level, Reference = entry.Reference.FormKey.ToString(), entry.Count }).ToArray()
        }).ToArray();
        run.WriteReport(".equipment-distribution.json", new
        {
            run.Settings.ReportOnly, ConfigurationFile = path,
            Summary = new { Actors = rows.Count, ModifiedActors = modifiedActors.Count, ProcessedInventoryLists = processedRoots,
                PreservedInventoryLists = retainedRoots, GeneratedPools = generated.Length }, Actors = rows, GeneratedPools = generated,
            Limitations = new[] { "Fixed equipment is retained; unique items and artifacts are not automatically reassigned.",
                "Mixed loot, consumables, death items, merchant containers and quest rewards are deferred to later phases.",
                "Quality weights use available source equipment. Precious-only pools receive compatible mundane fallback items; Ebony and Daedric eligibility is controlled separately.",
                "Chance None and counts are retained. Enchanted-only source pools stay enchanted; this module does not create missing mundane variants.",
                "Existing save inventories need a fresh spawn or inventory reset to show new equipment." }
        }, EquipmentConfiguration.Options);
        run.Log($"Equipment: {modifiedActors.Count} actors modified, {processedRoots} equipment inventory lists processed, {retainedRoots} other/guarded lists retained, {generated.Length} private pools.");
        return changes;
    }

    internal static string? Guard(IMajorRecordGetter actor, FormKey inventoryRoot, IReadOnlyDictionary<FormKey, IMajorRecordGetter> records)
    {
        if (actor.FormKey == FormKey.Factory("000007:Oblivion.esm")) return "Player inventory preserved.";
        if (Regex.IsMatch(actor.EditorID ?? "", @"^Test|Dummy|Template|Marker", RegexOptions.IgnoreCase)) return "Helper/test actor preserved.";
        var key = actor is INpcGetter npc ? npc.Script.FormKeyNullable : ((ICreatureGetter)actor).Script.FormKeyNullable;
        if (key is null) return null;
        if (records.GetValueOrDefault(key.Value) is not IScriptGetter script || script.Fields?.SourceCode is not {} code)
            return "Actor script is unresolved; inventory retained.";
        code = string.Join("\n", code.Split('\n').Select(line => line.Split(';')[0]));
        if (Regex.IsMatch(code, @"\b(RemoveAllItems|AddToLeveledList|RemoveFromLeveledList)\b", RegexOptions.IgnoreCase))
            return "Actor script manages whole inventories or list contents; equipment retained.";
        var commands = Regex.Matches(code, @"\b(?:AddItem|RemoveItem|GetItemCount|EquipItem|UnequipItem)\s+(\w+)", RegexOptions.IgnoreCase);
        if (commands.Count == 0) return null;
        var referenced = script.EnumerateFormLinks().Select(link => link.FormKey).ToHashSet();
        var names = referenced.Select(reference => records.GetValueOrDefault(reference)?.EditorID).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (commands.Any(command => !names.Contains(command.Groups[1].Value)))
            return "Actor script uses a dynamic or unresolved inventory operand; equipment retained.";
        bool Controls(FormKey reference, HashSet<FormKey> visited)
        {
            if (!visited.Add(reference)) return false;
            return referenced.Contains(reference) || records.GetValueOrDefault(reference) is ILeveledItemGetter list &&
                list.Entries.Any(entry => Controls(entry.Reference.FormKey, visited));
        }
        return Controls(inventoryRoot, []) ? "Actor script references this equipment branch; retained for individual review." : null;
    }

    private static string Evidence(IMajorRecordGetter actor, ActorProfile? profile,
        IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, string[] locations)
    {
        var factions = actor is INpcGetter npc ? npc.Factions : ((ICreatureGetter)actor).Factions;
        var parts = new List<string> { actor.EditorID ?? "" };
        parts.AddRange(factions.Select(faction => records.GetValueOrDefault(faction.Faction.FormKey)?.EditorID ?? ""));
        if (actor is INpcGetter n)
        {
            parts.Add(records.GetValueOrDefault(n.Class.FormKey)?.EditorID ?? "");
            parts.Add("Race=" + (records.GetValueOrDefault(n.Race.FormKey)?.EditorID ?? ""));
        }
        if (profile is not null) parts.AddRange(profile.Dimensions.Select(pair => $"{pair.Key}={pair.Value.Selected.Value}"));
        parts.AddRange(locations);
        return string.Join(" | ", parts);
    }

    private static Dictionary<FormKey, string[]> ActorLocations(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records)
    {
        var uses = new Dictionary<FormKey, HashSet<string>>();
        foreach (var cell in records.Values.OfType<ICellGetter>())
            foreach (var placed in cell.Persistent.Concat(cell.Temporary).Concat(cell.VisibleWhenDistant).OfType<IPlacedNpcGetter>())
            {
                if (records.GetValueOrDefault(placed.FormKey) is not IPlacedNpcGetter winner) continue;
                if (!uses.TryGetValue(winner.Base.FormKey, out var cells)) uses[winner.Base.FormKey] = cells = [];
                cells.Add(cell.EditorID ?? cell.FormKey.ToString());
            }
        return uses.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.Ordinal).ToArray());
    }
}
