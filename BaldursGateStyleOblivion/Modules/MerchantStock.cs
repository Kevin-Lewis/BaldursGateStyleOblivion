using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Core;
using BaldursGateStyleOblivion.Discovery;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Modules;

internal static class MerchantStock
{
    internal static bool CanSell(IMajorRecordGetter item, string services)
    {
        var category = item switch
        {
            IWeaponGetter or IAmmunitionGetter => "Weapons", IArmorGetter => "Armor", IClothingGetter => "Clothing",
            IBookGetter => "Books", IIngredientGetter => "Ingredients", IPotionGetter => "Potions",
            IAlchemicalApparatusGetter => "Apparatus", ILightGetter => "Lights", IMiscellaneousGetter or ISoulGemGetter => "Miscellaneous", _ => ""
        };
        var enchanted = item switch { IWeaponGetter x => !x.Enchantment.IsNull, IArmorGetter x => !x.Enchantment.IsNull,
            IClothingGetter x => !x.Enchantment.IsNull, IAmmunitionGetter x => !x.Enchantment.IsNull, IBookGetter x => !x.Enchantment.IsNull, _ => false };
        return (item.EditorID ?? "") != "Gold001" && (services.Split(", ").Contains(category) || enchanted && services.Split(", ").Contains("MagicItems"));
    }
    internal static IEnumerable<IMajorRecordGetter> InventoryLeaves(IReadOnlyDictionary<FormKey, IMajorRecordGetter> records, FormKey key, HashSet<FormKey> visited)
    {
        if (!visited.Add(key) || records.GetValueOrDefault(key) is not {} item) yield break;
        if (item is ILeveledItemGetter list)
        {
            foreach (var entry in list.Entries)
                foreach (var leaf in InventoryLeaves(records, entry.Reference.FormKey, visited)) yield return leaf;
        }
        else yield return item;
    }
    public static Dictionary<FormKey, string[]> Run(IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run)
    {
        var path = Path.Combine(run.DataDirectory, "merchants.json");
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "merchants.json");
        if (!string.IsNullOrWhiteSpace(run.Settings.MerchantConfigurationFile)) path = Path.GetFullPath(run.Settings.MerchantConfigurationFile, run.DataDirectory);
        var settings = MerchantConfiguration.Load(path);
        var records = new Dictionary<FormKey, IMajorRecordGetter>();
        foreach (var listing in state.LoadOrder.PriorityOrder.Where(l => l.Enabled && l.Mod is not null && run.IsInputPlugin(l.ModKey)))
            foreach (var record in listing.Mod!.EnumerateMajorRecords()) records.TryAdd(record.FormKey, record);
        records = records.Where(p => !p.Value.IsDeleted).ToDictionary();
        var placements = records.Values.OfType<IPlacedNpcGetter>().GroupBy(p => p.Base.FormKey).ToDictionary(g => g.Key, g => g.OrderBy(p => p.FormKey.ToString(), StringComparer.Ordinal).ToArray());
        var cells = new Dictionary<FormKey, ICellGetter>();
        foreach (var cell in records.Values.OfType<ICellGetter>())
            foreach (var placed in cell.Persistent.Concat(cell.Temporary).Concat(cell.VisibleWhenDistant)) cells.TryAdd(placed.FormKey, cell);
        var scripts = QuestRewards.ScriptContexts(records.Values).ToArray();
        var rewards = RewardConfiguration.Load(RewardRecords.PathFor(run));
        var protectedItems = RewardRecords.Protected(rewards);
        var builder = new MerchantPoolBuilder(records, settings, state.PatchMod.ModKey, state.PatchMod.ModHeader.Stats.NextFormID,
            run.Includes, protectedItems, WorldLoot.UnsafeListReferences(scripts.Select(s => s.Fields)));
        var merchants = records.Values.OfType<INpcGetter>().Where(n => run.Includes(n.FormKey.ModKey) &&
            ((n.AIData?.BuySellServices ?? 0) != 0 || placements.GetValueOrDefault(n.FormKey, []).Any(p => !p.MerchantContainer.IsNull)))
            .OrderBy(n => n.FormKey.ToString(), StringComparer.Ordinal).ToArray();
        IContainerGetter? Stock(IPlacedNpcGetter npc) => records.GetValueOrDefault(npc.MerchantContainer.FormKey) is IPlacedObjectGetter placed
            ? records.GetValueOrDefault(placed.Base.FormKey) as IContainerGetter : null;
        string Id(FormKey key) => records.GetValueOrDefault(key)?.EditorID ?? "";
        var assignments = merchants.ToDictionary(n => n.FormKey, n =>
        {
            var places = placements.GetValueOrDefault(n.FormKey, []);
            var evidence = string.Join(" ", places.Select(p => Stock(p)?.EditorID).Append(n.EditorID).Append(Id(n.Class.FormKey))
                .Append("Services:" + (n.AIData?.BuySellServices ?? 0)).Concat(n.Factions.Select(f => Id(f.Faction.FormKey))).Concat(places.Select(p => cells.GetValueOrDefault(p.FormKey)?.EditorID)));
            var selected = MerchantConfiguration.Select(settings, n.FormKey.ToString(), n.FormKey.ModKey.ToString(), evidence);
            var material = selected.Material ?? settings.RaceMaterials.GetValueOrDefault(Id(n.Race.FormKey), "");
            return (selected.Profile, selected.Rule, selected.Preserve, Material: material, Evidence: evidence);
        });
        var conflicts = merchants.SelectMany(n => placements.GetValueOrDefault(n.FormKey, []).Where(p => !p.MerchantContainer.IsNull)
            .Select(p => (Reference: p.MerchantContainer.FormKey, Assignment: assignments[n.FormKey])))
            .GroupBy(p => p.Reference).Where(g => g.Select(p => (p.Assignment.Profile, p.Assignment.Material, p.Assignment.Preserve)).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet();
        var redirects = new Dictionary<FormKey, FormKey>();
        var changes = new Dictionary<FormKey, string[]>();
        var rows = new List<object>();
        var related = merchants.Select(n => n.FormKey).ToHashSet();
        var containers = new Dictionary<(FormKey, string, string), FormKey>();
                var modifiedBranches = 0; var retainedBranches = 0; var remainingGates = 0;
        bool LevelGated(FormKey key, HashSet<FormKey> visited)
        {
            if (!visited.Add(key)) return false;
            return records.GetValueOrDefault(key) is ILeveledItemGetter list &&
                list.Entries.Any(e => e.Level > 1 || LevelGated(e.Reference.FormKey, visited));
        }
        foreach (var npc in merchants)
        {
            var selected = assignments[npc.FormKey];
            var stockProfile = selected.Profile;
            if (settings.Overrides.GetValueOrDefault(npc.FormKey.ToString())?.GlassItems.Count > 0) stockProfile += "|" + npc.FormKey;
            var plans = new List<object>();
            void Plan(FormKey owner, string kind, FormKey root, long count, string? guard, Action<FormKey> apply)
            {
                related.Add(owner);
                if (records.GetValueOrDefault(root) is ILeveledItemGetter) related.Add(root);
                if (records.GetValueOrDefault(root) is not ILeveledItemGetter list)
                {
                    plans.Add(new { Owner = owner.ToString(), Kind = kind, Source = root.ToString(), EditorID = Id(root), Count = count,
                        Status = "Fixed", Reason = "Authored fixed stock/carried item retained; no level selection to remove.", Protected = protectedItems.Contains(root) });
                    return;
                }
                var target = root; var status = "Retained"; var reason = guard ?? "Static private stock; original family, quantities and empty chances retained.";
                try
                {
                    if (selected.Preserve) throw new InvalidDataException("Merchant explicitly preserved.");
                    if (guard is not null) throw new InvalidDataException(guard);
                    if (count is <= 0 or > int.MaxValue) throw new InvalidDataException("Non-standard inventory count retained.");
                    target = builder.Build(root, selected.Profile, selected.Material);
                    if (!run.Settings.ReportOnly) apply(target);
                    status = run.Settings.ReportOnly ? "Planned" : "Modified"; modifiedBranches++;
                }
                catch (InvalidDataException error) { reason = error.Message; retainedBranches++; if (LevelGated(root, [])) remainingGates++; }
                plans.Add(new { Owner = owner.ToString(), Kind = kind, Source = root.ToString(), EditorID = list.EditorID, Count = count,
                    Target = target.ToString(), Status = status, Reason = reason, SourceLevelGated = LevelGated(root, []),
                    RemainingLevelGated = status == "Retained" && LevelGated(root, []) });
            }
            var services = (npc.AIData?.BuySellServices ?? 0).ToString();
            var saleServices = services.Split(", ").Except(["0", "Training", "Repair", "Recharge"]).Any();
            bool Sold(IMajorRecordGetter item) => CanSell(item, services);
            if (saleServices)
                for (var index = 0; index < npc.Items.Count; index++)
                {
                    var slot = index; var item = npc.Items[index];
                    var leaves = InventoryLeaves(records, item.Item.FormKey, []).ToArray();
                    if (leaves.Length > 0 && !leaves.Any(Sold))
                    {
                        plans.Add(new { Kind = "Carried inventory", Source = item.Item.FormKey.ToString(), EditorID = Id(item.Item.FormKey),
                            Status = "Carried", Reason = "Not offered by merchant sale services; actor equipment policy remains in charge." });
                        continue;
                    }
                    var guard = leaves.Any(Sold) && leaves.Any(leaf => !Sold(leaf)) ? "Mixed personal sale stock and unsold carried goods retained." : null;
                    Plan(npc.FormKey, "Personal sale inventory", item.Item.FormKey, item.Count ?? 0, guard, target =>
                    {
                        state.PatchMod.Npcs.GetOrAddAsOverride(npc).Items[slot].Item.SetTo(target);
                        changes[npc.FormKey] = ["Items"];
                    });
                }
            var places = placements.GetValueOrDefault(npc.FormKey, []);
            foreach (var place in places) related.Add(place.FormKey);
            foreach (var place in places.Where(p => !p.MerchantContainer.IsNull))
            {
                var reference = place.MerchantContainer.FormKey;
                related.Add(reference);
                if (records.GetValueOrDefault(reference) is not IPlacedObjectGetter placed || Stock(place) is not {} stock)
                {
                    plans.Add(new { Owner = reference.ToString(), Kind = "Merchant container", Status = "Retained", Reason = "Unresolved stock container." });
                    retainedBranches++; continue;
                }
                related.Add(stock.FormKey);
                var guard = !run.Includes(placed.FormKey.ModKey) || !run.Includes(stock.FormKey.ModKey) ? "Stock placement/base plugin excluded." :
                    conflicts.Contains(reference) ? "Stock reference shared by merchants with conflicting profiles; explicit shared policy required." :
                    !stock.Script.IsNull ? "Scripted stock container retained for review." : null;
                Container? copy = null;
                var cache = (stock.FormKey, stockProfile, selected.Material);
                for (var index = 0; index < stock.Items.Count; index++)
                {
                    var slot = index; var item = stock.Items[index];
                    Plan(reference, "Linked merchant stock", item.Item.FormKey, item.Count, guard, target =>
                    {
                        if (!containers.TryGetValue(cache, out var copyKey))
                        {
                            copy = builder.Patch.Containers.AddNew(); copy.DeepCopyIn(stock); copy.EditorID = $"BGSO_MerchantStock_{copy.FormKey.ID:X6}";
                            containers[cache] = copyKey = copy.FormKey;
                        }
                        else copy = builder.Patch.Containers[copyKey];
                        copy.Items[slot].Item.SetTo(target);
                        redirects[placed.FormKey] = copyKey;
                        changes[placed.FormKey] = ["Base"];
                    });
                }
            }
            if (!selected.Preserve && settings.Profiles[selected.Profile].GlassPercent > 0 &&
                settings.Overrides.TryGetValue(npc.FormKey.ToString(), out var assignment) && assignment.GlassItems.Count > 0)
            {
                var place = places.FirstOrDefault(p => !p.MerchantContainer.IsNull);
                var status = "Retained"; var reason = "One curated Glass item opportunity per refreshed stock container.";
                try
                {
                    if (place is null || records.GetValueOrDefault(place.MerchantContainer.FormKey) is not IPlacedObjectGetter placed || Stock(place) is not {} stock)
                        throw new InvalidDataException("Curated Glass offer requires a linked stock container.");
                    if (conflicts.Contains(placed.FormKey) || !run.Includes(placed.FormKey.ModKey) || !run.Includes(stock.FormKey.ModKey) || !stock.Script.IsNull)
                        throw new InvalidDataException("Shared conflicting, excluded, or scripted stock container: Glass offer retained.");
                    var offer = builder.GlassOffer(assignment.GlassItems, selected.Profile);
                    if (!run.Settings.ReportOnly)
                    {
                        // Keep a separate container for this individually curated offer.
                        var cache = (stock.FormKey, stockProfile, selected.Material);
                                        Container copy;
                        if (containers.TryGetValue(cache, out var existing)) copy = builder.Patch.Containers[existing];
                        else
                        {
                            copy = builder.Patch.Containers.AddNew(); copy.DeepCopyIn(stock); containers[cache] = copy.FormKey;
                        }
                        copy.EditorID = $"BGSO_MerchantOfferStock_{copy.FormKey.ID:X6}";
                        var item = new ContainerItem { Count = 1 }; item.Item.SetTo(offer); copy.Items.Add(item);
                        redirects[placed.FormKey] = copy.FormKey; changes[placed.FormKey] = ["Base"];
                    }
                    status = run.Settings.ReportOnly ? "Planned" : "Modified"; modifiedBranches++;
                }
                catch (InvalidDataException error) { reason = error.Message; retainedBranches++; }
                plans.Add(new { Kind = "Curated Glass offer", Status = status, Reason = reason, Items = assignment.GlassItems,
                    Count = 1, ChancePercent = settings.Profiles[selected.Profile].GlassPercent });
            }
            rows.Add(new { FormKey = npc.FormKey.ToString(), npc.EditorID, Name = npc.Name?.ToString(), SourcePlugin = npc.FormKey.ModKey.ToString(),
                Profile = selected.Profile, selected.Rule, selected.Material, Race = Id(npc.Race.FormKey), RaceMaterial = settings.RaceMaterials.GetValueOrDefault(Id(npc.Race.FormKey), ""), selected.Evidence, Services = services,
                ServiceOnly = !saleServices && places.All(p => p.MerchantContainer.IsNull),
                Locations = places.Select(p => cells.GetValueOrDefault(p.FormKey)).OfType<ICellGetter>().DistinctBy(c => c.FormKey)
                    .Select(c => new { FormKey = c.FormKey.ToString(), c.EditorID, Name = c.Name?.ToString() }).ToArray(),
                Spells = npc.Spells.Select(s => s.FormKey.ToString()).ToArray(), SpellPolicy = "Fixed offered spells retained; skill requirements unchanged.",
                Settings = settings.Profiles[selected.Profile], Plans = plans });
        }
        // Audit stock-changing and player-level expressions without rewriting quest or dialogue behavior.
        var attachedScripts = merchants.Select(n => n.Script.FormKey).Where(k => !k.IsNull).ToHashSet();
        var scriptRows = scripts.Where(s => attachedScripts.Contains(s.Owner.FormKey) ||
            s.Fields.EnumerateFormLinks().Any(l => related.Contains(l.FormKey)) ||
            s.Owner is IDialogItemGetter && s.Owner.EnumerateFormLinks().Any(l => related.Contains(l.FormKey))).Select(s => new
        {
            Record = s.Owner.FormKey.ToString(), s.Owner.EditorID, s.Context,
            Fingerprint = RewardRecords.Fingerprint(s.Fields), Signals = ScriptDiscovery.Scan(s.Fields.SourceCode),
            StockMutationCandidate = Regex.IsMatch(string.Join("\n", (s.Fields.SourceCode ?? "").Split('\n').Select(l => l.Split(';')[0])),
                @"\b(AddToLeveledList|RemoveFromLeveledList|AddItem|AddItemNS|RemoveItem|RemoveAllItems|AddSpell|RemoveSpell)\b", RegexOptions.IgnoreCase),
            RelatedReferences = (s.Owner is IDialogItemGetter ? s.Owner.EnumerateFormLinks() : s.Fields.EnumerateFormLinks()).Select(l => l.FormKey).Where(related.Contains).Distinct().Select(k => k.ToString()).ToArray(),
            Status = rewards.Scripts.Any(r => r.Record.Equals(s.Owner.FormKey.ToString(), StringComparison.OrdinalIgnoreCase) && r.Context == s.Context && r.Fingerprint == RewardRecords.Fingerprint(s.Fields))
                ? "Reviewed quest reward context; handled by quest rewards module when enabled" : "Audited; script preserved"
        }).ToArray();
        if (!run.Settings.ReportOnly)
        {
            foreach (var list in builder.Patch.LeveledItems) { state.PatchMod.LeveledItems.Add(list); changes[list.FormKey] = ["NewRecord"]; }
            foreach (var container in builder.Patch.Containers) { state.PatchMod.Containers.Add(container); changes[container.FormKey] = ["NewRecord"]; }
            var contexts = state.LoadOrder.PriorityOrder.Where(l => l.Enabled && l.Mod is not null && run.IsInputPlugin(l.ModKey))
                .SelectMany(l => l.Mod!.EnumerateMajorRecordContexts<IPlacedObject, IPlacedObjectGetter>(state.LinkCache))
                .GroupBy(c => c.Record.FormKey).ToDictionary(g => g.Key, g => g.First());
            foreach (var pair in redirects) contexts[pair.Key].GetOrAddAsOverride(state.PatchMod).Base.SetTo(pair.Value);
            state.PatchMod.ModHeader.Stats.NextFormID = builder.Patch.ModHeader.Stats.NextFormID;
        }
        run.WriteReport(".merchant-stock.json", new { run.Settings.ReportOnly, ConfigurationFile = path, Merchants = rows, ScriptAudit = scriptRows,
            Summary = new { Actors = rows.Count, ModifiedBranches = modifiedBranches, RetainedBranches = retainedBranches, RemainingLevelGatedBranches = remainingGates,
                GeneratedLists = builder.Patch.LeveledItems.Count, GeneratedContainers = builder.Patch.Containers.Count },
            Limitations = new[] { "Refresh/reset existing merchant stock to see changes; sold player items are not removed.",
                "Fixed authored sales and carried items are preserved. Protected unique items are excluded from generated random stock.",
                "No prices, item stats, barter gold, spell skill requirements, quest conditions or restock timers are changed.",
                "Scripts are audited, not rewritten. Retained branches and dynamic stock require individual review.",
                "GlassPercent is a chance per root selection before nested empty chances; multiple native rolls increase shop-wide availability.",
                "Race preference weights existing source-family Orcish/Elven choices; it does not invent stock absent from that family." } }, MerchantConfiguration.Options);
        run.Log($"Merchant stock: {rows.Count} service actors, {modifiedBranches} static branches, {retainedBranches} retained branches.");
        return changes;
    }
}
