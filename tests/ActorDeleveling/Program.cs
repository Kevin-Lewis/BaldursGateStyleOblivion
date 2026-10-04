using System.Text.Json;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Discovery;
using BaldursGateStyleOblivion.Modules;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;

void Check(bool value, string message) { if (!value) throw new Exception(message); }
const string key = "000800:Test.esp";
var settings = new ClassificationSettings();
var profile = new ActorProfile(); profile.Apply(new ActorValues { PowerTier = 4, Handling = ActorHandling.Generic }, "Species", "Test");
ActorLevelDecision Decide(bool offset = true, ActorProfile? actor = null, string? risk = null, string? editorId = "Bandit") =>
    ActorLevelPolicy.Decide(settings, key, editorId, "Bandit", false, offset, 10, actor ?? profile, risk);
Check(Decide().TargetLevel == 15, "Tier 4 must become fixed level 15");
Check(Decide(false).TargetLevel == 15, "Fixed generic actors must also follow tier mapping");
var named = new ActorProfile(); named.Apply(new ActorValues { PowerTier = 4, Handling = ActorHandling.Named }, "Class", "Test");
Check(Decide(false, named).TargetLevel is null && Decide(true, named).TargetLevel == 15, "Fixed named fallbacks need different handling from scalable named actors");
var quest = new ActorProfile(); quest.Apply(new ActorValues { PowerTier = 4, Handling = ActorHandling.QuestRelated }, "Class", "Test");
Check(Decide(actor: quest).TargetLevel is null, "Quest-sensitive fallback requires a curated decision");
settings.FormKeyOverrides[key] = new() { PowerTier = 4 };
Check(Decide(actor: quest).TargetLevel == 15, "Curated quest actors must be convertible");
settings.FormKeyOverrides[key].FixedLevel = 17;
Check(Decide().TargetLevel == 17, "Individual level must take precedence over tier mapping");
settings.FormKeyOverrides[key].Delevel = false;
Check(Decide().TargetLevel is null, "An exemption must beat an explicit level");
settings.FormKeyOverrides[key].Delevel = true;
Check(Decide(editorId: "TestBandit").TargetLevel == 17, "Explicit inclusion should allow a restored test actor");
Check(Decide(editorId: "Player").TargetLevel is null && Decide(risk: "Runtime scaling").TargetLevel is null, "Player and runtime safeguards cannot be bypassed");
settings.FormKeyOverrides.Remove(key);
Check(Decide(editorId: "SECorpserotGrummite01TEMP").TargetLevel == 15, "Corpserot location names must not be treated as corpses");
Check(ActorLevelPolicy.Decide(settings, key, "HearsVoicesInTheAir", "Hears-Voices-In-The-Air", false, true, 4, profile).TargetLevel == 15, "Named mage must not be mistaken for a recording voice");

Check(Decide(editorId: "TestBandit").TargetLevel is null, "Technical helpers must be protected by default");
var apex = new ActorProfile(); apex.Apply(new ActorValues { PowerTier = 10, Handling = ActorHandling.MajorBoss }, "Override", "Test");
Check(Decide(actor: apex).TargetLevel is null, "Apex requires an individual level");
settings.FormKeyOverrides[key] = new() { FixedLevel = 45 };
Check(Decide(actor: apex).TargetLevel == 45, "Apex individual level must work");
var mod = new OblivionMod(ModKey.FromNameAndExtension("Test.esp"), OblivionRelease.Oblivion);
var npc = mod.Npcs.AddNew(); npc.EditorID = "TestNpc"; npc.Name = "NPC";
npc.Configuration = new NpcConfiguration { LevelOffset = -2, CalcMin = 3, CalcMax = 25,
    Flags = Npc.NpcFlag.PCLevelOffset | Npc.NpcFlag.Essential | Npc.NpcFlag.AutoCalcStats, BaseSpellPoints = 123, Fatigue = 99, BarterGold = 17 };
