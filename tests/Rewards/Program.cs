using System.Text.Json;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
void Check(bool good, string reason) { if (!good) throw new Exception(reason); }
var fixture = new OblivionMod(ModKey.FromNameAndExtension("Source.esp"), OblivionRelease.Oblivion);
var weak = fixture.Weapons.AddNew(); var strong = fixture.Weapons.AddNew();
LeveledItemEntry Entry(FormKey key, short level, short count = 1) { var e = new LeveledItemEntry { Level = level, Count = count }; e.Reference.SetTo(key); return e; }
var family = fixture.LeveledItems.AddNew(); family.Entries.AddRange(new[] { Entry(weak.FormKey, 1), Entry(strong.FormKey, 20, 2) });
var records = fixture.EnumerateMajorRecords().ToDictionary(r => r.FormKey, r => (IMajorRecordGetter)r);
var builder = new RewardPoolBuilder(records, ModKey.FromNameAndExtension("Patch.esp"), 0x800, _ => true);
var low = builder.Build(family, new() { SelectionLevel = 10 }); var high = builder.Build(family, new() { SelectionLevel = 20 });
Check(low.Entries.Single().Reference.FormKey == weak.FormKey && high.Entries.Single().Reference.FormKey == strong.FormKey, "Fixed benchmark selects correct variant.");
Check(high.Entries.Single().Count == 2 && high.Entries.Single().Level == 1, "Counts preserved and player gates removed.");
Check(family.Entries[1].Level == 20, "Inputs immutable.");
Check(builder.Build(family, new() { SelectionLevel = 10, Variant = strong.FormKey.ToString(), Count = 3 }).Entries.Single().Count == 3, "Explicit variant and quantity take priority.");
family.Flags = LeveledFlag.CalculateFromAllLevelsLessThanPlayers;
Check(builder.Build(family, new() { SelectionLevel = 20 }).Entries.Count == 2, "Random eligible pool remains random.");
family.Flags = LeveledFlag.UseAll;
Check(builder.Build(family, new() { SelectionLevel = 20 }).Flags?.HasFlag(LeveledFlag.UseAll) == true, "UseAll reward bundles retained.");
family.Flags = null;
var lootSettings = LootConfiguration.Load("BaldursGateStyleOblivion/loot.json");
try { new LootPoolBuilder(records, lootSettings, builder.Patch.ModKey, 0x800, _ => true, [strong.FormKey]).Build(family.FormKey, "Danger4"); throw new Exception("Artifact guard missing."); } catch (InvalidDataException) { }
var safeScript = new ScriptFields { SourceCode = "player.AddItem RewardList 1" }; var reference = new ScriptObjectReference(); reference.Reference.SetTo(family.FormKey); safeScript.References.Add(reference);
Check(!WorldLoot.UnsafeListReferences([safeScript]).Contains(family.FormKey), "Read-only grants no longer block private chest pools.");
safeScript.SourceCode = "AddToLeveledList RewardList Item 1 1";
Check(WorldLoot.UnsafeListReferences([safeScript]).Contains(family.FormKey), "Runtime list mutation stays guarded.");
safeScript.SourceCode = null; Check(WorldLoot.UnsafeListReferences([safeScript]).Contains(family.FormKey), "Source-unavailable references stay guarded.");
var config = RewardConfiguration.Load("BaldursGateStyleOblivion/rewards.json");
var originals = Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data", "*.esm").Concat(Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data", "DLC*.esp")).Concat(Directory.GetFiles("F:/SteamLibrary/steamapps/common/Oblivion/Data", "Knights.esp")).Select(path => OblivionMod.CreateFromBinaryOverlay(path, OblivionRelease.Oblivion)).ToArray();
var native = originals.SelectMany(m => m.EnumerateMajorRecords()).GroupBy(r => r.FormKey).ToDictionary(g => g.Key, g => g.Last());
var contexts = QuestRewards.ScriptContexts(native.Values).ToArray();
foreach (var choice in config.Scripts)
{
    var context = contexts.Single(c => c.Owner.FormKey.ToString() == choice.Record && c.Context == choice.Context);
    var fixedScript = RewardScriptEditor.FixLevel(context.Fields, choice.SelectionLevel, choice.Fingerprint);
    var bytes = context.Fields.CompiledScript.GetValueOrDefault().ToArray();
    Check(bytes.Length == fixedScript.Bytes.Length, "All compiled offsets and metadata sizes preserved.");
    Check(!BaldursGateStyleOblivion.Discovery.ScriptDiscovery.Scan(fixedScript.Source).Any(s => s.Kind == "PlayerLevelRead"), "Reward-only source contains no player-level reads.");
    int replaced = 0;
    for (var offset = 0; offset < bytes.Length; offset++)
    {
        if (bytes[offset] == fixedScript.Bytes[offset]) continue;
        Check(bytes[offset] == (byte)'r' && fixedScript.Bytes.AsSpan(offset, 8).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(choice.SelectionLevel.ToString("D8"))), "Only complete player-level expression tokens changed.");
        Check(int.Parse(System.Text.Encoding.ASCII.GetString(fixedScript.Bytes, offset, 8)) == choice.SelectionLevel, "Native ASCII numeric literal evaluates to fixed benchmark.");
        offset += 7; replaced++;
    }
    Check(replaced == fixedScript.Replacements, "All source reads have exact compiled replacements.");
    Console.WriteLine($"Verified {choice.Name}: {replaced} fixed reward expressions.");
}
var nativeBuilder = new RewardPoolBuilder(native, builder.Patch.ModKey, 0x800, _ => true);
foreach (var choice in config.Lists)
{
    var original = (ILeveledItemGetter)native[FormKey.Factory(choice.Key)];
    var result = nativeBuilder.Build(original, choice.Value);
    Check(result.Entries.Count > 0 && result.Entries.All(e => e.Level == 1), "All configured reward families are static.");
    Check(result.ChanceNone == original.ChanceNone, "Native empty chance retained.");
}
if (args.Length > 1 && args[0] == "--dry")
{
    using var dry = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    Check(!dry.EnumerateMajorRecords().Any(), "Report-only rewards must write no gameplay records.");
    using var dryReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1])!, "Reports/BaldursGateStyleOblivion.quest-rewards.json")));
    Check(dryReport.RootElement.GetProperty("Rewards").EnumerateArray().All(r => r.GetProperty("Status").GetString() == "Planned"), "Report-only still produces reward decisions.");
    Console.WriteLine("Report-only rewards verified: all plans present, empty ESP.");
}
else if (args.Length > 0)
{
    using var output = OblivionMod.CreateFromBinaryOverlay(args[0], OblivionRelease.Oblivion);
    var saved = output.EnumerateMajorRecords().ToDictionary(r => r.FormKey);
    foreach (var pair in config.Lists)
    {
        var key = FormKey.Factory(pair.Key); Check(saved.GetValueOrDefault(key) is ILeveledItemGetter, "Reward override written: " + key);
        Check(((ILeveledItemGetter)saved[key]).Entries.All(e => e.Level == 1), "Written reward gates removed.");
    }
    foreach (var choice in config.Scripts)
    {
        var edited = QuestRewards.ScriptContexts(saved.Values).Single(c => c.Owner.FormKey.ToString() == choice.Record && c.Context == choice.Context);
        Check(!BaldursGateStyleOblivion.Discovery.ScriptDiscovery.Scan(edited.Fields.SourceCode).Any(s => s.Kind == "PlayerLevelRead"), "Written scripted rewards static.");
        var original = contexts.Single(c => c.Owner.FormKey == edited.Owner.FormKey && c.Context == edited.Context);
        Check(edited.Fields.CompiledScript.GetValueOrDefault().Span.SequenceEqual(RewardScriptEditor.FixLevel(original.Fields, choice.SelectionLevel, choice.Fingerprint).Bytes), "Written bytecode equals verified edit.");
    }
    foreach (var artifact in config.Artifacts.Keys) Check(!saved.ContainsKey(FormKey.Factory(artifact)), "Artifact stats untouched: " + artifact);
    Console.WriteLine("Written ESP verified: reward overrides, compiled scripts and unchanged artifact items.");
}
foreach (var mod in originals) mod.Dispose();
Console.WriteLine($"Reward fixtures passed: {config.Lists.Count} native families, {config.Scripts.Count} reviewed scripts, artifact protection and chest guards.");

