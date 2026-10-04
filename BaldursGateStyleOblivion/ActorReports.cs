using BaldursGateStyleOblivion.Core;
using System.Text.Json;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion;

internal static class ActorReports
{
    public static void Write(IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherRun run)
    {
        var npcs = state.LoadOrder.PriorityOrder.Where(listing => listing.ModKey != state.PatchMod.ModKey).Npc().WinningContextOverrides().Where(context => run.Includes(context.Record.FormKey.ModKey))
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

        var creatures = state.LoadOrder.PriorityOrder.Where(listing => listing.ModKey != state.PatchMod.ModKey).Creature().WinningContextOverrides().Where(context => run.Includes(context.Record.FormKey.ModKey))
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

        run.WriteReport(".actors.json", new { NPCs = npcs, Creatures = creatures }, new JsonSerializerOptions { WriteIndented = true });
        run.Log($"Reported {npcs.Length} winning NPCs and {creatures.Length} winning creatures.");

        var scaledNpcs = npcs.Where(actor => actor.PCLevelOffset == true).ToArray();
        var scaledCreatures = creatures.Where(actor => actor.PCLevelOffset == true).ToArray();
        run.WriteReport(".scaled-actors.json", new
        {
            Reason = "PCLevelOffset flag is enabled; Level contains the player-level offset.",
            NPCs = scaledNpcs,
            Creatures = scaledCreatures
        }, new JsonSerializerOptions { WriteIndented = true });
        run.Log($"Reported {scaledNpcs.Length} player-level-dependent NPCs and {scaledCreatures.Length} creatures.");
    }

}