npc.Stats = new() { Health = 200, Strength = 40 };
var npcCopy = npc.DeepCopy();
ActorDeleveling.Apply(npc, 15);
Check(npc.Configuration.LevelOffset == 15 && !npc.Configuration.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset) && npc.Configuration.CalcMin == 0 && npc.Configuration.CalcMax == 0, "NPC must lose offset and bounds");
Check(npc.Configuration.Flags == (npcCopy.Configuration!.Flags & ~Npc.NpcFlag.PCLevelOffset), "Unrelated NPC flags must be preserved");
Check(npc.Configuration.BaseSpellPoints == 123 && npc.Configuration.Fatigue == 99 && npc.Configuration.BarterGold == 17 && npc.Stats!.Health == 200 && npc.Stats.Strength == 40, "NPC stats and economic data must stay untouched");
Check(npcCopy.Configuration.LevelOffset == -2, "The winning source record must not be mutated");
var creature = mod.Creatures.AddNew(); creature.Name = "Creature";
creature.Configuration = new CreatureConfiguration { LevelOffset = 2, CalcMin = 5, CalcMax = 30, Flags = Creature.CreatureFlag.PCLevelOffset, BaseSpellPoints = 321 };
creature.Data = new CreatureData { Health = 20, AttackDamage = 17, CombatSkill = 12, MagicSkill = 19, StealthSkill = 8, Strength = 60 };
var creatureCopy = creature.DeepCopy();
ActorDeleveling.Apply(creature, 25);
Check(creature.Configuration.LevelOffset == 0 && creature.Configuration.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset)
    && creature.Configuration.CalcMin == 25 && creature.Configuration.CalcMax == 25, "Scaled creature must be fixed by equal bounds, preserving native calculation");
Check(creature.Data.Equals(creatureCopy.Data) && creature.Configuration.BaseSpellPoints == 321 && creature.Configuration.Fatigue == creatureCopy.Configuration!.Fatigue,
    "Health, attack, skills, attributes and resource coefficients must remain unchanged");
Check(!ActorDeleveling.IsPlayerDependent(true, 25, 25) && ActorDeleveling.IsPlayerDependent(true, 25, 0), "Pinned levels must be distinguished from uncapped offset scaling");
foreach (var playerLevel in new[] { 1, 5, 25, 50, 100 })
    Check(Math.Clamp(playerLevel + creature.Configuration.LevelOffset, (int)creature.Configuration.CalcMin, (int)creature.Configuration.CalcMax) == 25,
        "Different player levels must produce the same creature level");
Check(ActorDeleveling.ChangedFields(0, true, 25, 25, 25, true).Length == 0, "Repeated creature conversion must be idempotent");
Check(creatureCopy.Configuration!.LevelOffset == 2 && creatureCopy.Configuration.CalcMin == 5, "Source creature must remain unchanged");
var fixedCreature = creatureCopy.DeepCopy(); fixedCreature.Configuration!.Flags &= ~Creature.CreatureFlag.PCLevelOffset;
ActorDeleveling.Apply(fixedCreature, 15);
Check(fixedCreature.Configuration.LevelOffset == 15 && fixedCreature.Configuration.CalcMin == 0 && fixedCreature.Configuration.CalcMax == 0
    && !fixedCreature.Configuration.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset) && fixedCreature.Data!.Equals(creatureCopy.Data),
    "Already-fixed creatures must keep their original stat interpretation");
Check(ActorDeleveling.ChangedFields(15, false, 0, 0, 15).Length == 0 && ActorDeleveling.ChangedFields(15, true, 0, 0, 15).Contains("Configuration.Flags.PCLevelOffset"), "Offset removal must be reported even if the numeric field matches");
Check(ActorDeleveling.ChangedFields(15, false, 8, 30, 15).Length == 0, "Already-fixed actors need no override solely for inactive bounds");
var signals = ScriptDiscovery.Scan("; player.GetLevel\nMessage \"SetLevel 1\"\nif player.GetLevel > 10\nfoo.SetAV aggression 5\nfoo.SetAV health 200\nbar.SetLevel 1, 1");
var writes = ActorDeleveling.ScalingWrites(signals);
Check(writes.Length == 2 && writes.Any(signal => signal.ActorValue == "health") && writes.All(signal => signal.ActorValue != "aggression"), "Quest aggression changes are not player-dependent combat-stat scaling");
Check(ActorDeleveling.ScalingWrites(ScriptDiscovery.Scan("foo.SetAV health 200")).Length == 0, "A fixed scripted health assignment alone is not player-level dependence");
Console.WriteLine("Actor deleveling policy and record checks passed: mapping, handling, exemptions, Apex, source preservation, flags, bounds, stats and script candidates.");

