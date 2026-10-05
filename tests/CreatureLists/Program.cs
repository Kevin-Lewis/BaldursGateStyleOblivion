using System.Text.Json;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var original = new OblivionMod(ModKey.FromNameAndExtension("Test.esp"), OblivionRelease.Oblivion);
var patchKey = ModKey.FromNameAndExtension("Patch.esp");
var profiles = new Dictionary<FormKey, ActorProfile>();
Creature Actor(string name, int tier)
{
    var actor = original.Creatures.AddNew(); actor.Name = name;
    var profile = new ActorProfile(); profile.Apply(new ActorValues { PowerTier = tier }, "Test", "Test"); profiles[actor.FormKey] = profile;
    return actor;
}
LeveledCreatureEntry Entry(FormKey reference, short level = 1, short count = 1)
{
    var entry = new LeveledCreatureEntry { Level = level, Count = count, Unknown = 7, Unknown2 = 9 }; entry.Reference.SetTo(reference); return entry;
}
LeveledCreature List(string id, params LeveledCreatureEntry[] entries)
{
    var list = new LeveledCreature(original.GetNextFormKey(), OblivionRelease.Oblivion) { EditorID = id, Entries = new(entries), Flags = LeveledFlag.CalculateForEachItemInCount, ChanceNone = new Percent(0.25) };
    original.LeveledCreatures.Add(list); return list;
}
CreaturePoolBuilder Build(CreatureListSettings settings, HashSet<FormKey>? included = null)
{
    CreatureListConfiguration.Validate(settings);
    var records = original.EnumerateMajorRecords().ToDictionary(record => record.FormKey, record => (IMajorRecordGetter)record);
    var builder = new CreaturePoolBuilder(records, profiles, settings, patchKey, 0x800, included ?? original.LeveledCreatures.Select(list => list.FormKey).ToHashSet());
    builder.Plan(); return builder;
}
var weak = Actor("Common", 2); var strong = Actor("Strong", 4); var rare = Actor("Rare", 7);
var variantActors = Enumerable.Range(0, 10).Select(_ => Actor("Common", 2)).ToArray();
var root = List("LL1Wildlife", variantActors.Select(actor => Entry(actor.FormKey)).Concat([Entry(strong.FormKey, 10), Entry(rare.FormKey, 25)]).ToArray());
var settings = new CreatureListSettings { Groups = [new() { Name = "Wildlife", Match = "^LL1Wildlife", Policy = CreatureListPolicy.WeightedPool }] };
var builder = Build(settings);
var weighted = builder.GetList(root.FormKey)!;
Check(builder.Accepted.Contains(root.FormKey) && weighted.Entries!.Count == 100, "Weighted pool must contain 100 tickets");
Check(weighted.ChanceNone == root.ChanceNone && weighted.Flags!.Value.HasFlag(LeveledFlag.CalculateForEachItemInCount), "Native chance and per-count flags must survive");
Check(root.Entries!.Any(entry => entry.Level > 1), "Incoming records must not be mutated");
var odds = CreatureListDeleveling.Probabilities(root.FormKey, 1, builder.GetList, [])!;
Check(Math.Abs(odds.Values.Sum() - 1) < 1e-10, "Probabilities must sum to one");
var commonChance = variantActors.Sum(actor => odds.GetValueOrDefault(actor.FormKey.ToString()));
Check(Math.Abs(commonChance / 0.75 - 0.70) < 0.02, "Variant count must not inflate family weight");
Check(Math.Abs(odds[rare.FormKey.ToString()] / 0.75 - 0.05) < 0.02, "Rare encounters must remain rare");
foreach (var level in new[] { 1, 10, 25, 40 })
{
    var current = CreatureListDeleveling.Probabilities(root.FormKey, level, builder.GetList, [])!;
    Check(current.Count == odds.Count && current.All(pair => Math.Abs(pair.Value - odds[pair.Key]) < 1e-10), "Pool must be independent of player level");
}
var repeat = Build(settings);
Check(CreaturePoolBuilder.SameEntries(weighted.Entries, repeat.GetList(root.FormKey)!.Entries!), "Generated IDs and tickets must be deterministic");
var sharedChild = List("SharedChild", Entry(weak.FormKey), Entry(rare.FormKey, 30));
var quest = List("MQTest", Entry(sharedChild.FormKey));
var safe = List("LL1Safe", Entry(sharedChild.FormKey));
settings.Groups.Add(new() { Name = "Safe", Match = "^LL1Safe", Policy = CreatureListPolicy.StaticPool });
builder = Build(settings);
var privateKey = builder.GetList(safe.FormKey)!.Entries![0].Reference.FormKey;
Check(privateKey != sharedChild.FormKey && privateKey.ModKey == patchKey, "Shared child needs a new private FormKey");
Check(builder.GetList(quest.FormKey)!.Entries![0].Reference.FormKey == sharedChild.FormKey && !builder.PlannedPatch.LeveledCreatures.ContainsKey(sharedChild.FormKey), "Quest branch must retain original child");
Check(builder.GetList(privateKey)!.ChanceNone == sharedChild.ChanceNone && builder.GetList(privateKey)!.Entries!.All(entry => entry.Level == 1), "Private copy must preserve chance and remove gates");
var scripted = List("Scripted", Entry(rare.FormKey, 30)); scripted.Script.SetTo(new FormKey(original.ModKey, 0xFFFF));
var unsafeRoot = List("LL1Unsafe", Entry(scripted.FormKey));
settings.Groups.Add(new() { Name = "Unsafe", Match = "^LL1Unsafe", Policy = CreatureListPolicy.StaticPool });
builder = Build(settings); Check(!builder.Accepted.Contains(unsafeRoot.FormKey), "Scripted dependency must stop the entire encounter");
var staticScripted = List("StaticScripted", Entry(weak.FormKey)); staticScripted.Script.SetTo(new FormKey(original.ModKey, 0xFFFF));
var staticScriptRoot = List("LL1UnsafeStaticScript", Entry(staticScripted.FormKey, 20));
builder = Build(settings); Check(!builder.Accepted.Contains(staticScriptRoot.FormKey), "A static entry table does not remove runtime script uncertainty");
var missing = List("LL1Missing", Entry(new FormKey(original.ModKey, 0xFFF0), 30));
settings.Groups.Add(new() { Name = "Missing", Match = "^LL1Missing", Policy = CreatureListPolicy.StaticPool });
builder = Build(settings); Check(!builder.Accepted.Contains(missing.FormKey), "Missing entries must preserve the original");
var cycleA = List("LL1CycleA"); var cycleB = List("LL1CycleB", Entry(cycleA.FormKey)); cycleA.Entries!.Add(Entry(cycleB.FormKey));
settings.Groups.Add(new() { Name = "Cycle", Match = "^LL1Cycle", Policy = CreatureListPolicy.StaticPool });
builder = Build(settings); Check(!builder.Accepted.Contains(cycleA.FormKey), "Cycles must not produce a partial pool");
var curated = List("Manual", Entry(rare.FormKey, 30));
settings.FormKeyOverrides[curated.FormKey.ToString()] = new() { Policy = CreatureListPolicy.CuratedPool, Entries = [new(weak.FormKey.ToString(), 3, 2), new(strong.FormKey.ToString(), 1)] };
builder = Build(settings);
var manual = builder.GetList(curated.FormKey)!;
Check(manual.Entries!.Count == 4 && manual.Entries.Count(entry => entry.Count == 2) == 3, "Manual weight must duplicate entries; Count remains actor quantity");
settings.PluginRules.Add(new() { Name = "Plugin", SourcePlugin = "Test.esp", Match = "^Manual", Policy = CreatureListPolicy.Preserve });
Check(CreatureListConfiguration.Select(settings, curated.FormKey.ToString(), "Manual", "Test.esp").Definition.Policy == CreatureListPolicy.CuratedPool, "FormKey override beats plugin rules");
settings.FormKeyOverrides.Remove(curated.FormKey.ToString());
Check(CreatureListConfiguration.Select(settings, curated.FormKey.ToString(), "Manual", "Test.esp").Rule == "Plugin", "Plugin rule beats group/fallback");
var included = original.LeveledCreatures.Select(list => list.FormKey).Where(key => key != sharedChild.FormKey).ToHashSet();
builder = Build(settings, included); Check(!builder.Accepted.Contains(safe.FormKey), "Excluded plugin dependency must not be transformed");
var templatePool = List("LL1SafeTemplate", Entry(rare.FormKey, 20)); templatePool.Template.SetTo(weak.FormKey);
builder = Build(settings); Check(!builder.Accepted.Contains(templatePool.FormKey), "Templates need individual review");
settings.FormKeyOverrides[templatePool.FormKey.ToString()] = new() { Policy = CreatureListPolicy.StaticPool, AllowSpecial = true };
builder = Build(settings); Check(builder.Accepted.Contains(templatePool.FormKey) && builder.GetList(templatePool.FormKey)!.Template.FormKeyNullable == weak.FormKey, "Reviewed template must remain intact");
var strongProfile = profiles[strong.FormKey]; profiles.Remove(strong.FormKey);
builder = Build(settings); Check(builder.GetList(root.FormKey)!.Entries!.Count == root.Entries!.Count && builder.Reasons[root.FormKey].Contains("incomplete tier"), "Unknown actor tiers must use the original static pool");
profiles[strong.FormKey] = strongProfile;
var useAll = List("LL1SafeUseAll", Entry(rare.FormKey, 30)); useAll.Flags = LeveledFlag.UseAll;
builder = Build(settings); Check(!builder.Accepted.Contains(useAll.FormKey), "UseAll pools are guarded");
Check(CreaturePoolBuilder.AllocateTickets([70,25,5]).Sum() == 100, "Weight tickets must fit native list capacity");
var randomOnly = List("LL1WildlifeRandomOnly", Entry(weak.FormKey), Entry(strong.FormKey), Entry(rare.FormKey));
builder = Build(settings);
Check(builder.GetList(randomOnly.FormKey)!.Entries!.Count == 3 && !builder.ReachableChanges.Contains(randomOnly.FormKey), "Existing random-only pools retain native weights by default");
settings.FormKeyOverrides[randomOnly.FormKey.ToString()] = new() { Policy = CreatureListPolicy.WeightedPool };
builder = Build(settings);
Check(builder.GetList(randomOnly.FormKey)!.Entries!.Count == 100, "Individual weighted override may reweight a random-only pool");
var reviewedScript = original.Scripts.AddNew();
reviewedScript.Fields.SourceCode = "scn Reviewed\nbegin OnDeath\nend"; reviewedScript.Fields.CompiledScript = new byte[] { 1, 2, 3 };
var reviewedPool = List("ReviewedScriptPool", Entry(rare.FormKey, 30)); reviewedPool.Script.SetTo(reviewedScript.FormKey);
settings.FormKeyOverrides[reviewedPool.FormKey.ToString()] = new() { Policy = CreatureListPolicy.StaticPool, AllowSpecial = true };
builder = Build(settings); Check(!builder.Accepted.Contains(reviewedPool.FormKey), "Unreviewed script remains guarded");
settings.ReviewedScripts[reviewedScript.FormKey.ToString()] = CreaturePoolBuilder.ScriptFingerprint(reviewedScript);
builder = Build(settings); Check(builder.Accepted.Contains(reviewedPool.FormKey), "Reviewed matching script permits static selection");
reviewedScript.Fields.SourceCode += "\n; changed source";
builder = Build(settings); Check(!builder.Accepted.Contains(reviewedPool.FormKey), "Changed source invalidates script review");
settings.ReviewedScripts[reviewedScript.FormKey.ToString()] = CreaturePoolBuilder.ScriptFingerprint(reviewedScript);
reviewedScript.Fields.CompiledScript = new byte[] { 1, 2, 4 };
builder = Build(settings); Check(!builder.Accepted.Contains(reviewedPool.FormKey), "Changed compiled script invalidates review even if source is unchanged");
weak.Script.SetTo(reviewedScript.FormKey);
builder = Build(settings); Check(!builder.Accepted.Contains(templatePool.FormKey), "Template scripts require matching review too");
settings.ReviewedScripts[reviewedScript.FormKey.ToString()] = CreaturePoolBuilder.ScriptFingerprint(reviewedScript);
builder = Build(settings); Check(builder.Accepted.Contains(templatePool.FormKey), "Reviewed template script remains intact and permits deleveling");
Console.WriteLine("Creature list tests passed: weighting, variant bias, level independence, determinism, shared quest isolation, scripts, cycles, missing links, manual counts, priority, exclusions and UseAll.");

