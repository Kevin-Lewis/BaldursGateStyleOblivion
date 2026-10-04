# Record Discovery

Phase 1 stores reusable reports and findings before any deleveling. The reports describe winning records; runtime behavior is not simulated.

Reports use the configured persistent directory and ESP filename stem. EnableRecordDiscovery defaults to true and requires EnableDiagnostics. Inclusion/exclusion filters select report subjects by originating plugin. Reference lookup and inbound usage retain full-load-order context.

| Report suffix | Purpose |
|---|---|
| `.scaling-audit.json` | Actor level modes, raw configuration, stored stats, auto-calculation flag, combat styles; list flags, thresholds, counts, nesting |
| `.list-dependencies.json` | Direct nested lists and missing entry references |
| `.reference-index.json` | Relevant records with their direct FormKey users, including placed actors and objects |
| `.actor-inventories.json` | NPC/creature inventories, spell links, death-item lists, attached scripts |
| `.merchants.json` | Service flags, barter gold, personal inventory, explicit placed-NPC merchant-container links and stock |
| `.classes.json` | Attributes, skill selections, specialization, services and training |
| `.factions.json` | Flags, ranks and faction relations |
| `.combat-styles.json` | Stored combat parameters, including advanced settings |
| `.quests.json` | Attached scripts, stages, log entries, result scripts, conditions, target references |
| `.scripts.json` | Standalone script source availability, source text, compiled size, FormKey references and source signals |
| `.script-candidates.json` | Standalone, quest-stage and dialogue script grant/level/stage-change candidates; executable scripts missing source |
| `.discovery-summary.json` | Counts, script coverage, detected list-cycle back edges and limitations |

Existing `.containers.json` and `.leveled-items.json` reports supply container contents and item-list detail. Use the reference index to connect inventory entries to their users, then list dependencies to follow nested lists. The index is direct usage, not a computed transitive closure. Scripts using literal IDs or indirect runtime functions may have no resolvable FormKey link.

Discovery rows contain Record identity and Original data. UsedBy in the reference index includes users from excluded plugins because they can still depend on an included record. Cell/worldspace grouping links are skipped to avoid incorrectly attributing descendants' links to their parents; dialogue-topic quest links are read separately from child dialogue records. This is not a geographic placement hierarchy yet.

## Interpretation

- PlayerLevelOffset describes the stored actor flag; LevelOffset, CalcMin and CalcMax retain their raw values. FixedLevel describes the absence of the offset flag, not a guarantee that scripts or special engine behavior never change an actor. The player base record is a special case.
- AutoCalcStats is separate from the offset flag. Stored stats are not guaranteed to equal runtime stats. No generated values are calculated.
- HasEntriesAboveLevelOne is a threshold observation, not proof that the whole list varies with player level. Entries at level one can still reference nested lists or actors with scaling flags.
- UseAll and the two calculation flags are reported independently, including unusual combinations. Selection probabilities, threshold handling, counts, and respawn semantics need focused runtime validation before conversion.
- Service flags can identify trainers, repairers and recharge providers as well as item merchants. An explicit MerchantContainerReference is stronger evidence for stock storage. It resolves from placed NPC to placed object to container base. A base actor may have multiple placements.
- GrantCandidate means source contains AddItem, AddItemNS or AddSpell. PlayerLevelRead requires a literal player.GetLevel receiver; other GetLevel calls are ActorLevelRead. Quoted strings and semicolon comments are ignored. SetStage is a quest-progress signal, not a reward by itself.
- Signals retain line numbers and source lines. They do not prove execution, reward intent, control flow, effective item variants, or whether an indirectly referenced actor is the player.
- Embedded fields with no source, compiled bytes or references are empty script slots, not missing-source warnings. Compiled bytecode is not disassembled. Source reports may contain mod-authored script text.
- Conditions retain function, operator, flags, comparison value and raw parameters. They are not evaluated or assigned player-level semantics automatically.
- Cycle back edges require review before recursive list expansion. They are not automatically repaired or declared quest-breaking.

## Evidence and Remaining Validation

Installed Mutagen 0.54.4 record APIs and the current plugin data provide the field snapshots. xEdit's [Oblivion definitions](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.6/Core/wbDefinitionsTES4.pas) independently identify the actor offset/configuration fields and the placed-NPC XMRC merchant-container field. This corroborates record structure, not engine execution.

See [initial findings](Findings.md) for examples from the tested load order. Remaining work includes in-game checks at multiple player levels, cap-zero semantics, nested-list selection behavior, merchant restocks, and manual review of important quest reward candidates. Reports are an investigation tool, not a declaration that Phase 1 runtime validation is complete.

For the development baseline, run tests/VerifyDiscovery.ps1 after generating reports. It verifies count consistency, inventoried actors, merchant/reference connections, source scanning, and cyclic/acyclic graph handling. The merchant example assumes DLCFrostcrag is loaded.
