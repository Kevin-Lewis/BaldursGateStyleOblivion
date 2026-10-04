using BaldursGateStyleOblivion.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using BaldursGateStyleOblivion.Classification;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Diagnostics;

internal sealed record DiagnosticWarning(string Code, string Record, string Reference, string Reason);
internal sealed record DiagnosticRecord(string FormKey, string? EditorID, string? Name, string RecordType,
    string SourcePlugin, string WinningOverridePlugin, object Original,
    ActorProfile? Classification, string ClassificationStatus, string[] ModifiedFields, DiagnosticWarning[] Warnings);

internal static class DiagnosticReports
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static void Write(IPatcherState<IOblivionMod, IOblivionModGetter> state,
        IReadOnlyDictionary<FormKey, ActorProfile> profiles, PatcherRun run, IReadOnlyDictionary<FormKey, string[]> changes)
    {
        var effects = state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).MagicEffect().WinningOverrides()
            .Where(effect => effect.EditorID is not null)
            .ToDictionary(effect => effect.EditorID!, effect => effect.Data?.MagicSchool.ToString(), StringComparer.Ordinal);
        var warnings = new List<DiagnosticWarning>();
        var counts = new Dictionary<string, int>();

        DiagnosticRecord Record(IMajorRecordGetter record, ModKey winner, string type, string? name, object original)
        {
            var missing = record.EnumerateFormLinks().Select(link => link.FormKey)
                .Where(key => !key.IsNull).Distinct()
                .Where(key => !state.LinkCache.TryResolve<IMajorRecordGetter>(key, out _))
                .OrderBy(key => key.ToString(), StringComparer.Ordinal)
                .Select(key => new DiagnosticWarning("UnresolvedFormKey", record.FormKey.ToString(), key.ToString(),
                    "Reference could not be resolved in the loaded order.")).ToArray();
            warnings.AddRange(missing);
            profiles.TryGetValue(record.FormKey, out var profile);
            return new(record.FormKey.ToString(), record.EditorID, name, type,
                record.FormKey.ModKey.ToString(), winner.ToString(), original, profile,
                profile is null ? "Unclassified" : profile.Dimensions.Count == 0 ? "Unclassified" : profile.Unclassified.Length == 0 ? "Classified" : "Partial",
                changes.GetValueOrDefault(record.FormKey, []), missing);
        }

        void Report(string suffix, IEnumerable<DiagnosticRecord> records)
        {
            var rows = records.OrderBy(record => record.FormKey, StringComparer.Ordinal).ToArray();
            counts.Add(suffix, rows.Length);
            run.WriteReport($".{suffix}.json", new { Records = rows }, Options);
            run.Log($"Diagnostics: {rows.Length} {suffix} records.");
        }

        var npcs = state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).Npc().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey)).ToArray();
        var creatures = state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).Creature().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey)).ToArray();
        Report("npcs", npcs.Select(c => Record(c.Record, c.ModKey, "NPC", c.Record.Name, new
        {
            Level = c.Record.Configuration?.LevelOffset,
            PCLevelOffset = c.Record.Configuration?.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset),
            MinimumLevel = c.Record.Configuration?.CalcMin, MaximumLevel = c.Record.Configuration?.CalcMax,
            Class = c.Record.Class.FormKeyNullable?.ToString(), Race = c.Record.Race.FormKeyNullable?.ToString(),
            Factions = c.Record.Factions.Select(f => new { FormKey = f.Faction.FormKey.ToString(), f.Rank }).ToArray()
        })));
        Report("creatures", creatures.Select(c => Record(c.Record, c.ModKey, "Creature", c.Record.Name, new
        {
            Level = c.Record.Configuration?.LevelOffset,
            PCLevelOffset = c.Record.Configuration?.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset),
            MinimumLevel = c.Record.Configuration?.CalcMin, MaximumLevel = c.Record.Configuration?.CalcMax,
            CreatureType = c.Record.Data?.Type.ToString(),
            Factions = c.Record.Factions.Select(f => new { FormKey = f.Faction.FormKey.ToString(), f.Rank }).ToArray()
        })));
        Report("leveled-creatures", state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).LeveledCreature().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey))
            .Select(c => Record(c.Record, c.ModKey, "LeveledCreature", null, new
            {
                Flags = c.Record.Flags?.ToString(), FlagsValue = (int?)c.Record.Flags,
                ChanceNone = c.Record.ChanceNone?.ToString(),
                Script = c.Record.Script.FormKeyNullable?.ToString(), Template = c.Record.Template.FormKeyNullable?.ToString(),
                Entries = c.Record.Entries?.Select(e => new { e.Level, Reference = e.Reference.FormKey.ToString(), e.Count }).ToArray()
            })));
        Report("leveled-items", state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).LeveledItem().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey))
            .Select(c => Record(c.Record, c.ModKey, "LeveledItem", null, new
            {
                Flags = c.Record.Flags?.ToString(), FlagsValue = (int?)c.Record.Flags,
                ChanceNone = c.Record.ChanceNone?.ToString(),
                Entries = c.Record.Entries?.Select(e => new { e.Level, Reference = e.Reference.FormKey.ToString(), e.Count }).ToArray()
            })));
        Report("weapons", state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).Weapon().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey))
            .Select(c => Record(c.Record, c.ModKey, "Weapon", c.Record.Name, new
            {
                Type = c.Record.Data?.Type.ToString(), c.Record.Data?.Damage, c.Record.Data?.Speed, c.Record.Data?.Reach,
                c.Record.Data?.Weight, c.Record.Data?.Health, c.Record.Data?.Value, Flags = c.Record.Data?.Flags.ToString(),
                Enchantment = c.Record.Enchantment.FormKeyNullable?.ToString(), c.Record.EnchantmentPoints
            })));
        Report("armor", state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).Armor().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey))
            .Select(c => Record(c.Record, c.ModKey, "Armor", c.Record.Name, new
            {
                c.Record.Data?.ArmorValue, c.Record.Data?.Weight, c.Record.Data?.Health, c.Record.Data?.Value,
                Slots = c.Record.ClothingFlags?.BipedFlags.ToString(), EquipmentFlags = c.Record.ClothingFlags?.GeneralFlags.ToString(),
                ArmorType = c.Record.ClothingFlags is null ? null
                    : c.Record.ClothingFlags.GeneralFlags.HasFlag(EquipmentFlag.HeavyArmor) ? "Heavy" : "Light",
                Enchantment = c.Record.Enchantment.FormKeyNullable?.ToString(), c.Record.EnchantmentPoints
            })));
        Report("spells", state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).Spell().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey))
            .Select(c => Record(c.Record, c.ModKey, "Spell", c.Record.Name, new
            {
                Type = c.Record.Data?.Type.ToString(), c.Record.Data?.Cost,
                SkillLevel = c.Record.Data?.Level.ToString(), Flags = c.Record.Data?.Flag.ToString(),
                Effects = c.Record.Effects.Select(e => new
                {
                    MagicEffect = e.Data?.MagicEffect.ToString(),
                    MagicSchool = e.ScriptEffect?.Data?.MagicSchool.ToString()
                        ?? (e.Data is null ? null : effects.GetValueOrDefault(e.Data.MagicEffect.ToString() ?? "")),
                    e.Data?.Magnitude, e.Data?.Duration, e.Data?.Area,
                    Range = e.Data?.Type.ToString(), ActorValue = e.Data?.ActorValue.ToString(),
                    ScriptEffectName = e.ScriptEffect?.Name,
                    Script = e.ScriptEffect?.Data?.Script.FormKey.ToString()
                }).ToArray()
            })));
        Report("containers", state.LoadOrder.PriorityOrder.Where(listing => run.IsInputPlugin(listing.ModKey)).Container().WinningContextOverrides().Where(c => run.Includes(c.Record.FormKey.ModKey))
            .Select(c => Record(c.Record, c.ModKey, "Container", c.Record.Name, new
            {
                Flags = c.Record.Data?.Flags.ToString(), c.Record.Data?.Weight,
                Script = c.Record.Script.FormKeyNullable?.ToString(),
                Items = c.Record.Items.Select(item => new { Reference = item.Item.FormKey.ToString(), item.Count }).ToArray()
            })));

        var dimensions = typeof(ActorValues).GetProperties().Select(property => property.Name);
        run.WriteReport(".diagnostic-summary.json", new
        {
            IncludedPlugins = run.Settings.IncludedPlugins,
            ExcludedPlugins = run.Settings.ExcludedPlugins,
            ActorClassificationEnabled = run.Settings.EnableActorClassification,
            ReportOnly = run.Settings.ReportOnly,
            RecordCounts = counts,
            PlayerLevelOffsetActors = new
            {
                NPCs = npcs.Count(c => c.Record.Configuration?.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset) == true),
                Creatures = creatures.Count(c => c.Record.Configuration?.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset) == true)
            },
            PlayerLevelOffsetActorsAfterPatch = new
            {
                NPCs = npcs.Count(c => c.Record.Configuration?.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset) == true)
                    - npcs.Count(c => changes.GetValueOrDefault(c.Record.FormKey, []).Contains("Configuration.Flags.PCLevelOffset")),
                Creatures = creatures.Count(c => c.Record.Configuration?.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset) == true)
                    - creatures.Count(c => changes.GetValueOrDefault(c.Record.FormKey, []).Contains("Configuration.Flags.PCLevelOffset"))
            },
            PlayerDependentActorsAfterPatch = new
            {
                NPCs = npcs.Count(c => c.Record.Configuration is { } config
                    && Modules.ActorDeleveling.IsPlayerDependent(config.Flags.HasFlag(Npc.NpcFlag.PCLevelOffset), config.CalcMin, config.CalcMax)
                    && !changes.ContainsKey(c.Record.FormKey)),
                Creatures = creatures.Count(c => c.Record.Configuration is { } config
                    && Modules.ActorDeleveling.IsPlayerDependent(config.Flags.HasFlag(Creature.CreatureFlag.PCLevelOffset), config.CalcMin, config.CalcMax)
                    && !changes.ContainsKey(c.Record.FormKey))
            },
            ActorClassificationCoverage = dimensions.ToDictionary(name => name,
                name => profiles.Values.Count(profile => profile.Dimensions.ContainsKey(name))),
            ActorDelevelingEnabled = run.Settings.EnableActorDeleveling && run.Settings.EnableActorClassification,
            ModifiedRecords = changes.Count,
            WarningCount = warnings.Count,
            Warnings = warnings.OrderBy(warning => warning.Record, StringComparer.Ordinal)
                .ThenBy(warning => warning.Reference, StringComparer.Ordinal).ToArray()
        }, Options);
        run.Log($"Diagnostic warnings: {warnings.Count}");
    }

}

