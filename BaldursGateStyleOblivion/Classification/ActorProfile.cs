using BaldursGateStyleOblivion.Core;

namespace BaldursGateStyleOblivion.Classification;

public enum ActorCategory { Civilian, Bandit, Marauder, Soldier, Guard, Adventurer, Mage, Necromancer, Vampire, Undead, Goblin, Daedra, Animal, Monster, Other }
public enum ActorNature { Mortal, Undead, Daedric, Animal, Other }
public enum CombatRole { Civilian, Melee, Tank, Bruiser, Skirmisher, Archer, Mage, Battlemage, Support, Controller }
public enum FactionRole { Member, Leader, Other }
public enum BossStatus { None, Minor, Major }
public enum QuestImportance { Ordinary, QuestRelated, Critical }
public enum ActorHandling { Generic, Named, QuestRelated, FactionLeader, MinorBoss, MajorBoss, ProtectedSpecial }

public class ActorValues : PowerValues
{
    public ActorHandling? Handling { get; set; }
    public ActorCategory? ActorCategory { get; set; }
    public ActorNature? ActorNature { get; set; }
    public CombatRole? CombatRole { get; set; }
    public FactionRole? FactionRole { get; set; }
    public BossStatus? BossStatus { get; set; }
    public QuestImportance? QuestImportance { get; set; }
    public int? EquipmentTier { get; set; }
    public int? MagicTier { get; set; }
    public int? LocationTier { get; set; }
}

public sealed class ActorProfile : PowerProfile
{
    protected override Type ValuesType => typeof(ActorValues);
}

public sealed class ActorRule
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConfigurationFile { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Id { get; set; } = "";
    public RulePriority? Priority { get; set; }
    public string Reason { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Match { get; set; } = "";
    public string MatchMode { get; set; } = "Exact";
    public List<ActorCondition> All { get; set; } = new();
    public string? SourcePlugin { get; set; }
    public ActorValues Values { get; set; } = new();
}

public sealed class ClassificationSettings
{
    public string ResearchModel { get; set; } = "gpt-6.1-sol";
    public Dictionary<string, List<ActorRule>> Groups { get; set; } = new();
    public List<string> Includes { get; set; } = new();
    public List<ActorRule> Rules { get; set; } = new();
    public Dictionary<string, ActorOverride> FormKeyOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}




public sealed class ActorCondition
{
    public string Evidence { get; set; } = "";
    public string Match { get; set; } = "";
    public string MatchMode { get; set; } = "Exact";
}

public sealed class ActorOverride : ActorValues
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConfigurationFile { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Uncertainty { get; set; } = "";
    public string[] Sources { get; set; } = [];
    public string Model { get; set; } = "";
}
