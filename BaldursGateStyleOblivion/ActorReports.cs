using System.Text.Json;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion;

internal static class ActorReports
{
    public static void Write(IPatcherState<IOblivionMod, IOblivionModGetter> state)
    {
        var npcs = state.LoadOrder.PriorityOrder.Npc().WinningContextOverrides()
            .OrderBy(context => context.Record.FormKey.ToString(), StringComparer.Ordinal)
            .Select(context =>
            {
                var actor = context.Record;
                var config = actor.Configuration;
                return new
                {
                    FormKey = actor.FormKey.ToString(),
                    actor.EditorID,
                    actor.Name,
                    SourcePlugin = actor.FormKey.ModKey.ToString(),
                    WinningOverridePlugin = context.ModKey.ToString(),
                    Level = config?.LevelOffset,
                    PCLevelOffset = config?.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset),
                    MinimumLevel = config?.CalcMin,
                    MaximumLevel = config?.CalcMax,
                    Class = actor.Class.FormKeyNullable?.ToString(),
                    Race = actor.Race.FormKeyNullable?.ToString(),
                    Factions = actor.Factions.Select(faction => new
                    {
                        FormKey = faction.Faction.FormKey.ToString(),
                        faction.Rank
                    }).OrderBy(faction => faction.FormKey, StringComparer.Ordinal).ToArray()
                };
            }).ToArray();

        var creatures = state.LoadOrder.PriorityOrder.Creature().WinningContextOverrides()
            .OrderBy(context => context.Record.FormKey.ToString(), StringComparer.Ordinal)
            .Select(context =>
            {
                var actor = context.Record;
                var config = actor.Configuration;
                return new
                {
                    FormKey = actor.FormKey.ToString(),
                    actor.EditorID,
                    actor.Name,
                    SourcePlugin = actor.FormKey.ModKey.ToString(),
                    WinningOverridePlugin = context.ModKey.ToString(),
                    Level = config?.LevelOffset,
                    PCLevelOffset = config?.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset),
                    MinimumLevel = config?.CalcMin,
                    MaximumLevel = config?.CalcMax,
                    Factions = actor.Factions.Select(faction => new
                    {
                        FormKey = faction.Faction.FormKey.ToString(),
                        faction.Rank
                    }).OrderBy(faction => faction.FormKey, StringComparer.Ordinal).ToArray()
                };
            }).ToArray();

        var path = Path.ChangeExtension(state.OutputPath.ToString(), ".actors.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path);
        JsonSerializer.Serialize(output, new { NPCs = npcs, Creatures = creatures },
            new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Reported {npcs.Length} winning NPCs and {creatures.Length} winning creatures: {path}");
    }
}