if (args.Length == 3 && args[0] == "verify")
{
    using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[2], "BaldursGateStyleOblivion.actor-deleveling.json")));
    var rows = report.RootElement.GetProperty("Actors").EnumerateArray().Where(row => row.GetProperty("Status").GetString() == "Modified")
        .ToDictionary(row => FormKey.Factory(row.GetProperty("FormKey").GetString()!), row => row.Clone());
    using var scripts = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[2], "BaldursGateStyleOblivion.scripts.json")));
    var reviewedSource = scripts.RootElement.GetProperty("Records").EnumerateArray()
        .Single(row => row.GetProperty("Record").GetProperty("FormKey").GetString() == "02D32C:Oblivion.esm")
        .GetProperty("Original").GetProperty("SourceCode").GetString()!;
    var reviewedKey = FormKey.Factory("02D32C:Oblivion.esm");
    Check(ActorDeleveling.ReviewedConstantBonus(reviewedKey, reviewedSource), "Reviewed vanilla constant bonus must be recognized");
    Check(!ActorDeleveling.ReviewedConstantBonus(reviewedKey, reviewedSource + "\nCaminaldaRef.SetLevel 0"), "Changed scripts must retain scaling safeguards");
    using var patch = OblivionMod.CreateFromBinaryOverlay(args[1], OblivionRelease.Oblivion);
    var data = @"F:\SteamLibrary\steamapps\common\Oblivion\Data";
    var sources = rows.Values.Select(row => row.GetProperty("WinningOverridePlugin").GetString()!).Distinct()
        .ToDictionary(plugin => plugin, plugin => OblivionMod.CreateFromBinaryOverlay(Path.Combine(data, plugin), OblivionRelease.Oblivion));
    try
    {
        var sourceNpcs = sources.ToDictionary(pair => pair.Key, pair => pair.Value.Npcs.ToDictionary(actor => actor.FormKey));
        var sourceCreatures = sources.ToDictionary(pair => pair.Key, pair => pair.Value.Creatures.ToDictionary(actor => actor.FormKey));
        foreach (var actor in patch.Npcs)
        {
            Check(rows.TryGetValue(actor.FormKey, out var row), "Unexpected NPC override");
            var expected = sourceNpcs[row.GetProperty("WinningOverridePlugin").GetString()!][actor.FormKey].DeepCopy();
            ActorDeleveling.Apply(expected, row.GetProperty("TargetLevel").GetInt32());
            Check(expected.Equals(actor.DeepCopy()), $"Unexpected NPC changes: {actor.FormKey}");
        }
        foreach (var actor in patch.Creatures)
        {
            Check(rows.TryGetValue(actor.FormKey, out var row), "Unexpected creature override");
            var expected = sourceCreatures[row.GetProperty("WinningOverridePlugin").GetString()!][actor.FormKey].DeepCopy();
            ActorDeleveling.Apply(expected, row.GetProperty("TargetLevel").GetInt32());
            Check(expected.Equals(actor.DeepCopy()), $"Unexpected creature changes: {actor.FormKey}");
        }
        Check(patch.Npcs.Count + patch.Creatures.Count == rows.Count, "ESP record count must match actual modifications");
        Console.WriteLine($"ESP verification passed: {patch.Npcs.Count} NPCs and {patch.Creatures.Count} creatures; every record matches its winning original with only the intended configuration changes.");
    }
    finally { foreach (var source in sources.Values) source.Dispose(); }
}
