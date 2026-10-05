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
    var originalLists = baseline.LeveledCreatures.Where(list => !(list.EditorID ?? "").StartsWith("BGSO_Geo_", StringComparison.Ordinal)).ToDictionary(list => list.FormKey);
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

if (args.Length == 2 && args[0] != "--storyline" && args[0] != "--realm")
{
    using var report = JsonDocument.Parse(File.ReadAllText(args[0]));
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    Check(report.RootElement.GetProperty("ReportOnly").GetBoolean() && report.RootElement.GetProperty("ModifiedRecords").GetInt32() == 0,
        "Dry run must report zero modifications");
    Check(!patch.EnumerateMajorRecords().Any(), "Report-only pipeline ESP must contain no gameplay records");
    Console.WriteLine("Report-only binary checked: planned encounters with zero applied records.");
}

var dungeons = DungeonConfiguration.Load("BaldursGateStyleOblivion/dungeons.json");
Check(dungeons.Groups.All(group => group.BossTierModifier == 0 && group.SpecialEncounterChance is null), "No automatic boss boost or rare injection");
var dungeonPool = new LeveledCreature(native.GetNextFormKey(), OblivionRelease.Oblivion) { Entries = new([Entry(low.FormKey), Entry(high.FormKey)]), ChanceNone = new Percent(0.25) };
records[dungeonPool.FormKey] = dungeonPool;
var dungeonBuilder = new GeographicEncounterPools(records, actorProfiles, _ => true, [], ModKey.FromNameAndExtension("DungeonPatch.esp"), 0x800);
var capped = dungeonBuilder.Build(dungeonPool.FormKey, 1, 3, 3);
Check(dungeonBuilder.Patch.LeveledCreatures[capped].Entries!.Count(entry => entry.Reference.FormKey == low.FormKey) == 4 && dungeonBuilder.Patch.LeveledCreatures[capped].Entries!.Count(entry => entry.Reference.FormKey == high.FormKey) == 1, "Dungeon ordinary weighting must favor actors below the cap");
var large = new LeveledCreature(native.GetNextFormKey(), OblivionRelease.Oblivion) { Entries = new(Enumerable.Range(0,100).Select(index => {var entry=Entry(index<80?low.FormKey:high.FormKey);entry.Count=1;return entry;})), ChanceNone = new Percent(0.25) };
records[large.FormKey] = large;
var compressed = dungeonBuilder.Build(large.FormKey, 1, 3, 3);
ILeveledCreatureGetter? Lookup(FormKey key) => dungeonBuilder.Patch.LeveledCreatures.TryGetValue(key, out var list) ? list : records.GetValueOrDefault(key) as ILeveledCreatureGetter;
var probabilities = CreatureListDeleveling.Probabilities(compressed, 1, Lookup, [])!;
Check(probabilities[low.FormKey.ToString()]/0.75 > 0.9 && probabilities[high.FormKey.ToString()] > 0 && dungeonBuilder.Patch.LeveledCreatures.All(list=>list.Entries!.Count<=255), "Large tapered pools must retain weaker dominance and exceptions within engine limits");
var rarePool = dungeonBuilder.BuildSpecial(large.FormKey, 1, 3, 0.05);
var rareOdds = CreatureListDeleveling.Probabilities(rarePool, 40, Lookup, [])!;
Check(Math.Abs(rareOdds[high.FormKey.ToString()]/0.75-0.05)<1e-8 && Math.Abs(rareOdds["None"]-0.25)<1e-8, "Explicit rare chance must be 5% among non-empty outcomes and preserve native no-spawn chance");
Check(dungeonBuilder.BuildSpecial(large.FormKey,1,3,0.05)==rarePool,"Repeated dungeon rare policy must reuse its pools");
var roomA = new Cell(native.GetNextFormKey(), OblivionRelease.Oblivion) { EditorID="ExampleCave01",Flags=Cell.Flag.IsInteriorCell };
var roomB = new Cell(native.GetNextFormKey(), OblivionRelease.Oblivion) { EditorID="ExampleCave02",Flags=Cell.Flag.IsInteriorCell };
var rooms = new Dictionary<FormKey,ICellGetter>{{roomA.FormKey,roomA},{roomB.FormKey,roomB}};
var connections = new Dictionary<FormKey,FormKey[]> {{roomA.FormKey,[roomA.FormKey,roomB.FormKey]},{roomB.FormKey,[roomA.FormKey,roomB.FormKey]}};
var groupingSettings = new DungeonSettings { Groups = [new() { Match = "Cave", BasePowerTier = 3 }] };
var inferred = DungeonProfiles.BuildSites(groupingSettings,connections,rooms,new Dictionary<FormKey,List<IPlacedObjectGetter>>());
Check(inferred[roomA.FormKey]==inferred[roomB.FormKey]&&inferred[roomA.FormKey].Profile.BasePowerTier==3,"Connected cave rooms must share the cap-3 profile");
groupingSettings.FormKeyOverrides[roomB.FormKey.ToString()]=new(){Cells=[roomB.FormKey.ToString()],BasePowerTier=2};
var split = DungeonProfiles.BuildSites(groupingSettings,connections,rooms,new Dictionary<FormKey,List<IPlacedObjectGetter>>());
Check(split[roomA.FormKey]!=split[roomB.FormKey]&&split[roomB.FormKey].Profile.BasePowerTier==2,"Manual room membership must split an inferred site");
Console.WriteLine("Dungeon checks passed: conservative defaults, tapered common enemies, large pools, explicit rare odds and manual grouping.");

