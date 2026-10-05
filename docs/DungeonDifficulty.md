# Phase 6: dungeon identity

`dungeons.json` contains editable groups and dungeon FormKey overrides. The geographic editor shows dungeon controls under location details, plus a group JSON editor. Save/delete writes this single configuration file; rerun the patcher to apply changes.

BasePowerTier is the usual cap, not an average. Common entries below the cap receive weight 4, entries at the cap weight 2, and entries outside the common range retain weight 1. Existing Phase 4 weights remain the starting distribution. Count-one pools that would exceed 255 entries use private weight-band helpers, with approximately whole-percent band weights. Other native counts and no-spawn chances are preserved; unsupported branches are reported.

Initial caps: ordinary caves/mines 3, ruins and tougher magical/marauder lairs 4, vampire dungeons 5. BossTierModifier defaults to zero and affects only selection in existing Boss-identified pools; fixed named actors are never promoted. EnemyTierRange defaults to cap minus two through cap. Individual geographic cell overrides take priority over dungeon profiles, which take priority over geographic groups.

Door-connected rooms share a profile. Manual Cells lists can split or merge sites. Names, factions, creature families, available actors, quest links and encounter decisions are reported in `*.dungeons.json` and the geographic page. Faction and CreatureFamily are descriptive; available pool members remain authoritative. LootTierRange is stored for the later loot phase and makes no loot changes.

SpecialEncounterChance is optional and defaults to null: no extra rare mechanism. Explicit values select existing above-cap actors at the configured whole-percent chance among non-empty results. This requires count-one entries without nested no-spawn chances; unsupported cases remain preserved with a reason. No new actor families are introduced. A dungeon with no above-cap actors gets no added rare encounter.

Quest-associated encounters, scripted actors/lists, templates, UseAll, cycles, unresolved dependencies, exclusions and curated pools retain their safeguards. ReportOnly plans the same changes without applying gameplay records. EnableDungeonDifficulty disables dungeon integration; an individual profile with Enabled=false falls back to geographic behavior. Fresh-spawn testing remains necessary.
