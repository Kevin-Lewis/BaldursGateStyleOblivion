using System.Text.Json;
using BaldursGateStyleOblivion.Classification;
using BaldursGateStyleOblivion.Core;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Reject(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new Exception("Expected invalid profile data to be rejected.");
}
static PowerTierRange Range(int minimum, int maximum) => new(new(minimum), new(maximum));

var power = new PowerProfile();
Check(power.Tier is null && power.Unclassified.Contains("PowerTier"), "Unknown must differ from zero.");
power.AssignTier(new(2), "fallback", "Fallback tier", RulePriority.Fallback);
power.AssignTier(new(4), "creature", "Creature classification rule", RulePriority.RaceCreature);
power.AssignTier(new(6), "explicit", "Explicit FormKey override", RulePriority.ExplicitFormKeyOverride);
power.AssignTier(new(7), "tie", "Later assignment at the same priority", RulePriority.ExplicitFormKeyOverride);
var decision = power.Dimensions["PowerTier"];
Check(power.Tier?.Value == 6 && decision.Selected.Reason == "Explicit FormKey override", "Tier precedence failed.");
Check(decision.Superseded.Length == 3 && decision.Superseded[0].Rule == "tie", "Losing assignments or ties lost.");
var zero = new PowerProfile();
zero.AssignTier(new(0), "zero", "Deliberate negligible power");
Check(zero.Tier?.Value == 0 && zero.Unclassified.Length == 0, "Tier zero should be classified.");

var actor = new ActorProfile();
actor.Apply(new ActorValues { CombatRole = CombatRole.Archer }, "class", "Archer class", RulePriority.Class);
actor.Apply(new ActorValues { PowerTier = 6 }, "manual", "Explicit actor tier", RulePriority.ExplicitFormKeyOverride);
Check(actor.Tier?.Value == 6 && (CombatRole)actor.Dimensions["CombatRole"].Selected.Value == CombatRole.Archer,
    "Partial actor override lost inferred role.");
Reject(() => actor.Apply(new ActorValues { ActorCategory = ActorCategory.Bandit, PowerTier = 11 }, "bad", "Invalid tier"));
Check(!actor.Dimensions.ContainsKey("ActorCategory"), "Invalid values partially changed the profile.");
Reject(() => actor.Apply(new ActorValues { CombatRole = (CombatRole)999 }, "bad", "Invalid role"));

var location = new LocationProfile();
location.Apply(new LocationValues { PowerTier = 3, EnemyTierRange = Range(2, 4), LootTierRange = Range(2, 3) },
    "dungeon", "Dungeon classification", RulePriority.Location);
location.Apply(new LocationValues { LootTierRange = Range(4, 5) }, "reward", "Curated loot range", RulePriority.ExplicitFormKeyOverride);
Check(location.Tier?.Value == 3 && ((PowerTierRange)location.Dimensions["EnemyTierRange"].Selected.Value).Maximum.Value == 4,
    "Location override changed unrelated dimensions.");
Check(((PowerTierRange)location.Dimensions["LootTierRange"].Selected.Value).Minimum.Value == 4, "Location range override failed.");

var equipment = new EquipmentProfile();
equipment.Apply(new EquipmentValues { PowerTier = 2, Kind = EquipmentKind.Weapon, Material = "Iron" },
    "material", "Material rule", RulePriority.GenericArchetype);
equipment.Apply(new EquipmentValues { PowerTier = 5 }, "special", "Curated item tier", RulePriority.ExplicitFormKeyOverride);
Check(equipment.Tier?.Value == 5 && (string)equipment.Dimensions["Material"].Selected.Value == "Iron", "Equipment override lost identity.");
Reject(() => equipment.Apply(new EquipmentValues { Material = " " }, "bad", "Blank material"));
Reject(() => location.Apply(new EquipmentValues(), "bad", "Wrong profile values"));
Reject(() => power.AssignTier(new(1), "rule", " "));
Reject(() => new PowerTier(-1));
Reject(() => new PowerTier(11));
Reject(() => Range(7, 3));
Check(Range(0, 10).Maximum.Value == 10 && Range(4, 4).Minimum.Value == 4, "Valid range rejected.");

