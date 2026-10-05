# Phase 4: static encounter pools

The creature-list module inspects every winning leveled creature list. Coverage includes ordinary habitats, dungeon factions, Daedra, Shivering Isles creatures, and individually reviewed quest/template encounters. Unknown scripts, UseAll composition, missing references and cyclic pools remain guarded. In-game validation uses fresh spawns.

## Editing

Open **Edit Actor Tiers.cmd**, then click **Creature list editor**. The companion page is `/lists` on the same local editor server. It provides search, paging, purpose/status filters, and override-only/non-override filters. Save and Delete override update the configuration immediately. Run the patcher afterward; reload the page to refresh the **Last run** column. Saving configuration does not regenerate the ESP.

All encounter decisions are in `BaldursGateStyleOblivion/creature-lists.json`, separate from actor tier assignments:

- `ReviewedScripts`: individually reviewed script FormKeys and implementation fingerprints (source, compiled bytes and references). Changed implementations require renewed review.
- `Weights`: default tier bands and relative weights.
- `Groups`: named encounter-family rules matched against EditorID regular expressions.
- `PluginRules`: rules restricted by `SourcePlugin`, evaluated before groups.
- `FormKeyOverrides`: individual list definitions, evaluated before other rules.
- `Fallback`: `Preserve` by default. Unknown mod-added lists remain unchanged until a matching group, plugin rule, or individual override exists.

The first matching enabled plugin/group rule wins within its category. Source plugin means the originating plugin, not the winning override plugin. Existing global plugin inclusion/exclusion settings apply to this module too.

| Policy | Behavior |
| --- | --- |
| Preserve | Retain the original list and its selection behavior. |
| StaticPool | Set entry requirements to level 1, including safe nested dependencies. Retain original multiplicities and actor counts. |
| WeightedPool | Remove gates and apply tier-band weights to level-dependent pools. Existing random-only pools retain their distribution unless an individual WeightedPool override requests reweighting. Incomplete tier evidence or multi-actor counts fall back to StaticPool. |
| CuratedPool | Use the individually specified references, relative integer weights, and actor counts. |

The starting weights are common (tiers 0–3): **70**, strong (4–5): **25**, rare (6–10): **5**. These are relative weights, normalized over bands that actually exist in each list. Within a band, distinct name/role/tier groups receive equal shares. Cosmetic variants share their group's probability instead of multiplying it. Nested groups use the highest classified descendant tier, a conservative starting assumption that can be replaced with a curated pool. Automatic pools use 100 integer selection tickets; rounding and a minimum ticket per group can make percentages approximate.

For curated pools, **Weight** repeats an entry to control its probability. **Count** retains native actor quantity; it is not a probability weight. The sum of curated weights cannot exceed 255 entries. References must resolve to live NPCs, creatures, or creature lists. The editor starts its curated entry table from the original pool; it does not silently copy the previous generated pool.

Example of an individual static pool:

```json
"FormKeyOverrides": {
  "0343DD:Oblivion.esm": {
    "Name": "LL1WildernessForest",
    "Policy": "StaticPool",
    "Reason": "Use the original forest entries with equal, player-independent eligibility."
  }
}
```

A curated definition uses `"Policy": "CuratedPool"` plus `Entries`, for example `[{ "Reference": "025314:Oblivion.esm", "Weight": 3, "Count": 1 }]`. Add only the actor/list references you want in that encounter.

`AllowSpecial` permits an individually reviewed quest/template list to be processed. It does not bypass script review, UseAll, missing-reference, cycle, or excluded-plugin safeguards. Attached list and template scripts must match a ReviewedScripts fingerprint. The reviewed callbacks retain quest counters, resurrection, boss phases, packages and actor-level effects; scripts are never rewritten. A generic list shared with a preserved parent is kept intact; safe parent branches can point to private static copies. Editing that child's override therefore affects eligible copied branches, not its preserved parents. Review a parent definition when you want to change that encounter specifically.

## Output and verification

`BaldursGateStyleOblivion.creature-list-deleveling.json` reports all incoming lists, their purpose and rule, original/planned entries, direct users and cell associations, modified fields, guard reasons, generated helper pools, and remaining direct or nested level gates. Cell associations are observations, not location difficulty assignments.

For each accepted encounter, `SelectionChecks` calculates its leaf selection probabilities at player levels **1, 10, 25, and 40**, including nested spawn chances. These checks describe one list selection; actor quantity/per-count behavior remains native. They do not simulate quest scripts or prove game-engine behavior. Actual cell encounters still need in-game testing with fresh spawns.

Existing ChanceNone, entry counts, per-count flags, scripts, templates, and unrelated record fields are preserved. Helper pools implement probabilities through repeated level-one entries and nested variant selection. Leveled item lists, actor stats, geographic difficulty, and loot balance are outside this module.

`EnableCreatureListDeleveling` controls the module. `CreatureListConfigurationFile` selects an alternate file. ReportOnly builds and reports the same plans but adds no list overrides or helper records to the output.

Validation commands:

```powershell
dotnet run --project tests/CreatureLists/CreatureLists.Tests.csproj
# Verify the written lists against their generated report:
dotnet run --project tests/CreatureLists/CreatureLists.Tests.csproj -- verify <patch.esp> <creature-list-deleveling.json>
node tests/VerifyCreatureListEditor.cjs
```

The expanded local run changes 425 original lists and generates 206 helper pools. Of 701 incoming lists, 499 were flagged as directly/nested level-dependent or cyclic; 497 now have verified player-independent selection. The two preserved exceptions are `025E8B:Oblivion.esm` (unused TestZombieLeveled) and `079C90:Oblivion.esm` (self-referencing SE14GSEscortList, flagged conservatively because its cycle cannot be evaluated). 198 selected lists already have static selection and need no override. Counts describe this load order.

Written patch checks compare planned entries, counts, scripts, templates, flags and spawn chance with the ESP. All accepted pool distributions match at levels 1, 10, 25 and 40. Fresh-spawn gameplay testing should cover wildlife, dungeon factions, Oblivion gates/Kvatch, Knights of the Nine and Isles quest encounters. Existing spawned actors do not establish fresh list behavior.


## Oblivion realm weighting

`RealmWeights` in `BaldursGateStyleOblivion/creature-lists.json` controls Kvatch ordinary enemies, Kvatch guardians, ordinary realms, and late Main Quest invasions. `CommonMaxTier` and `StrongMaxTier` define the three bands; `Common`, `Strong`, and `Rare` divide selection weight between bands present in each pool. Nested pools and missing bands mean these are not guaranteed percentages for an entire dungeon.

Realm selection uses private copies of existing static pools. Shared source lists, actor tiers, scripts, templates, Chance None and native actor counts remain intact. Existing script fingerprints must match; changed scripts, UseAll, excluded dependencies and manually curated pools remain guarded. Individual geographic or dungeon overrides take priority. Paradise retains its existing dedicated encounter identity; Sigil Stone rewards are deferred.

Each geographic encounter report includes the applied realm rule and `RealmWeights`. These changes require regenerating the patch and testing fresh encounters.
