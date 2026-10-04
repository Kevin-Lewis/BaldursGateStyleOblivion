using BaldursGateStyleOblivion.Core;

namespace BaldursGateStyleOblivion.Classification;

public enum ActorCategory { Civilian, Bandit, Marauder, Soldier, Guard, Adventurer, Mage, Necromancer, Vampire, Undead, Goblin, Daedra, Animal, Monster, Other }
public enum ActorNature { Mortal, Undead, Daedric, Animal, Other }
public enum CombatRole { Civilian, Melee, Tank, Bruiser, Skirmisher, Archer, Mage, Battlemage, Support, Controller }
public enum FactionRole { Member, Leader, Other }
public enum BossStatus { None, Minor, Major }
public enum QuestImportance { Ordinary, QuestRelated, Critical }

public readonly record struct PowerTier
{
    public int Value { get; }
    public PowerTier(int value)
    {
        if (value is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(value), "Tier must be 0–10.");
        Value = value;
    }
}

public readonly record struct PowerTierRange
{
    public PowerTier Minimum { get; }
    public PowerTier Maximum { get; }
    public PowerTierRange(PowerTier minimum, PowerTier maximum)
    {
        if (minimum.Value > maximum.Value) throw new ArgumentException("Minimum tier exceeds maximum.");
        Minimum = minimum;
        Maximum = maximum;
    }
}



public sealed class ActorValues
{
    public int? PowerTier { get; set; }
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

public sealed class ActorProfile
{
    private readonly RuleResolver<object> resolver = new();
    public Dictionary<string, ResolvedField<object>> Dimensions => resolver.Resolve();
    public string[] Unclassified => typeof(ActorValues).GetProperties()
        .Select(property => property.Name).Where(name => !Dimensions.ContainsKey(name)).ToArray();

    public void Apply(ActorValues values, string rule, string reason, RulePriority priority = RulePriority.Fallback)
    {
        // Apply only supplied dimensions; missing values remain unclassified.
        foreach (var property in typeof(ActorValues).GetProperties())
        {
            var value = property.GetValue(values);
            if (value is null) continue;
            if (value is int tier) value = new PowerTier(tier).Value;
            else if (!Enum.IsDefined(value.GetType(), value))
                throw new ArgumentException($"Invalid {property.Name}: {value}");
            resolver.Add(property.Name, new(value, rule, priority, reason));
        }
    }
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