// Admission-quest pools must affect only the reviewed placements, never their shared list.
var questMod = new OblivionMod(ModKey.FromNameAndExtension("Oblivion.esm"), OblivionRelease.Oblivion);
var questImp = new Creature(FormKey.Factory("01E649:Oblivion.esm"), OblivionRelease.Oblivion);
var questTroll = new Creature(FormKey.Factory("002DBC:Oblivion.esm"), OblivionRelease.Oblivion);
var questOgre = new Creature(FormKey.Factory("00C20D:Oblivion.esm"), OblivionRelease.Oblivion);
foreach (var actor in new[] { questImp, questTroll, questOgre }) questMod.Creatures.Add(actor);
var questPool = new LeveledCreature(FormKey.Factory("0340B1:Oblivion.esm"), OblivionRelease.Oblivion)
{ Entries = new(), ChanceNone = new Percent(0.25), Flags = LeveledFlag.CalculateForEachItemInCount };
foreach (var actor in new[] { questImp, questTroll, questOgre })
{ var entry = Entry(actor.FormKey); entry.Count = 1; entry.Level = 8; questPool.Entries.Add(entry); }
questMod.LeveledCreatures.Add(questPool);
var questRecords = questMod.EnumerateMajorRecords().ToDictionary(record => record.FormKey, record => (IMajorRecordGetter)record);
var questBuilder = new GeographicEncounterPools(questRecords, new Dictionary<FormKey, ActorProfile>(), _ => true, [], ModKey.FromNameAndExtension("QuestPatch.esp"), 0x800);
var questCell = FormKey.Factory("03379A:Oblivion.esm");
var questSpawn = new PlacedObject(FormKey.Factory("03CD6C:Oblivion.esm"), OblivionRelease.Oblivion); questSpawn.Base.SetTo(questPool.FormKey);
Check(questBuilder.TryBuildLeyawiinPool(questCell, questSpawn, out var questTarget), "Reviewed Leyawiin spawn must receive its private pool");
var questResult = questBuilder.Patch.LeveledCreatures[questTarget];
Check(questResult.Entries!.Count == 10 && questResult.Entries.Count(entry => entry.Reference.FormKey == questImp.FormKey) == 9 &&
    questResult.Entries.Count(entry => entry.Reference.FormKey == questTroll.FormKey) == 1, "Admission pool must be 90% imps / 10% trolls");
Check(questResult.ChanceNone == questPool.ChanceNone && questResult.Flags == questPool.Flags &&
    questResult.Entries.All(entry => entry.Level == 1 && entry.Count == 1 && entry.Unknown == 7 && entry.Unknown2 == 9), "Static quest pool must retain spawn chance and entry metadata");
