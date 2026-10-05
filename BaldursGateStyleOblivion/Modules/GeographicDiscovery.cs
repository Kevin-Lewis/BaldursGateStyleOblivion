using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Modules;

// Classify locations first, then apply private encounter pools and report each decision.
internal static class GeographicDiscovery
{
    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, PatcherRun run)
    {
        var config = string.IsNullOrWhiteSpace(run.Settings.GeographicConfigurationFile)
            ? Path.Combine(AppContext.BaseDirectory, "geography.json")
            : Path.GetFullPath(run.Settings.GeographicConfigurationFile, run.DataDirectory);
        var settings = GeographicConfiguration.Load(config);
        var cellWorlds = new Dictionary<FormKey, FormKey>();
        var placementCells = new Dictionary<FormKey, FormKey>();
        var roads = new Dictionary<FormKey, (IRoadGetter Road, ModKey Plugin)>();
        var winners = ReadRecords(state, run, cellWorlds, placementCells, roads);
        var records = winners.Where(pair => !pair.Value.Record.IsDeleted).ToDictionary(pair => pair.Key, pair => pair.Value.Record);
        // Observe the current Phase 4 pools alongside their original encounter identities.
        var effective = new Dictionary<FormKey, IMajorRecordGetter>(records);
        foreach (var record in state.PatchMod.EnumerateMajorRecords()) effective[record.FormKey] = record;
        var creatureConfig = string.IsNullOrWhiteSpace(run.Settings.CreatureListConfigurationFile)
            ? Path.Combine(AppContext.BaseDirectory, "creature-lists.json")
            : Path.GetFullPath(run.Settings.CreatureListConfigurationFile, run.DataDirectory);
        var creatureSettings = CreatureListConfiguration.Load(creatureConfig);
        var curated = records.Values.OfType<ILeveledCreatureGetter>().Where(list =>
            CreatureListConfiguration.Select(creatureSettings, list.FormKey.ToString(), list.EditorID, list.FormKey.ModKey.ToString()).Definition.Policy == CreatureListPolicy.CuratedPool ||
            (creatureSettings.FormKeyOverrides.TryGetValue(list.FormKey.ToString(), out var decision) && decision.Policy == CreatureListPolicy.Preserve))
            .Select(list => list.FormKey).ToHashSet();
        var nextId = state.PatchMod.ModHeader.Stats.NextFormID;
        // Report-only must see the same static Phase 4 plan as a gameplay run.
        if (run.Settings.ReportOnly && run.Settings.EnableCreatureListDeleveling)
        {
            var staticPools = new CreaturePoolBuilder(records, profiles, creatureSettings, state.PatchMod.ModKey, nextId,
                records.Values.OfType<ILeveledCreatureGetter>().Where(list => run.Includes(list.FormKey.ModKey)).Select(list => list.FormKey).ToHashSet());
            staticPools.Plan();
            foreach (var key in staticPools.ReachableChanges) effective[key] = staticPools.GetList(key)!;
            nextId = staticPools.PlannedPatch.ModHeader.Stats.NextFormID;
        }
        var geographicPools = new GeographicEncounterPools(effective, profiles,
            key => key.ModKey == state.PatchMod.ModKey || run.Includes(key.ModKey), curated,
            state.PatchMod.ModKey, nextId);
        var encounterPlans = new List<object>();
        var redirects = new Dictionary<FormKey, FormKey>();
        var cells = records.Values.OfType<ICellGetter>().ToDictionary(cell => cell.FormKey);
        var placements = MapPlacements(records, cells, cellWorlds, placementCells);
        var settlements = records.Values.OfType<IPlacedObjectGetter>()
            .Where(marker => marker.MapMarker is not null && marker.Location is not null && (marker.MajorRecordFlagsRaw & 0x800) == 0 &&
                marker.MapMarker.Types?.Any(type => type.ToString() is "City" or "Settlement" or "Tavern") == true &&
                placementCells.TryGetValue(marker.FormKey, out var cell) && cellWorlds.ContainsKey(cell))
            .GroupBy(marker => cellWorlds[placementCells[marker.FormKey]])
            .ToDictionary(group => group.Key, group => group.OrderBy(marker => marker.FormKey.ToString(), StringComparer.Ordinal).ToArray());
        var connections = MapConnections(records, cells, placementCells);
        var sites = MapInteriorSites(cells, connections);
        var entranceMarkers = FindEntranceMarkers(records, cells, placementCells, placements, sites);
        var questUses = new Dictionary<FormKey, HashSet<FormKey>>();
        foreach (var quest in records.Values.OfType<IQuestGetter>())
            foreach (var key in quest.EnumerateFormLinks().Select(link => link.FormKey).Distinct())
            { if (!questUses.TryGetValue(key, out var uses)) questUses[key] = uses = []; uses.Add(quest.FormKey); }
        var questKeys = records.Values.OfType<IQuestGetter>().Select(quest => quest.FormKey).ToHashSet();
        var shared = new Dictionary<FormKey, HashSet<FormKey>>();
        var rows = new List<object>();
        var categoryCounts = new Dictionary<string, int>();
        var unclassified = 0; var withEncounters = 0;
        object Identity(FormKey key) => new { FormKey = key.ToString(), EditorID = records.GetValueOrDefault(key)?.EditorID,
            Name = records.GetValueOrDefault(key) switch { ICellGetter c => c.Name, IWorldspaceGetter w => w.Name, IFactionGetter f => f.Name, IQuestGetter q => q.Name, _ => null } };
        foreach (var cell in cells.Values.Where(cell => run.Includes(cell.FormKey.ModKey)).OrderBy(cell => cell.FormKey.ToString(), StringComparer.Ordinal))
        {
            var localEncounterPlans = new List<object>();
            var local = placements.GetValueOrDefault(cell.FormKey, []);
            var world = cellWorlds.TryGetValue(cell.FormKey, out var worldKey) ? records.GetValueOrDefault(worldKey) as IWorldspaceGetter : null;
            var regions = (cell.Regions ?? []).Select(link => link.FormKey).Distinct().OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
            var doors = connections[cell.FormKey].OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
            var bases = local.Select(record => Base(record, records)).Where(key => !key.IsNull).ToArray();
            var originalPools = new HashSet<FormKey>(); var currentPools = new HashSet<FormKey>(); var issues = new SortedSet<string>(StringComparer.Ordinal);
            var originalActors = Actors(bases, records, originalPools, issues);
            var actors = Actors(bases, effective, currentPools, issues);
            foreach (var pool in originalPools)
            { if (!shared.TryGetValue(pool, out var uses)) shared[pool] = uses = []; uses.Add(cell.FormKey); }
            if (bases.Length > 0) withEncounters++;
            var factionKeys = actors.SelectMany(key => effective[key] switch
            { INpcGetter npc => npc.Factions.Select(faction => faction.Faction.FormKey), ICreatureGetter creature => creature.Factions.Select(faction => faction.Faction.FormKey), _ => [] }).Distinct().OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
            var quests = new HashSet<FormKey>();
            foreach (var key in actors.Concat(originalPools).Concat(local.Select(record => record.FormKey)).Append(cell.FormKey))
            {
                quests.UnionWith(questUses.GetValueOrDefault(key, []));
                var scriptKey = effective.GetValueOrDefault(key) switch { INpcGetter npc => npc.Script.FormKeyNullable, ICreatureGetter creature => creature.Script.FormKeyNullable, _ => null };
                if (scriptKey is { } script && records.GetValueOrDefault(script) is IScriptGetter code)
                    quests.UnionWith(code.EnumerateFormLinks().Select(link => link.FormKey).Where(questKeys.Contains));
            }
            var markerRows = local.OfType<IPlacedObjectGetter>().Where(placed => placed.MapMarker is not null)
                .Select(placed => new { FormKey = placed.FormKey.ToString(), placed.MapMarker!.Name, Types = placed.MapMarker.Types?.Select(type => type.ToString()).ToArray() ?? [] }).ToArray();
            var signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var text = $"{cell.EditorID} {cell.Name}";
            var poolText = string.Join(" ", originalPools.Select(key => records[key].EditorID));
            var regionText = string.Join(" ", regions.Select(key => records.GetValueOrDefault(key)?.EditorID));
            var worldText = $"{world?.EditorID} {world?.Name}";
            var interior = cell.Flags?.HasFlag(Cell.Flag.IsInteriorCell) == true;
            if (interior) signals.Add("Interior"); else signals.Add("Exterior");
            if (quests.Count > 0) signals.Add("QuestAssociated");
            if (actors.Count == 0) signals.Add("NoRecordedActors");
            var roadData = world is null ? default : roads.GetValueOrDefault(world.FormKey);
            var roadDistance = !interior && cell.Grid is { } grid && roadData.Road is { } road ? RoadDistance(grid.X, grid.Y, road) : null;
            var settlement = !interior && cell.Grid is { } settlementGrid && world is not null
                ? settlements.GetValueOrDefault(world.FormKey, []).Select(marker => new { Marker = marker, Distance = Math.Sqrt(
                    Math.Pow((settlementGrid.X + 0.5) * 4096 - marker.Location!.Position.X, 2) +
                    Math.Pow((settlementGrid.Y + 0.5) * 4096 - marker.Location.Position.Y, 2)) })
                    .OrderBy(value => value.Distance).ThenBy(value => value.Marker.FormKey.ToString(), StringComparer.Ordinal).FirstOrDefault()
                : null;
            if (settlement?.Distance <= 6144) signals.Add("NearSettlement");
            if (roadDistance <= 10240) signals.Add("NearRoad");
            foreach (var marker in markerRows) foreach (var type in marker.Types) signals.Add("Marker:" + type);
            foreach (var marker in entranceMarkers.GetValueOrDefault(cell.FormKey, []))
                foreach (var type in marker.MapMarker!.Types ?? []) signals.Add("Entrance:" + type);
            if (interior && doors.Any(key => cellWorlds.TryGetValue(key, out var connectedWorld) &&
                records.GetValueOrDefault(connectedWorld) is IWorldspaceGetter linkedWorld && System.Text.RegularExpressions.Regex.IsMatch(linkedWorld.EditorID ?? "", @"^(?:Anvil|Bravil|Bruma|Cheydinhal|Chorrol|Leyawiin|Skingrad|IC|ImperialCity)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) signals.Add("SettledEntrance");
            foreach (var category in actors.Select(key => Category(profiles.GetValueOrDefault(key))).Where(category => category is not null)) signals.Add("Actor:" + category);
            foreach (var keyword in new[] { "Cave", "Fort", "Ruin", "Ayleid", "Necromancer", "Conjurer", "Bandit", "Marauder", "Vampire", "Goblin", "Undead" })
                if ((text + " " + poolText).Contains(keyword, StringComparison.OrdinalIgnoreCase)) signals.Add(keyword);
            // Resolve location identity separately from its observed occupants.
            var (definition, rule) = GeographicConfiguration.Select(settings, cell.FormKey.ToString(), cell.FormKey.ModKey.ToString(), text, worldText, regionText, signals);
            if (run.Settings.EnableGeographicEncounters)
                foreach (var placed in local.OfType<IPlacedObjectGetter>().Where(placed => effective.GetValueOrDefault(placed.Base.FormKey) is ILeveledCreatureGetter))
                {
                    var reason = "Typical geographic range favors matching actors; original encounter identity and counts retained.";
                    var target = placed.Base.FormKey;
                    try
                    {
                        if (!run.Includes(placed.FormKey.ModKey)) throw new InvalidDataException("Placement plugin is excluded.");
                        if (quests.Count > 0 || placed.EnumerateFormLinks().Any(link => records.GetValueOrDefault(link.FormKey) is IScriptGetter))
                            throw new InvalidDataException("Quest-associated or scripted placement preserved.");
                        if (definition.Category == LocationCategory.Special || definition.MinimumTier is not { } minimum || definition.MaximumTier is not { } maximum)
                            throw new InvalidDataException("Handcrafted or unclassified location requires a typical range.");
                        target = geographicPools.Build(placed.Base.FormKey, minimum, maximum);
                        if (target == placed.Base.FormKey) reason = "Existing pool probabilities already match this weighting (uniform or single-kind pool).";
                        else redirects[placed.FormKey] = target;
                    }
                    catch (InvalidDataException exception) { reason = exception.Message; }
                    var encounterPlan = new { Location = cell.FormKey.ToString(), Placement = placed.FormKey.ToString(),
                        OriginalPool = placed.Base.FormKey.ToString(), PlannedPool = target.ToString(), Rule = rule,
                        definition.MinimumTier, definition.MaximumTier, Reason = reason,
                        Status = target == placed.Base.FormKey ? "Preserved" : run.Settings.ReportOnly ? "WouldModify" : "Modified" };
                    encounterPlans.Add(encounterPlan); localEncounterPlans.Add(encounterPlan);
                }
            var profile = new LocationProfile();
            profile.Apply(new LocationValues { Category = definition.Category,
                EnemyTierRange = definition.MinimumTier is { } min && definition.MaximumTier is { } max ? new(new(min), new(max)) : null },
                rule, definition.Reason, settings.FormKeyOverrides.ContainsKey(cell.FormKey.ToString()) ? RulePriority.ExplicitFormKeyOverride : RulePriority.Location);
            if (definition.Category is null) { unclassified++; issues.Add("Category needs review."); }
            else categoryCounts[definition.Category.ToString()!] = categoryCounts.GetValueOrDefault(definition.Category.ToString()!) + 1;
            if (actors.Any(key => profiles.GetValueOrDefault(key)?.Tier?.Value >= 7)) issues.Add("Legendary or stronger inhabitant: review whether it is an exceptional threat or a non-hostile resident.");
            if (quests.Count > 0) issues.Add("Quest references are evidence, not proof of a required encounter or difficulty.");
            if (definition.Category == LocationCategory.Special) issues.Add("Handcrafted location: review the typical tier range individually.");
            var threats = actors.Where(key => Category(profiles.GetValueOrDefault(key)) is not ("Civilian" or "Guard" or "Soldier") &&
                !(effective[key] is ICreatureGetter creature && (creature.Name ?? "").Contains("Horse", StringComparison.OrdinalIgnoreCase))).ToArray();
            var tiers = threats.Select(key => profiles.GetValueOrDefault(key)?.Tier?.Value).Where(tier => tier is not null).Select(tier => tier!.Value).ToArray();
            if (threats.Any(key => profiles.GetValueOrDefault(key)?.Tier is null)) issues.Add("Some possible actors have no assigned tier.");
            if (definition.MaximumTier is { } ceiling && tiers.Any(tier => tier > ceiling)) issues.Add("Possible threat above proposed typical range; may be an intended exception.");
            var confidence = settings.FormKeyOverrides.ContainsKey(cell.FormKey.ToString()) ? "Manual" : definition.Category is null || rule.Contains("wilderness", StringComparison.OrdinalIgnoreCase) ? "Low" : "Medium";
            rows.Add(new
            {
                FormKey = cell.FormKey.ToString(), cell.EditorID, cell.Name, SourcePlugin = cell.FormKey.ModKey.ToString(), WinningOverridePlugin = winners[cell.FormKey].Plugin.ToString(),
                Interior = interior, Grid = cell.Grid is { } point ? new { point.X, point.Y } : null, Worldspace = world is null ? null : Identity(world.FormKey),
                SiteCells = sites.GetValueOrDefault(cell.FormKey, []).Select(Identity).ToArray(),
                EntranceMarkers = entranceMarkers.GetValueOrDefault(cell.FormKey, []).Select(marker => new { marker.MapMarker!.Name, Types = marker.MapMarker.Types?.Select(type => type.ToString()).ToArray() }).ToArray(),
                Regions = regions.Select(Identity).ToArray(), RoadDistanceFromCellCenter = roadDistance,
                RoadSourcePlugin = roadData.Road is null ? null : roadData.Plugin.ToString(),
                NearestSettlement = settlement is null ? null : new { FormKey = settlement.Marker.FormKey.ToString(), settlement.Marker.MapMarker!.Name,
                    DistanceFromCellCenter = Math.Round(settlement.Distance, 1), Types = settlement.Marker.MapMarker.Types?.Select(type => type.ToString()).ToArray() },
                Markers = markerRows, ConnectedCells = doors.Select(Identity).ToArray(),
                Proposal = new { definition.Category, definition.MinimumTier, definition.MaximumTier, Rule = rule, definition.Reason, Confidence = confidence,
                    HasOverride = settings.FormKeyOverrides.ContainsKey(cell.FormKey.ToString()), Status = "ProposalOnly" },
                GeographicEncounters = localEncounterPlans,
                Signals = signals.Order(StringComparer.Ordinal).ToArray(), ReviewSignals = issues.ToArray(), Profile = profile,
                EncounterPlacements = local.Where(record => !Base(record, records).IsNull).Select(record => new { Reference = record.FormKey.ToString(), Base = Base(record, records).ToString(), record.EditorID,
                    InitiallyDisabled = (record.MajorRecordFlagsRaw & 0x800) != 0 }).ToArray(),
                OriginalPools = originalPools.OrderBy(key => key.ToString(), StringComparer.Ordinal).Select(Identity).ToArray(),
                CurrentPools = currentPools.OrderBy(key => key.ToString(), StringComparer.Ordinal).Select(key => new { FormKey = key.ToString(), effective[key].EditorID }).ToArray(),
                Actors = actors.OrderBy(key => key.ToString(), StringComparer.Ordinal).Select(key => new { FormKey = key.ToString(), effective[key].EditorID,
                    Name = effective[key] is INpcGetter npc ? npc.Name : ((ICreatureGetter)effective[key]).Name, Tier = profiles.GetValueOrDefault(key)?.Tier?.Value,
                    Category = Category(profiles.GetValueOrDefault(key)), Direct = bases.Contains(key) }).ToArray(),
                OriginalActorCount = originalActors.Count, PossibleActorCount = actors.Count,
                ObservedThreatTierRange = tiers.Length == 0 ? null : new { Minimum = tiers.Min(), Maximum = tiers.Max() },
                Factions = factionKeys.Select(Identity).ToArray(), QuestAssociations = quests.OrderBy(key => key.ToString(), StringComparer.Ordinal).Select(Identity).ToArray()
            });
        }
        run.WriteReport(".geography.json", new
        {
            ProposalOnly = true, Configuration = config, ModifiedRecords = 0,
            Summary = new { Locations = rows.Count, WithRecordedEncounters = withEncounters, Unclassified = unclassified, Categories = categoryCounts }, Locations = rows,
            Limitations = new[] { "Possible actors include conditional and initially disabled placements; this is not a simultaneous spawn count or a hostility test.",
                "Road distance is measured from cell center to recorded road segments; child road records resolve separately from worldspace headers. A nearby road does not make the whole cell safe.",
                "Settlement proximity uses active City, Settlement and Tavern markers in the same worldspace within 6144 units of cell center. Marker type is evidence of settlement, not proof of safety.",
                "Interior connections are observed door links, not a complete dungeon grouping. Regions are taken from explicit cell links.",
                "Script-created encounters and dynamic teleport destinations may not be discoverable from static placement records. Geographic encounter changes are reported separately in geographic-encounters.json." }
        }, GeographicConfiguration.JsonOptions);
        run.WriteReport(".geographic-shared-pools.json", new { Pools = shared.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new { Pool = Identity(pair.Key), Shared = pair.Value.Count > 1, Locations = pair.Value.OrderBy(key => key.ToString(), StringComparer.Ordinal).Select(Identity).ToArray() }).ToArray() }, GeographicConfiguration.JsonOptions);
        var changes = ApplyEncounterPlans(state, run, geographicPools, redirects);
        run.WriteReport(".geographic-encounters.json", new { Enabled = run.Settings.EnableGeographicEncounters,
            run.Settings.ReportOnly, RedirectedPlacements = redirects.Count, GeneratedPools = geographicPools.Patch.LeveledCreatures.Count,
            ModifiedRecords = changes.Count, Encounters = encounterPlans,
            Pools = geographicPools.Patch.LeveledCreatures.Select(list => new { FormKey = list.FormKey.ToString(), list.EditorID, Planned = CreatureListDeleveling.Snapshot(list) }).ToArray() }, GeographicConfiguration.JsonOptions);
        run.Log($"Geographic encounters: {redirects.Count} planned redirects, {geographicPools.Patch.LeveledCreatures.Count} private pools; {changes.Count} records modified.");
        run.Log($"Geography: {rows.Count} location proposals, {withEncounters} with recorded encounters; location proposals written.");
        return changes;
    }

    private static Dictionary<FormKey, string[]> ApplyEncounterPlans(
        IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run,
        GeographicEncounterPools geographicPools, IReadOnlyDictionary<FormKey, FormKey> redirects)
    {
        var changes = new Dictionary<FormKey, string[]>();
        if (!run.Settings.ReportOnly)
        {
            foreach (var list in geographicPools.Patch.LeveledCreatures)
            { state.PatchMod.LeveledCreatures.Add(list); changes[list.FormKey] = ["NewRecord"]; }
            state.PatchMod.ModHeader.Stats.NextFormID = geographicPools.Patch.ModHeader.Stats.NextFormID;
            var contexts = state.LoadOrder.PriorityOrder.Where(listing => listing.Enabled && listing.Mod is not null && run.IsInputPlugin(listing.ModKey))
                .SelectMany(listing => listing.Mod!.EnumerateMajorRecordContexts<IPlacedObject, IPlacedObjectGetter>(state.LinkCache))
                .GroupBy(context => context.Record.FormKey).ToDictionary(group => group.Key, group => group.First());
            foreach (var redirect in redirects.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
            {
                var context = contexts[redirect.Key];
                context.GetOrAddAsOverride(state.PatchMod).Base.SetTo(redirect.Value);
                changes[redirect.Key] = ["Base"];
            }
        }
        return changes;
    }

    private static Dictionary<FormKey, (IMajorRecordGetter Record, ModKey Plugin)> ReadRecords(
        IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run,
        Dictionary<FormKey, FormKey> cellWorlds, Dictionary<FormKey, FormKey> placementCells,
        Dictionary<FormKey, (IRoadGetter Road, ModKey Plugin)> roads)
    {
        var winners = new Dictionary<FormKey, (IMajorRecordGetter Record, ModKey Plugin)>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(listing => listing.Enabled && listing.Mod is not null && run.IsInputPlugin(listing.ModKey)))
        {
            foreach (var record in listing.Mod!.EnumerateMajorRecords()) winners.TryAdd(record.FormKey, (record, listing.ModKey));
            foreach (var world in listing.Mod.Worldspaces)
            {
                // A header-only override does not replace an independently stored ROAD child.
                if (world.Road is { } road) roads.TryAdd(world.FormKey, (road, listing.ModKey));
                if (world.TopCell is { } top) cellWorlds.TryAdd(top.FormKey, world.FormKey);
                foreach (var cell in world.SubCells.SelectMany(block => block.Items).SelectMany(block => block.Items))
                    cellWorlds.TryAdd(cell.FormKey, world.FormKey);
            }
            // Child references can be overridden without a complete override of the containing cell.
            foreach (var cell in listing.Mod.EnumerateMajorRecords().OfType<ICellGetter>())
                foreach (var placed in cell.Persistent.Concat(cell.Temporary).Concat(cell.VisibleWhenDistant))
                    placementCells.TryAdd(placed.FormKey, cell.FormKey);
        }
        return winners;
    }

    private static Dictionary<FormKey, IMajorRecordGetter[]> MapPlacements(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, IReadOnlyDictionary<FormKey, ICellGetter> cells,
        IReadOnlyDictionary<FormKey, FormKey> cellWorlds, Dictionary<FormKey, FormKey> placementCells)
    {
        foreach (var key in placementCells.Keys.Where(key => !cells.ContainsKey(placementCells[key])).ToArray())
            placementCells.Remove(key);
        var grids = cells.Values.Where(cell => cell.Grid is not null && cellWorlds.ContainsKey(cell.FormKey))
            .GroupBy(cell => (World: cellWorlds[cell.FormKey], X: cell.Grid!.Value.X, Y: cell.Grid.Value.Y))
            .ToDictionary(group => group.Key, group => group.First().FormKey);
        // Persistent worldspace references belong to their physical grid cell when one is available.
        foreach (var key in placementCells.Keys.ToArray())
            if (cells.TryGetValue(placementCells[key], out var cell) && cell.Grid is null && cellWorlds.TryGetValue(cell.FormKey, out var world) &&
                records.GetValueOrDefault(key) is IPlacedGetter)
            {
                var location = records[key] switch { IPlacedObjectGetter obj => obj.Location, IPlacedNpcGetter npc => npc.Location, IPlacedCreatureGetter creature => creature.Location, _ => null };
                if (location is not null && grids.TryGetValue((world, (int)Math.Floor(location.Position.X / 4096), (int)Math.Floor(location.Position.Y / 4096)), out var physical))
                    placementCells[key] = physical;
            }
        var placements = placementCells.Where(pair => records.ContainsKey(pair.Key)).GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => records[pair.Key]).OrderBy(record => record.FormKey.ToString(), StringComparer.Ordinal).ToArray());
        return placements;
    }

    private static Dictionary<FormKey, HashSet<FormKey>> MapConnections(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, IReadOnlyDictionary<FormKey, ICellGetter> cells,
        IReadOnlyDictionary<FormKey, FormKey> placementCells)
    {
        var connections = cells.Keys.ToDictionary(key => key, _ => new HashSet<FormKey>());
        foreach (var placed in records.Values.OfType<IPlacedObjectGetter>())
            if (placed.TeleportDestination is { } destination && placementCells.TryGetValue(placed.FormKey, out var source) &&
                placementCells.TryGetValue(destination.Destination.FormKey, out var target) && source != target)
            { connections[source].Add(target); connections[target].Add(source); }
        return connections;
    }

    private static Dictionary<FormKey, FormKey[]> MapInteriorSites(IReadOnlyDictionary<FormKey, ICellGetter> cells, IReadOnlyDictionary<FormKey, HashSet<FormKey>> connections)
    {
        var sites = new Dictionary<FormKey, FormKey[]>();
        foreach (var cell in cells.Values.Where(cell => cell.Flags?.HasFlag(Cell.Flag.IsInteriorCell) == true).OrderBy(cell => cell.FormKey.ToString(), StringComparer.Ordinal))
        {
            if (sites.ContainsKey(cell.FormKey)) continue;
            var rooms = new HashSet<FormKey>(); var queue = new Queue<FormKey>(); queue.Enqueue(cell.FormKey);
            while (queue.TryDequeue(out var room))
            {
                if (!rooms.Add(room)) continue;
                foreach (var next in connections[room])
                    if (cells[next].Flags?.HasFlag(Cell.Flag.IsInteriorCell) == true) queue.Enqueue(next);
            }
            var ordered = rooms.OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray();
            foreach (var room in rooms) sites[room] = ordered;
        }
        return sites;
    }

    private static Dictionary<FormKey, List<IPlacedObjectGetter>> FindEntranceMarkers(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, IReadOnlyDictionary<FormKey, ICellGetter> cells,
        IReadOnlyDictionary<FormKey, FormKey> placementCells, IReadOnlyDictionary<FormKey, IMajorRecordGetter[]> placements,
        IReadOnlyDictionary<FormKey, FormKey[]> sites)
    {
        var entranceMarkers = new Dictionary<FormKey, List<IPlacedObjectGetter>>();
        foreach (var door in records.Values.OfType<IPlacedObjectGetter>())
        {
            if (door.TeleportDestination is not { } destination || !placementCells.TryGetValue(door.FormKey, out var interiorKey) ||
                !sites.TryGetValue(interiorKey, out var rooms) || !placementCells.TryGetValue(destination.Destination.FormKey, out var exteriorKey) ||
                cells[exteriorKey].Flags?.HasFlag(Cell.Flag.IsInteriorCell) == true || records.GetValueOrDefault(destination.Destination.FormKey) is not IPlacedObjectGetter exit || exit.Location is null) continue;
            var nearest = placements.GetValueOrDefault(exteriorKey, []).OfType<IPlacedObjectGetter>().Where(marker => marker.MapMarker is not null && marker.Location is not null)
                .Select(marker => (Marker: marker, Distance: Math.Pow(marker.Location!.Position.X - exit.Location.Position.X, 2) + Math.Pow(marker.Location.Position.Y - exit.Location.Position.Y, 2)))
                .Where(value => value.Distance <= 4096 * 4096).OrderBy(value => value.Distance).FirstOrDefault().Marker;
            if (nearest is null) continue;
            foreach (var room in rooms)
            { if (!entranceMarkers.TryGetValue(room, out var markers)) entranceMarkers[room] = markers = []; if (!markers.Contains(nearest)) markers.Add(nearest); }
        }
        return entranceMarkers;
    }

    private static FormKey Base(IMajorRecordGetter record, IReadOnlyDictionary<FormKey, IMajorRecordGetter> records)
    {
        var key = record switch { IPlacedNpcGetter npc => npc.Base.FormKey, IPlacedCreatureGetter creature => creature.Base.FormKey,
            IPlacedObjectGetter obj => obj.Base.FormKey, _ => FormKey.Null };
        return records.GetValueOrDefault(key) is INpcGetter or ICreatureGetter or ILeveledCreatureGetter ? key : FormKey.Null;
    }

    private static string? Category(ActorProfile? profile) => profile?.Dimensions.GetValueOrDefault(nameof(ActorValues.ActorCategory))?.Selected.Value.ToString();

    private static HashSet<FormKey> Actors(IEnumerable<FormKey> roots, IReadOnlyDictionary<FormKey, IMajorRecordGetter> records,
        HashSet<FormKey> pools, SortedSet<string> issues)
    {
        var actors = new HashSet<FormKey>(); var visited = new HashSet<FormKey>();
        void Visit(FormKey key, HashSet<FormKey> path)
        {
            if (records.GetValueOrDefault(key) is INpcGetter or ICreatureGetter) { actors.Add(key); return; }
            if (!path.Add(key)) { issues.Add($"Cyclic encounter pool {key}."); return; }
            if (visited.Add(key))
            {
                if (records.GetValueOrDefault(key) is ILeveledCreatureGetter list)
                { pools.Add(key); foreach (var entry in list.Entries ?? []) Visit(entry.Reference.FormKey, path); }
                else issues.Add($"Missing or unsupported encounter reference {key}.");
            }
            path.Remove(key);
        }
        foreach (var key in roots) Visit(key, []);
        return actors;
    }

    private static double? RoadDistance(int x, int y, IRoadGetter road)
    {
        var cx = (x + 0.5) * 4096; var cy = (y + 0.5) * 4096; var distance = double.PositiveInfinity;
        foreach (var point in road.Points ?? [])
        {
            distance = Math.Min(distance, Math.Sqrt(Math.Pow(cx - point.Point.X, 2) + Math.Pow(cy - point.Point.Y, 2)));
            foreach (var end in point.Connections ?? [])
            {
                var dx = end.X - point.Point.X; var dy = end.Y - point.Point.Y; var length = dx * dx + dy * dy;
                var t = length == 0 ? 0 : Math.Clamp(((cx - point.Point.X) * dx + (cy - point.Point.Y) * dy) / length, 0, 1);
                distance = Math.Min(distance, Math.Sqrt(Math.Pow(cx - point.Point.X - t * dx, 2) + Math.Pow(cy - point.Point.Y - t * dy, 2)));
            }
        }
        return double.IsFinite(distance) ? Math.Round(distance, 1) : null;
    }
}
