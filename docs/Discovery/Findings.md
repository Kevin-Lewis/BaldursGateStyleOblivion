# Initial Discovery Findings

Baseline: 2026-10-03, the installed Oblivion/DLC load order used for development (12 loaded plugins). These counts are observations from this run, not universal game constants.

## Coverage

| Subject | Count |
|---|---:|
| NPCs and creatures with original inventory reports | 3,636 |
| Actors with the PCLevelOffset flag | 1,993 |
| Leveled creature and item lists | 2,183 |
| Lists with entries above level one | 1,255 |
| Lists with direct nested-list references | 777 |
| Relevant targets in the used-by index | 19,998 |
| NPCs with service flags or merchant-container links | 266 |
| NPCs with a resolved explicit merchant-container link | 110 |
| Classes | 112 |
| Factions | 495 |
| Combat styles | 129 |
| Quests | 414 |
| Standalone script records | 3,010 |
| Nonempty standalone/quest-stage/dialogue script sources inspected | 10,951 |
| Source-signal candidate contexts | 3,737 |

No nonempty script context lacked source in this baseline. This does not imply that source is available in every mod or that scripts have been fully analyzed.

## Representative Connections

Aurelinwae demonstrates why merchant stock needs placement analysis:

```text
001246:DLCFrostcrag.esp  Aurelinwae (base NPC)
  -> 00124A:DLCFrostcrag.esp  AurelinwaeRef (placed NPC)
  -> 00124D:DLCFrostcrag.esp  merchant container reference
  -> 00124C:DLCFrostcrag.esp  container base and original stock
```

Her personal inventory and container stock differ. The stock container is reached through the explicit merchant-container field, rather than inferred from its name. All merchant-container links found in this baseline resolved to container bases.

`NDGarlasSpellTrap01SCRIPT` (`000D79:Knights.esp`) includes literal player.GetLevel comparisons against several thresholds. `ARTrapEvilStoneFAST01SCRIPT` (`001BA0:Oblivion.esm`) provides another example. These are confirmed source-code observations: runtime branches and resulting effects have not been tested. They show why auditing actor level flags alone cannot identify every player-level-dependent system.

`SE14GSEscortList` (`079C90:Oblivion.esm`) has a direct self-reference at entry level one. The list-dependency traversal records this back edge and terminates safely. It requires context/script review before any conversion; no claim is made that the associated quest is broken, and the record is unchanged.

The earlier Knights Holy Aura/Woodland Grace unresolved-script-reference warnings remain unchanged. They are input-data findings, separate from the discovery module.

## Before Deleveling

- Review representative actors with and without PCLevelOffset and AutoCalcStats. Confirm runtime levels/stats at multiple player levels, including zero maximum values and special actors.
- Review nested lists with multiple thresholds and each flag combination. Confirm selection, counts, UseAll behavior, and respawn/restock behavior in-game before designing conversions.
- Review merchant service-only actors separately from merchants with explicit stock-container links.
- Review important quest-stage, dialogue, and attached-script grant candidates manually. Establish which grants are rewards, how variants are chosen, and whether changes require script-aware handling.
- Trace important shared records through UsedBy before modifying them. Reference data establishes dependencies, not execution order or quest safety.

Reporting infrastructure and the initial baseline are implemented. This does not complete the remaining engine validation or manual quest-reward audit.
