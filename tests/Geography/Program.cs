using System.Text.Json;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Modules;

using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
var settings = GeographicConfiguration.Load("BaldursGateStyleOblivion/geography.json");
var inherited = new GeographicDefinition { Category = LocationCategory.Severe, Reason = "Inherited range" };
var effective = GeographicConfiguration.ResolveRange(settings, inherited);
Check(effective.MinimumTier == 3 && effective.MaximumTier == 6 && inherited.MinimumTier is null, "Category defaults resolve without converting inheritance to a custom range");
settings.CategoryDefaults[LocationCategory.Severe] = new(5, 8);
Check(GeographicConfiguration.ResolveRange(settings, inherited).MinimumTier == 5, "Changing defaults must update inherited ranges");
var custom = new GeographicDefinition { Category = LocationCategory.Severe, MinimumTier = 2, MaximumTier = 6, Reason = "Custom range" };
Check(GeographicConfiguration.ResolveRange(settings, custom).MinimumTier == 2, "Custom ranges must override category defaults");
Check(GeographicConfiguration.ResolveRange(settings, new() { Category = LocationCategory.Special }).MinimumTier is null, "Special stays handcrafted by default");
var road = GeographicConfiguration.Select(settings, "000001:Test.esp", "Test.esp", "Unnamed exterior", "Tamriel", "", ["Exterior", "NearRoad"]);
Check(road.Definition.Category == LocationCategory.LowDanger, "Road geometry should provide a low-danger proposal");
var outskirts = GeographicConfiguration.Select(settings, "000002:Test.esp", "Test.esp", "Unnamed exterior", "Tamriel", "", ["Exterior", "NearSettlement"]);
Check(outskirts.Definition.Category == LocationCategory.LowDanger, "Settlement proximity should improve the exterior fallback");
var hostile = GeographicConfiguration.Select(settings, "000002:Test.esp", "Test.esp", "Unnamed exterior", "Tamriel", "", ["Exterior", "NearSettlement", "NearRoad", "Undead", "Actor:Undead"]);
Check(hostile.Definition.Category == LocationCategory.Dangerous, "Hostile evidence must retain priority over nearby settlement and road signals");
var ruin = GeographicConfiguration.Select(settings, "000001:Test.esp", "Test.esp", "Unknown ruin", "", "", ["Interior", "Entrance:ElvenRuin"]);
Check(ruin.Definition.Category == LocationCategory.Dangerous, "Native ElvenRuin entrance signal must be recognized");
settings.FormKeyOverrides["000001:Test.esp"] = new() { Category = LocationCategory.Safe, MinimumTier = 0, MaximumTier = 1, Reason = "Personal decision" };
Check(GeographicConfiguration.Select(settings, "000001:Test.esp", "Test.esp", "Unknown ruin", "", "", ["Interior", "Entrance:ElvenRuin"]).Definition.Category == LocationCategory.Safe, "Explicit cell decision wins over archetype evidence");
settings.FormKeyOverrides.Clear();
settings.Groups.Add(new() { Name = "Mod-specific", SourcePlugin = "Test.esp", Signal = "NearRoad", Category = LocationCategory.Special, Reason = "Scripted ambush" });
Check(GeographicConfiguration.Select(settings, "000001:Test.esp", "Test.esp", "Unnamed", "Tamriel", "", ["NearRoad"]).Rule == "Mod-specific", "Plugin-specific rule wins over ordinary groups");
settings.FormKeyOverrides["000001:Test.esp"] = new() { MinimumTier = 6, MaximumTier = 2, Reason = "Invalid" };
try { GeographicConfiguration.Validate(settings); throw new Exception("Invalid range accepted"); } catch (ArgumentException) { }
if (args.Length == 1)
{
    using var document = JsonDocument.Parse(File.ReadAllText(args[0])); var root = document.RootElement;
    Check(root.GetProperty("ProposalOnly").GetBoolean() && root.GetProperty("ModifiedRecords").GetInt32() == 0, "Geography must remain observational");
    var locations = root.GetProperty("Locations").EnumerateArray().ToArray();
    Check(locations.Select(row => row.GetProperty("FormKey").GetString()).Distinct().Count() == locations.Length, "Location keys must be unique");
    Check(locations.Count(row => row.GetProperty("OriginalPools").GetArrayLength() > 0) > 1000, "General placed list references must be mapped, not just directly placed actors");
    var tamriel = locations.Where(row => !row.GetProperty("Interior").GetBoolean() && row.GetProperty("Worldspace").ValueKind == JsonValueKind.Object && row.GetProperty("Worldspace").GetProperty("EditorID").GetString() == "Tamriel").ToArray();
    Check(tamriel.Count(row => row.GetProperty("Signals").EnumerateArray().Any(signal => signal.GetString() == "NearRoad")) > 100, "Header-only DLC overrides must not hide Tamriel road children");
    Check(tamriel.Any(row => row.GetProperty("RoadSourcePlugin").GetString() == "Oblivion.esm"), "Road evidence must identify the actual child-record source");
    Check(tamriel.Count(row => row.GetProperty("Signals").EnumerateArray().Any(signal => signal.GetString() == "NearSettlement")) > 100, "Settlement proximity must cover surrounding exterior cells");
    var vindasel = locations.Single(row => row.GetProperty("EditorID").GetString() == "Vindasel");
    Check(vindasel.GetProperty("Actors").EnumerateArray().Any(actor => actor.GetProperty("EditorID").GetString() == "Umbra"), "Vindasel must include its guardian");
    Check(vindasel.GetProperty("EntranceMarkers").EnumerateArray().Any(marker => marker.GetProperty("Types").EnumerateArray().Any(type => type.GetString() == "ElvenRuin")), "Door-linked native ruin markers must reach interior cells");
    Console.WriteLine($"Geographic report checked: {locations.Length} cells, general encounter-list placements, guardian and ruin evidence, no geographic modifications.");
}
Console.WriteLine("Geographic rules passed: road and native ruin signals, explicit and plugin precedence, invalid range rejection.");

