using System.Text.RegularExpressions;

namespace BaldursGateStyleOblivion.Modules;

internal sealed record RealmEncounter(string Name, PoolWeights Weights);

internal static class RealmEncounters
{
    public static RealmEncounter? Select(string? cell, string? world, string? pool)
    {
        if (!Regex.IsMatch(pool ?? "", @"Daedra|Daedric|Dremora|Clanfear|^Kvatch.*Creature|^OblivionGate", RegexOptions.IgnoreCase)) return null;
        var location = $"{cell} {world}";
        if (Regex.IsMatch(location, @"Paradise|MQ15", RegexOptions.IgnoreCase)) return null;
        if ((location + " " + pool).Contains("Kvatch", StringComparison.OrdinalIgnoreCase))
        {
            var boss = (pool ?? "").Contains("Boss", StringComparison.OrdinalIgnoreCase) ||
                (cell ?? "").Contains("CitadelLord", StringComparison.OrdinalIgnoreCase) ||
                (pool ?? "").Contains("CountsChamber", StringComparison.OrdinalIgnoreCase);
            return boss ? new("Kvatch guardian", new() { CommonMaxTier = 3, StrongMaxTier = 5, Common = 10, Strong = 85, Rare = 5 })
                : new("Kvatch early invasion", new() { CommonMaxTier = 4, StrongMaxTier = 5, Common = 90, Strong = 9, Rare = 1 });
        }
        if (Regex.IsMatch(location + " " + pool, @"MQ10|MQ13|MQ14|MQ16", RegexOptions.IgnoreCase))
            return new("Late Main Quest invasion", new() { CommonMaxTier = 3, StrongMaxTier = 5, Common = 10, Strong = 70, Rare = 20 });
        if (location.Contains("Oblivion", StringComparison.OrdinalIgnoreCase) || (pool ?? "").StartsWith("OblivionGate", StringComparison.OrdinalIgnoreCase))
            return new("Ordinary Oblivion realm", new() { CommonMaxTier = 2, StrongMaxTier = 5, Common = 15, Strong = 80, Rare = 5 });
        return null;
    }
}