var secondSpawn = new PlacedObject(FormKey.Factory("03EE88:Oblivion.esm"), OblivionRelease.Oblivion); secondSpawn.Base.SetTo(questPool.FormKey);
Check(questBuilder.TryBuildLeyawiinPool(questCell, secondSpawn, out var secondTarget) && secondTarget == questTarget && questBuilder.Patch.LeveledCreatures.Count == 1, "Both reviewed spawns reuse one private pool");
Check(!questBuilder.TryBuildLeyawiinPool(FormKey.Factory("000001:Oblivion.esm"), questSpawn, out _) && questPool.Entries.Count == 3, "Other locations and the shared original list must stay intact");
var unrelatedSpawn = new PlacedObject(FormKey.Factory("000002:Oblivion.esm"), OblivionRelease.Oblivion); unrelatedSpawn.Base.SetTo(questPool.FormKey);
Check(!questBuilder.TryBuildLeyawiinPool(questCell, unrelatedSpawn, out _), "Unreviewed placements in the same room must stay intact");
var questExcluded = new GeographicEncounterPools(questRecords, new Dictionary<FormKey, ActorProfile>(), key => key != questTroll.FormKey, [], ModKey.FromNameAndExtension("ExcludedQuest.esp"), 0x800);
try { questExcluded.TryBuildLeyawiinPool(questCell, questSpawn, out _); throw new Exception("Excluded quest actor accepted"); } catch (InvalidDataException) { }
Check(questExcluded.Patch.LeveledCreatures.Count == 0, "Rejected quest exceptions must not allocate orphan pools");
var questCurated = new GeographicEncounterPools(questRecords, new Dictionary<FormKey, ActorProfile>(), _ => true, [questPool.FormKey], ModKey.FromNameAndExtension("CuratedQuest.esp"), 0x800);
try { questCurated.TryBuildLeyawiinPool(questCell, questSpawn, out _); throw new Exception("Curated quest pool accepted"); } catch (InvalidDataException) { }
questSpawn.Base.SetTo(FormKey.Factory("000003:Oblivion.esm"));
Check(!questBuilder.TryBuildLeyawiinPool(questCell, questSpawn, out _), "Mod-replaced encounter identity must stay intact");
Console.WriteLine("Storyline exception checks passed: private weighting, exact placement scope, shared-list isolation and manual/exclusion guards.");

if (args.Length == 2 && args[0] == "--storyline")
{
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    foreach (var id in new uint[] { 0x036627, 0x03662D, 0x036635 })
    {
        var actor = patch.Creatures[FormKey.Factory($"{id:X6}:Oblivion.esm")];
        Check(actor.Configuration!.LevelOffset == 5 && actor.Configuration.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset) == false, "Written starving lions must be fixed level 5");
    }
    var placements = patch.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().ToDictionary(record => record.FormKey);
    var root = patch.LeveledCreatures.Single(list => list.EditorID == "BGSO_LeyawiinRecommendation");
    foreach (var id in new uint[] { 0x03CD6C, 0x03EE88 })
        Check(placements[FormKey.Factory($"{id:X6}:Oblivion.esm")].Base.FormKey == root.FormKey, "Written quest placements must resolve to the private pool");
    foreach (var level in new[] { 1, 10, 25, 40 })
    {
        var chances = CreatureListDeleveling.Probabilities(root.FormKey, level, key => patch.LeveledCreatures.TryGetValue(key, out var list) ? list : null, []);
        Check(chances is not null && chances.Count == 2 && Math.Abs(chances[questImp.FormKey.ToString()] - 0.9) < 0.000001 &&
            Math.Abs(chances[questTroll.FormKey.ToString()] - 0.1) < 0.000001, "Written encounter probabilities must be static at every tested player level");
    }
    Check(patch.LeveledCreatures[questPool.FormKey].Entries!.Count > 10, "Shared mythic pool must retain its broader membership");
    Console.WriteLine("Written storyline patch verified: three fixed level 5 lions, two redirected quest placements, static 90/10 probabilities and shared pool retained.");
}