var native = new OblivionMod(ModKey.FromNameAndExtension("GeoTest.esp"), OblivionRelease.Oblivion);
var low = native.Creatures.AddNew(); var high = native.Creatures.AddNew();
var actorProfiles = new Dictionary<FormKey, ActorProfile>();
foreach (var (actor, tier) in new[] { (low, 2), (high, 5) })
{ var profile = new ActorProfile(); profile.Apply(new ActorValues { PowerTier = tier }, "Test", "Test"); actorProfiles[actor.FormKey] = profile; }
LeveledCreatureEntry Entry(FormKey key)
{ var entry = new LeveledCreatureEntry { Level = 1, Count = 2, Unknown = 7, Unknown2 = 9 }; entry.Reference.SetTo(key); return entry; }
var pool = new LeveledCreature(native.GetNextFormKey(), OblivionRelease.Oblivion) { Entries = new([Entry(low.FormKey), Entry(high.FormKey)]) }; native.LeveledCreatures.Add(pool);
pool.ChanceNone = new Percent(0.25); pool.Flags = LeveledFlag.CalculateForEachItemInCount;
var records = native.EnumerateMajorRecords().ToDictionary(record => record.FormKey, record => (IMajorRecordGetter)record);
var builder = new GeographicEncounterPools(records, actorProfiles, _ => true, [], ModKey.FromNameAndExtension("GeoPatch.esp"), 0x800);
var roadside = builder.Build(pool.FormKey, 1, 3); var dangerous = builder.Build(pool.FormKey, 4, 6);
Check(roadside != dangerous && roadside != pool.FormKey, "Different ranges need private geographic pools");
var weighted = builder.Patch.LeveledCreatures[roadside];
Check(weighted.Entries!.Count(entry => entry.Reference.FormKey == low.FormKey) == 3 && weighted.Entries.Count(entry => entry.Reference.FormKey == high.FormKey) == 1, "In-range preference must retain out-of-range actors");
Check(weighted.ChanceNone == pool.ChanceNone && weighted.Flags == pool.Flags && weighted.Entries.All(entry => entry.Count == 2 && entry.Unknown == 7 && entry.Unknown2 == 9), "Native spawn counts, no-spawn chance, flags and entry metadata must survive");
Check(pool.Entries!.Count == 2 && builder.Build(pool.FormKey, 1, 3) == roadside, "Original shared list must stay untouched; equal ranges reuse copies");
Check(builder.Build(pool.FormKey, 0, 9) == pool.FormKey, "Uniform weighting must not allocate a needless copy");
var parent = new LeveledCreature(native.GetNextFormKey(), OblivionRelease.Oblivion) { Entries = new([Entry(pool.FormKey)]) }; records[parent.FormKey] = parent;
var nested = builder.Build(parent.FormKey, 1, 3);
Check(builder.Patch.LeveledCreatures[nested].Entries![0].Reference.FormKey == roadside, "Nested private pools must be reused without flattening native counts");
var preserved = new GeographicEncounterPools(records, actorProfiles, _ => true, [pool.FormKey], ModKey.FromNameAndExtension("CuratedPatch.esp"), 0x800);
try { preserved.Build(parent.FormKey, 1, 3); throw new Exception("Curated dependency accepted"); } catch (InvalidDataException) { }
Check(preserved.Patch.LeveledCreatures.Count == 0, "Rejected branches must not allocate orphan pools");
var excluded = new GeographicEncounterPools(records, actorProfiles, key => key != high.FormKey, [], ModKey.FromNameAndExtension("ExcludedPatch.esp"), 0x800);
try { excluded.Build(pool.FormKey, 1, 3); throw new Exception("Excluded actor accepted"); } catch (InvalidDataException) { }
pool.Flags = LeveledFlag.UseAll;
var useAll = new GeographicEncounterPools(records, actorProfiles, _ => true, [], ModKey.FromNameAndExtension("AllPatch.esp"), 0x800);
try { useAll.Build(pool.FormKey, 1, 3); throw new Exception("UseAll accepted"); } catch (InvalidDataException) { }
Console.WriteLine("Geographic pools passed: preference, shared-list isolation, native count/chance preservation, reuse and UseAll guard.");

