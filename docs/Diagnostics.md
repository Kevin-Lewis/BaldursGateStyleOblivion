# Diagnostic Reports

Reports use the output ESP filename stem and are written to the configured persistent report directory. See [foundation settings](Foundation.md) for the destination, filters, and module switches. They inspect winning records without changing gameplay.

| Suffix | Records and original values |
|---|---|
| `.npcs.json` | NPC levels/offsets, scaling flags, limits, classes, races, factions |
| `.creatures.json` | Creature levels/offsets, scaling flags, limits, types, factions |
| `.leveled-creatures.json` | Flags, chance of nothing, entries, levels, counts, references, script and template |
| `.leveled-items.json` | Flags, chance of nothing, entries, levels, counts, references |
| `.weapons.json` | Type, damage, speed, reach, weight, health, value, flags, enchantment |
| `.armor.json` | Armor rating, weight, health, value, slots, equipment flags, heavy/light type, enchantment |
| `.spells.json` | Type, stored cost, skill level, flags, effect school, magnitude, duration, area, range, script |
| `.containers.json` | Flags, weight, script, inventory references and counts |

Each file contains a Records array sorted by FormKey. Every row includes FormKey, EditorID, Name where available, RecordType, SourcePlugin, WinningOverridePlugin, Original, Classification, ClassificationStatus, ModifiedFields, and Warnings.

SourcePlugin is the plugin that created the record. Original is the winning record's data before patching, not necessarily the original plugin's data. For actors, Original.Level is an offset when PCLevelOffset is true; otherwise it is the fixed level. Limits are reported as stored, without interpreting zero as an ordinary cap.

Actor Classification includes assigned dimensions with Selected and Superseded decisions, applied rules, priorities, and reasons. Unclassified lists dimensions without assignments. PowerTier is not inferred yet. Other record types have null Classification and an Unclassified status until their classification systems exist. ModifiedFields is empty for this report-only implementation; change tracking must be wired into future mutation modules.

Spell cost and flags are reported as stored; these are not calculated effective costs. Effect schools come from script-effect data when present, otherwise the winning magic-effect record. Flags preserve raw unknown bits through their string representation.

## Summary and Warnings

`.diagnostic-summary.json` includes effective plugin filters, classification/report-only settings, counts for all eight groups, player-level-offset actor counts, per-dimension actor classification coverage, modified-record count, and warnings.

UnresolvedFormKey warnings list non-null FormKey references that cannot resolve in the loaded order. They appear on the referring row and in the summary. This check does not validate reference types, script behavior, EDID effect links, or gameplay safety, and it does not repair records.

The earlier `.actors.json`, `.scaled-actors.json`, and `.actor-classifications.json` reports remain available. The leveled-creature report now uses the shared Records/Original structure instead of its previous LeveledCreatures array.