var apexDaedra = native.Creatures.AddNew(); apexDaedra.Name = "Strong Daedra";
low.Name = "Small Daedra"; high.Name = "Veteran Daedra";
var apexProfile = new ActorProfile(); apexProfile.Apply(new ActorValues { PowerTier = 6 }, "Test", "Test"); actorProfiles[apexDaedra.FormKey] = apexProfile;
records[apexDaedra.FormKey] = apexDaedra;
var realmScript = native.Scripts.AddNew(); realmScript.Fields.SourceCode = "scn Realm\nbegin OnDeath\nend"; records[realmScript.FormKey] = realmScript;
var realmRoot = new LeveledCreature(native.GetNextFormKey(), OblivionRelease.Oblivion) { Entries = new() }; native.LeveledCreatures.Add(realmRoot); realmRoot.EditorID = "LL1Daedra100"; realmRoot.ChanceNone = new Percent(0.25);
realmRoot.Script.SetTo(realmScript.FormKey); realmRoot.Template.SetTo(low.FormKey);
foreach (var actor in new[] { low, high, apexDaedra }) { var entry = Entry(actor.FormKey); entry.Count = 1; realmRoot.Entries.Add(entry); }
records[realmRoot.FormKey] = realmRoot;
var realmSettings = new CreatureListSettings(); realmSettings.ReviewedScripts[realmScript.FormKey.ToString()] = CreaturePoolBuilder.ScriptFingerprint(realmScript);
var realmBuilder = new GeographicEncounterPools(records, actorProfiles, _ => true, [], ModKey.FromNameAndExtension("RealmPatch.esp"), 0x800);
var earlyRealm = RealmEncounters.Select("OblivionMQKvatchTower", null, realmRoot.EditorID)!;
var lateRealm = RealmEncounters.Select("MQ14OblivionTower", null, realmRoot.EditorID)!;
var earlyKey = realmBuilder.BuildRealm(realmRoot.FormKey, earlyRealm, realmSettings);
var lateKey = realmBuilder.BuildRealm(realmRoot.FormKey, lateRealm, realmSettings);
ILeveledCreatureGetter? RealmList(FormKey key) => realmBuilder.Patch.LeveledCreatures.TryGetValue(key, out var list) ? list : records.GetValueOrDefault(key) as ILeveledCreatureGetter;
foreach (var level in new[] { 1, 10, 25, 40 })
{
    var earlyOdds = CreatureListDeleveling.Probabilities(earlyKey, level, RealmList, [])!;
    var lateOdds = CreatureListDeleveling.Probabilities(lateKey, level, RealmList, [])!;
    Check(Math.Abs(earlyOdds[low.FormKey.ToString()] / 0.75 - 0.90) < 1e-8 && Math.Abs(earlyOdds[apexDaedra.FormKey.ToString()] / 0.75 - 0.01) < 1e-8, "Kvatch must retain 90/9/1 static bands");
    Check(Math.Abs(lateOdds[high.FormKey.ToString()] / 0.75 - 0.70) < 1e-8 && Math.Abs(lateOdds[apexDaedra.FormKey.ToString()] / 0.75 - 0.20) < 1e-8, "Late gates must favor strong bands");
}
Check(earlyKey != lateKey && earlyKey != realmRoot.FormKey && realmRoot.Entries.Count == 3, "Different realm profiles need isolated pools without shared overrides");
var realmCopy = realmBuilder.Patch.LeveledCreatures[earlyKey];
Check(realmCopy.Script.FormKeyNullable == realmRoot.Script.FormKeyNullable && realmCopy.Template.FormKeyNullable == realmRoot.Template.FormKeyNullable && realmCopy.ChanceNone == realmRoot.ChanceNone, "Reviewed realm scripts, templates and Chance None must survive");
Check(realmBuilder.Patch.LeveledCreatures.All(list => list.FormKey.ModKey == realmBuilder.Patch.ModKey && list.Entries!.All(entry => entry.Count == 1 && entry.Unknown == 7 && entry.Unknown2 == 9)), "Realm helpers must retain counts and metadata, with no source-list overrides");
Check(realmBuilder.BuildRealm(realmRoot.FormKey, earlyRealm, realmSettings) == earlyKey, "Repeated realm profiles must reuse private pools");
realmScript.Fields.SourceCode += "\n; changed";
var changedRealm = new GeographicEncounterPools(records, actorProfiles, _ => true, [], ModKey.FromNameAndExtension("ChangedRealm.esp"), 0x800);
try { changedRealm.BuildRealm(realmRoot.FormKey, earlyRealm, realmSettings); throw new Exception("Changed realm script accepted"); } catch (InvalidDataException) { }
Check(changedRealm.Patch.LeveledCreatures.Count == 0, "Rejected scripts must not leave partial realm pools");
Check(RealmEncounters.Select("ParadiseGrotto01", null, realmRoot.EditorID) is null && RealmEncounters.Select("OrdinaryCave", "Tamriel", realmRoot.EditorID) is null, "Paradise and ordinary Tamriel locations must retain existing policies");
Console.WriteLine("Realm checks passed: early/late static weights, private isolation, script fingerprints, templates, counts, metadata and no-spawn chance.");

