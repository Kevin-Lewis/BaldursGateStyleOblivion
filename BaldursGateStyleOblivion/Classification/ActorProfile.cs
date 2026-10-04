using BaldursGateStyleOblivion.Core;

namespace BaldursGateStyleOblivion.Classification;

public enum ActorCategory { Civilian, Bandit, Marauder, Soldier, Guard, Adventurer, Mage, Necromancer, Vampire, Undead, Goblin, Daedra, Animal, Monster, Other }
public enum ActorNature { Mortal, Undead, Daedric, Animal, Other }
public enum CombatRole { Civilian, Melee, Tank, Bruiser, Skirmisher, Archer, Mage, Battlemage, Support, Controller }
public enum FactionRole { Member, Leader, Other }
public enum BossStatus { None, Minor, Major }
public enum QuestImportance { Ordinary, QuestRelated, Critical }

public sealed class ActorValues : PowerValues
{
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
    public string Id { get; set; } = "";
    public RulePriority? Priority { get; set; }
    public string Reason { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Match { get; set; } = "";
    public string? SourcePlugin { get; set; }
    public ActorValues Values { get; set; } = new();
}

public sealed class ClassificationSettings
{
    public List<ActorRule> Rules { get; set; } = new();
    public Dictionary<string, ActorValues> FormKeyOverrides { get; set; } = new();
}