var jsonOptions = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };
var json = JsonSerializer.Serialize(new LocationValues { PowerTier = 3, EnemyTierRange = Range(2, 4) });
var roundTrip = JsonSerializer.Deserialize<LocationValues>(json, jsonOptions)!;
Check(roundTrip.EnemyTierRange?.Minimum.Value == 2 && roundTrip.EnemyTierRange?.Maximum.Value == 4,
    "Range serialization lost its bounds.");
Reject(() => JsonSerializer.Deserialize<PowerTier>("{\"Value\":11}"));
Reject(() => JsonSerializer.Deserialize<PowerTierRange>("{\"Minimum\":{\"Value\":7},\"Maximum\":{\"Value\":3}}"));
var missingBoundRejected = false;
try { JsonSerializer.Deserialize<PowerTierRange>("{\"Maximum\":{\"Value\":4}}", jsonOptions); }
catch (JsonException) { missingBoundRejected = true; }
Check(missingBoundRejected, "Missing JSON range bounds must not become zero implicitly.");
Console.WriteLine("Power profile checks passed: assignment, explanations, precedence, partial overrides, validation, and serialization.");


static void RejectConfiguration<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception(message);
}
var configurationDirectory = Path.Combine(Path.GetTempPath(), "actor-configuration-" + Guid.NewGuid());
Directory.CreateDirectory(configurationDirectory);
try
{
    var rootFile = Path.Combine(configurationDirectory, "actor-classification.json");
    File.WriteAllText(rootFile, """{"Includes":["group.json","override.json"]}""");
    File.WriteAllText(Path.Combine(configurationDirectory, "group.json"), """
        {"Rules":[{"Id":"basic bandit","Priority":"Faction","Evidence":"EditorID","Match":"Bandit","MatchMode":"Prefix",
        "All":[{"Evidence":"Faction","Match":"BanditFaction"}],"Values":{"PowerTier":2,"Handling":"Generic"}}]}
        """);
    File.WriteAllText(Path.Combine(configurationDirectory, "override.json"), """
        {"FormKeyOverrides":{"000123:Oblivion.esm":{"Name":"Bandit boss","PowerTier":null,"Reason":"AI proposal"}}}
        """);
    var config = ActorConfiguration.Load(rootFile);
    var evidence = new Dictionary<string, string[]> { ["EditorID"] = ["BanditMelee"], ["Faction"] = ["BanditFaction"] };
    var result = ActorConfiguration.Classify(config, "000123:Oblivion.esm", "Oblivion.esm", evidence);
    Check(result.Tier?.Value == 2, "Empty override must preserve group rules");
    config.FormKeyOverrides["000123:Oblivion.esm"].PowerTier = 6;
    result = ActorConfiguration.Classify(config, "000123:Oblivion.esm", "Oblivion.esm", evidence);
    Check(result.Tier?.Value == 6 && result.Dimensions["PowerTier"].Selected.Reason == "AI proposal", "Direct override and reason must win");
    Check(result.Dimensions["Handling"].Selected.Value.Equals(ActorHandling.Generic), "Partial override preserves handling");
    Check(ActorConfiguration.Classify(config, "000124:Oblivion.esm", "Oblivion.esm", new() { ["EditorID"] = ["BanditMelee"] }).Tier is null, "All conditions must match");
    config.Rules[0].Enabled = false;
    Check(ActorConfiguration.Classify(config, "000124:Oblivion.esm", "Oblivion.esm", evidence).Tier is null, "Disabled rules must not assign tiers");
    File.WriteAllText(rootFile, """{"Includes":["group.json","group.json"]}""");
    RejectConfiguration<InvalidDataException>(() => ActorConfiguration.Load(rootFile), "Repeated includes must fail");
    File.WriteAllText(rootFile, """{"Includes":["../outside.json"]}""");
    RejectConfiguration<InvalidDataException>(() => ActorConfiguration.Load(rootFile), "Outside includes must fail");
    File.WriteAllText(rootFile, """{"Includes":["override.json"],"FormKeyOverrides":{"000123:Oblivion.esm":{}}}""");
    RejectConfiguration<InvalidDataException>(() => ActorConfiguration.Load(rootFile), "Duplicate overrides must fail");
    File.WriteAllText(rootFile, """{"Includes":[],"Rulez":[]}""");
    RejectConfiguration<System.Text.Json.JsonException>(() => ActorConfiguration.Load(rootFile), "Misspelled settings must fail");
    Console.WriteLine("Grouped rules, direct overrides, and include validation passed.");
}
finally { Directory.Delete(configurationDirectory, true); }