if (args.Length == 3 && args[0] == "verify")
{
    using var report = JsonDocument.Parse(File.ReadAllText(args[2]));
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    var actual = patch.LeveledCreatures.ToDictionary(list => list.FormKey.ToString(), list => list);
    var expected = report.RootElement.GetProperty("Lists").EnumerateArray().Where(row => row.GetProperty("Status").GetString() == "Modified")
        .Select(row => (Key: row.GetProperty("FormKey").GetString()!, Snapshot: row.GetProperty("Planned")))
        .Concat(report.RootElement.GetProperty("GeneratedPools").EnumerateArray().Select(row => (Key: row.GetProperty("FormKey").GetString()!, Snapshot: row.GetProperty("Planned")))).ToArray();
    // Pipeline-local new keys are serialized under the output plugin filename.
    string OutputKey(string key) => key.EndsWith(":Synthesis.esp", StringComparison.OrdinalIgnoreCase) ? key.Replace(":Synthesis.esp", ":" + patch.ModKey) : key;
    foreach (var item in expected)
    {
        Check(actual.TryGetValue(OutputKey(item.Key), out var record), $"Missing patched list {item.Key}");
        var snapshot = JsonSerializer.Serialize(CreatureListDeleveling.Snapshot(record!), CreatureListConfiguration.JsonOptions).Replace(":" + patch.ModKey, ":Synthesis.esp");
        Check(JsonElement.DeepEquals(JsonDocument.Parse(snapshot).RootElement, item.Snapshot), $"Patched list differs from plan: {item.Key}");
    }
    Check(actual.Count == expected.Length, "Unexpected list records in installed patch");
    foreach (var row in report.RootElement.GetProperty("Lists").EnumerateArray())
    {
        if (row.GetProperty("SelectionChecks").ValueKind == JsonValueKind.Null) continue;
        var checks = row.GetProperty("SelectionChecks").EnumerateArray().Select(check => check.GetProperty("Outcomes")).ToArray();
        Check(checks.All(check => check.ValueKind != JsonValueKind.Null && JsonElement.DeepEquals(check, checks[0])), "Selection probabilities changed with player level");
        Check(!row.GetProperty("PlannedDependsOnPlayerLevel").GetBoolean(), "Accepted pool still has a level gate");
    }
    Console.WriteLine($"Installed ESP verified: {expected.Length} list overrides/new pools; all accepted encounter distributions identical at levels 1, 10, 25 and 40.");
}

