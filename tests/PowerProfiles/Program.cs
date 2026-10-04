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