var shipped = ActorConfiguration.Load(Path.GetFullPath("BaldursGateStyleOblivion/actor-classification.json"));
ActorProfile TierFor(string name, string type, string cls = "") => ActorConfiguration.Classify(shipped,
    "FFFFFF:Oblivion.esm", "Oblivion.esm", new() { ["Name"] = [name], ["RecordType"] = [type], ["Class"] = [cls] });
Check(TierFor("Sharp Tooth Goblin Berserker", "Creature").Tier?.Value == 4, "Tribal specialist must beat ordinary goblin match.");
Check(TierFor("Vampire Bard", "NPC", "Bard").Tier?.Value == 4, "Vampire identity must beat civilian class baseline.");
Check(TierFor("Dremora Valkynaz", "NPC").Tier?.Value > TierFor("Dremora Churl", "NPC").Tier?.Value, "Dremora ranks must remain distinct.");
Check(TierFor("Rat", "NPC").Tier is null && TierFor("Unrecognized Monster", "Creature").Tier is null, "Unknown identities must not inherit unrelated species tiers.");
Check(TierFor("Umbra", "NPC", "Warrior").Tier is null, "Important named combatants need individual evidence.");
Check(ActorConfiguration.Classify(shipped, "000007:Oblivion.esm", "Oblivion.esm", new() { ["Name"] = ["Bendu Olo"], ["RecordType"] = ["NPC"], ["Class"] = ["CharactergenClass"] }).Tier is null, "Player must remain unassigned.");
shipped.FormKeyOverrides["FFFFFF:Oblivion.esm"] = new() { PowerTier = 6, Reason = "Personal judgment" };
Check(TierFor("Bandit", "NPC").Tier?.Value == 6, "Individual edits must beat shipped group tiers.");
Console.WriteLine("Shipped actor tiers: rank specificity, civilian conflicts, unknowns, player and overrides passed.");

shipped.FormKeyOverrides.Remove("FFFFFF:Oblivion.esm");
Check(TierFor("Ordinary Fighter", "NPC", "Warrior").Tier?.Value == 3, "Ordinary named fighter must receive class baseline.");
Check(TierFor("Marauder Warlord", "NPC", "Warrior").Tier?.Value == 5, "Specific role must beat class baseline.");
Check(TierFor("Hannibal Traven", "NPC", "Mage").Tier is null, "Prominent mage must remain available for lore review.");
Check(ActorConfiguration.Classify(shipped, "FFFFFE:Oblivion.esm", "Oblivion.esm", new() { ["Name"] = ["Voice"], ["EditorID"] = ["DASheogorathVoice"], ["Class"] = ["Acrobat"], ["RecordType"] = ["NPC"] }).Tier is null, "Voice helpers must not inherit class tiers.");
Console.WriteLine("Expanded coverage: class fallbacks, specific roles, exceptional actors and negative conditions passed.");

Check(TierFor("Brother Martin", "NPC", "Conjurer").Tier is null, "Martin's actual display name must be excluded.");
Check(ActorConfiguration.Classify(shipped, "FFFFFD:Oblivion.esm", "Oblivion.esm", new() { ["Name"] = ["Janus Hassildor"], ["RecordType"] = ["NPC"], ["Class"] = ["Noble"], ["Faction"] = ["VampireFaction"] }).Tier is null, "Named vampire lord must not inherit ordinary vampire or noble tiers.");