if (args.Length == 3 && args[0] == "report-only")
{
    using var report = JsonDocument.Parse(File.ReadAllText(args[2]));
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    Check(report.RootElement.GetProperty("ReportOnly").GetBoolean(), "Expected a report-only run");
    Check(!patch.EnumerateMajorRecords().Any(), "Report-only output must contain no modified/generated records");
    Check(report.RootElement.GetProperty("Summary").GetProperty("PlannedOverrides").GetInt32() > 0 && report.RootElement.GetProperty("Summary").GetProperty("ModifiedOverrides").GetInt32() == 0, "Dry run must plan changes without applying them");
    Check(report.RootElement.GetProperty("Lists").EnumerateArray().All(row => row.GetProperty("ModifiedFields").GetArrayLength() == 0), "Dry run must report no applied fields");
    Console.WriteLine("Report-only verification passed: plans present, no modified or generated ESP records.");
}

if (args.Length == 3 && args[0] == "audit")
{
    var records = new Dictionary<FormKey, IMajorRecordGetter>();
    using var baseGame = OblivionMod.CreateFromBinaryOverlay(Path.Combine(args[1], "Oblivion.esm"), OblivionRelease.Oblivion);
    using var knights = OblivionMod.CreateFromBinaryOverlay(Path.Combine(args[1], "Knights.esp"), OblivionRelease.Oblivion);
    foreach (var mod in new[] { baseGame, knights })
        foreach (var record in mod.EnumerateMajorRecords()) records[record.FormKey] = record;
    File.WriteAllText(args[2], JsonSerializer.Serialize(new
    {
        Scripts = records.Values.OfType<IScriptGetter>().Select(script => new { Key = script.FormKey.ToString(), script.EditorID, Fingerprint = CreaturePoolBuilder.ScriptFingerprint(script) }),
        Actors = records.Values.Where(record => record is INpcGetter or ICreatureGetter).Select(record => new
        {
            Key = record.FormKey.ToString(), record.EditorID,
            Script = record is INpcGetter npc ? npc.Script.FormKeyNullable?.ToString() : ((ICreatureGetter)record).Script.FormKeyNullable?.ToString()
        })
    }, new JsonSerializerOptions { WriteIndented = true }));
}
