namespace BaldursGateStyleOblivion.Core;

// Lower numeric values have higher precedence.
public enum RulePriority
{
    ExplicitFormKeyOverride, PluginSpecific, NamedSpecialActor, Faction, Class,
    RaceCreature, Location, GenericArchetype, Fallback
}

public record RuleDecision<T>(T Value, string Rule, RulePriority Priority, string Reason);
public sealed record ResolvedField<T>(RuleDecision<T> Selected, RuleDecision<T>[] Superseded);

public sealed class RuleResolver<T>
{
    private readonly Dictionary<string, List<RuleDecision<T>>> candidates = new(StringComparer.Ordinal);

    public void Add(string field, RuleDecision<T> decision)
    {
        if (!Enum.IsDefined(decision.Priority)) throw new ArgumentOutOfRangeException(nameof(decision));
        if (!candidates.TryGetValue(field, out var values)) candidates[field] = values = new();
        values.Add(decision);
    }

    public Dictionary<string, ResolvedField<T>> Resolve() => candidates.ToDictionary(pair => pair.Key, pair =>
    {
        // Stable ordering preserves configuration order for ties.
        var ordered = pair.Value.OrderBy(value => value.Priority).ToArray();
        return new ResolvedField<T>(ordered[0], ordered[1..]);
    }, StringComparer.Ordinal);
}