if (args.Length >= 3)
{
    using var report = JsonDocument.Parse(File.ReadAllText(args[0]));
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    using var baseline = OblivionMod.CreateFromBinaryOverlay(args[2], OblivionRelease.Oblivion);
    var placed = patch.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().ToDictionary(record => record.FormKey);
    var plans = report.RootElement.GetProperty("Encounters").EnumerateArray().Where(row => row.GetProperty("Status").GetString() == "Modified").ToArray();
    foreach (var plan in plans)
    {
        var key = FormKey.Factory(plan.GetProperty("Placement").GetString()!);
        var plannedKey = FormKey.Factory(plan.GetProperty("PlannedPool").GetString()!); var target = new FormKey(patch.ModKey, plannedKey.ID);
        Check(placed[key].Base.FormKey == target && patch.LeveledCreatures.ContainsKey(target), "Written placement must reference its generated pool");
    }
    var originalLists = baseline.LeveledCreatures.ToDictionary(list => list.FormKey);
    foreach (var pair in originalLists)
        Check(patch.LeveledCreatures.TryGetValue(pair.Key, out var current) && CreaturePoolBuilder.SameEntries(pair.Value.Entries ?? [], current.Entries ?? []) && pair.Value.Flags == current.Flags, "Existing Phase 4 pools must remain unchanged");
    Check(patch.Npcs.Count == baseline.Npcs.Count && patch.Creatures.Count == baseline.Creatures.Count, "Geography must not add or remove actor overrides");
    if (args.Length == 4)
    {
        var inputs = baseline.MasterReferences.Select(master => OblivionMod.CreateFromBinaryOverlay(Path.Combine(args[3], master.Master.ToString()), OblivionRelease.Oblivion)).ToArray();
        try
        {
            var cells = inputs.Reverse().SelectMany(mod => mod.EnumerateMajorRecords().OfType<ICellGetter>())
                .GroupBy(cell => cell.FormKey).ToDictionary(group => group.Key, group => group.First());
            foreach (var cell in patch.EnumerateMajorRecords().OfType<ICellGetter>())
                if (cells.TryGetValue(cell.FormKey, out var source))
                    Check(cell.Name == source.Name && cell.EditorID == source.EditorID && cell.Flags == source.Flags && cell.Grid == source.Grid,
                        $"Containing cell header must retain the winning input: {cell.FormKey}");
        }
        finally { foreach (var input in inputs) input.Dispose(); }
    }
    Console.WriteLine($"Written geographic patch checked: {plans.Length} redirects resolve; existing actor and creature pool sets retained.");
}

if (args.Length == 2)
{
    using var report = JsonDocument.Parse(File.ReadAllText(args[0]));
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    Check(report.RootElement.GetProperty("ReportOnly").GetBoolean() && report.RootElement.GetProperty("ModifiedRecords").GetInt32() == 0,
        "Dry run must report zero modifications");
    Check(!patch.EnumerateMajorRecords().Any(), "Report-only pipeline ESP must contain no gameplay records");
    Console.WriteLine("Report-only binary checked: planned encounters with zero applied records.");
}
