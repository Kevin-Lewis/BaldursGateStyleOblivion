using System.Reflection;
using System.Text.Json.Serialization;
using BaldursGateStyleOblivion.Core;

namespace BaldursGateStyleOblivion.Classification;

public readonly record struct PowerTier
{
    public int Value { get; }

    [JsonConstructor]
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

    [JsonConstructor]
    public PowerTierRange(PowerTier minimum, PowerTier maximum)
    {
        if (minimum.Value > maximum.Value) throw new ArgumentException("Minimum tier exceeds maximum.");
        Minimum = minimum;
        Maximum = maximum;
    }
}

public class PowerValues
{
    public int? PowerTier { get; set; }
}

public class PowerProfile
{
    private readonly RuleResolver<object> resolver = new();
    protected virtual Type ValuesType => typeof(PowerValues);
    public Dictionary<string, ResolvedField<object>> Dimensions => resolver.Resolve();
    public string[] Unclassified => ValuesType.GetProperties().Select(property => property.Name)
        .Where(name => !Dimensions.ContainsKey(name)).ToArray();

    [JsonIgnore]
    public PowerTier? Tier => Dimensions.TryGetValue(nameof(PowerValues.PowerTier), out var decision)
        ? new PowerTier((int)decision.Selected.Value) : null;

    public void AssignTier(PowerTier tier, string rule, string reason, RulePriority priority = RulePriority.Fallback)
        => resolver.Add(nameof(PowerValues.PowerTier), new(tier.Value, rule, priority, reason));

    public void Apply(PowerValues values, string rule, string reason, RulePriority priority = RulePriority.Fallback)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!ValuesType.IsInstanceOfType(values)) throw new ArgumentException($"Expected {ValuesType.Name}.", nameof(values));
        var assignments = new List<(string Field, object Value)>();
        foreach (var property in ValuesType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(values);
            if (value is null) continue;
            switch (value)
            {
                case int tier: value = new PowerTier(tier).Value; break;
                case PowerTierRange: break;
                case Enum flag when Enum.IsDefined(flag.GetType(), flag): break;
                case string text when !string.IsNullOrWhiteSpace(text): break;
                default: throw new ArgumentException($"Invalid {property.Name}: {value}");
            }
            assignments.Add((property.Name, value));
        }
        // Validate the complete value set before adding any candidates.
        foreach (var assignment in assignments)
            resolver.Add(assignment.Field, new(assignment.Value, rule, priority, reason));
    }
}

public sealed class LocationValues : PowerValues
{
    public PowerTierRange? EnemyTierRange { get; set; }
    public PowerTierRange? LootTierRange { get; set; }
}

public sealed class LocationProfile : PowerProfile
{
    protected override Type ValuesType => typeof(LocationValues);
}

public enum EquipmentKind { Weapon, Armor, Shield, Ammunition, Clothing, Jewelry, Other }

public sealed class EquipmentValues : PowerValues
{
    public EquipmentKind? Kind { get; set; }
    public string? Material { get; set; }
}

public sealed class EquipmentProfile : PowerProfile
{
    protected override Type ValuesType => typeof(EquipmentValues);
}