if (args.Length == 2 && args[0] == "--realm")
{
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    var reportPath = Path.Combine(Path.GetDirectoryName(args[1])!, "Reports", "BaldursGateStyleOblivion.geographic-encounters.json");
    using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
    var rows = report.RootElement.GetProperty("Encounters").EnumerateArray().Where(row =>
        row.GetProperty("Rule").GetString() is "Kvatch early invasion" or "Kvatch guardian" or "Ordinary Oblivion realm" or "Late Main Quest invasion").ToArray();
    Check(rows.Length > 500, "Realm coverage must include ordinary and story gates");
    var actualPlacements = patch.EnumerateMajorRecords().OfType<IPlacedObjectGetter>().ToDictionary(record => record.FormKey);
    var inputs = patch.MasterReferences.Select(master => OblivionMod.CreateFromBinaryOverlay(Path.Combine(@"F:\SteamLibrary\steamapps\common\Oblivion\Data", master.Master.ToString()), OblivionRelease.Oblivion)).ToArray();
    try
    {
        var sourceLists = inputs.Reverse().SelectMany(mod => mod.LeveledCreatures).GroupBy(list => list.FormKey).ToDictionary(group => group.Key, group => group.First());
        ILeveledCreatureGetter? ActualList(FormKey key) => patch.LeveledCreatures.TryGetValue(key, out var current) ? current : sourceLists.GetValueOrDefault(key);
        var checkedStatic = new HashSet<FormKey>();
        bool StaticBranch(FormKey key, HashSet<FormKey> path)
        {
            if (checkedStatic.Contains(key) || ActualList(key) is not { } list) return true;
            if (!path.Add(key) || list.Flags?.HasFlag(LeveledFlag.UseAll) == true) return false;
            var result = list.Entries!.All(entry => entry.Level <= 1 && StaticBranch(entry.Reference.FormKey, path));
            path.Remove(key);
            if (result) checkedStatic.Add(key);
            return result;
        }
        foreach (var row in rows.Where(row => row.GetProperty("Status").GetString() == "Modified"))
        {
            var placedKey = FormKey.Factory(row.GetProperty("Placement").GetString()!);
            var plannedKey = FormKey.Factory(row.GetProperty("PlannedPool").GetString()!);
            var targetKey = new FormKey(patch.ModKey, plannedKey.ID);
            var current = ActualList(targetKey)!;
            var source = ActualList(FormKey.Factory(row.GetProperty("OriginalPool").GetString()!))!;
            Check(actualPlacements[placedKey].Base.FormKey == targetKey && current.FormKey.ModKey == patch.ModKey, "Realm placement must point to its written private pool");
            Check(current.Script.FormKeyNullable == source.Script.FormKeyNullable && current.Template.FormKeyNullable == source.Template.FormKeyNullable && current.ChanceNone == source.ChanceNone, "Written realm root must preserve scripts, templates and no-spawn chance");
            Check(StaticBranch(targetKey, []), "Written realm branches must have no player-level gates");
        }
    }
    finally { foreach (var input in inputs) input.Dispose(); }
    Console.WriteLine($"Written realm plugin verified: {rows.Length} reviewed placements; private references, script/template/chance preservation and level-independent selection.");
}
