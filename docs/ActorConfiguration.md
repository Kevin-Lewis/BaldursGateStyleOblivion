# Actor Tier Editor



Double-click **Edit Actor Tiers.cmd** in the repository root. It opens the local browser editor. Keep its terminal window open while editing.



Everything is saved in **BaldursGateStyleOblivion/actor-classification.json**:



- `ResearchModel`: OpenAI API model, currently `gpt-6-luna`.

- `Groups`: group rules, organized by actor family.

- `FormKeyOverrides`: individual actors, with their names, tiers, handling, concise AI notes, and source links.



There are no approval states or acceptance commands. A saved tier is an assignment. Individual assignments take precedence over group rules.



## Editing



In the actor table, enter a tier from 0–10 and click Save. Change Handling when needed. Leave an individual tier or handling blank to use the group rules. Existing descriptions, reasons, sources, and other classification fields are preserved. **Delete override** removes the entire individual entry, including its notes and other overrides, and restores automatic rules.



Expand **Edit group rules** to set a group's tier and enable or disable a rule. Search and filter the actor table to inspect the result. Narrower rules at the same priority must appear first. Matching conditions can be edited directly in the JSON. `All` requires every listed condition; `None` excludes an actor when any listed condition matches.



You can also open the JSON and change `PowerTier` directly. Entries have Name next to PowerTier so they are easy to find. After external JSON edits, refresh the browser editor. Invalid tiers or configuration are rejected before the file is replaced.



The generated `artifacts/actor-catalog.html` remains a read-only snapshot; the local editor is the version with save controls. It listens only on this computer at `http://127.0.0.1:5078`.



Class baselines provide provisional tiers for ordinary named actors when no specific rank rule applies. They are estimates of occupational training, not final personal lore assessments. Prominent actors and voice/test helpers are excluded from these baselines. Species and rank rules, plus individual overrides, remain more specific.



## Patcher configuration



The development run in `artifacts/phase0-data` is configured to read this exact JSON file through `ActorConfigurationFile`, so saves take effect on its next patcher run without rebuilding. In Synthesis, set that field to this file's absolute path, or place the single JSON in the patcher's extra-data directory. A blank field uses the extra-data copy when present, otherwise the bundled copy.



Actor deleveling now uses the saved tiers. Enable `EnableActorClassification` and `EnableActorDeleveling`, and set `ReportOnly` to `false` to write changes. Report-only mode produces the same decisions without modifying actors. Development settings already enable gameplay output; separate Synthesis settings may still use report-only defaults.

`LevelMapping` in the actor JSON maps tiers 0-9 to **1, 2, 5, 10, 15, 20, 25, 30, 35, 40**. Tier 10 has no shared level: scalable Apex actors require an individual `FixedLevel`; already-fixed Apex actors retain their original level. Jyggalag's encounter variants initially use level 45.

Edit **Level override** beside Tier in the main table, then Save. Expand actor details to edit **Deleveling**. Blank fixed level uses the tier mapping. Exempt preserves the actor. Explicitly include permits helper and special handling exceptions while still protecting the player and detected script scaling. JSON fields are `FixedLevel` (1-32767) and `Delevel` (true/false; omit for automatic).

Eligible actors receive the mapped fixed level. NPC offset flags and active bounds are cleared when changed. Previously scaled creatures retain their offset flag, use an offset of zero, and set both minimum and maximum to the assigned level. This fixes their actual level while preserving the engine's native health, magicka, fatigue, damage and skill calculations. Already-fixed creatures keep their original stat interpretation. Stored stats, spells, inventory and unrelated flags are preserved. Named actors already fixed without a curated tier are preserved. Quest actors, leaders and bosses require a curated assignment or explicit inclusion. Helpers and detected runtime scaling are skipped by default.

`*.actor-deleveling.json` reports original levels, targets, handling, reasons and modified fields. `WouldModify` means report-only; `Modified` means an override was written. `*.actor-scaling-paths.json` reports remaining player-dependent creature lists and potential script scaling. Diagnostic summaries separate offset-flag counts from actual player-level dependence. `PlayerDependentActorsAfterPatch` is the effective dependence count; retained creature offset flags with equal bounds do not indicate ongoing scaling.

In-game validation remains necessary: compare fresh encounters at different player levels, including quest bosses and both DLCs. Already-spawned actors can retain saved state. Leveled creature lists still affect encounter selection; this pass does not transform them.



## Research



The utility writes new researched actors directly into the same file. Existing actor entries are never overwritten, including when research is refreshed. Remove an entry if you want it generated again.



New research focuses on lore; record identity identifies the intended actor form. Research entries include their model identifier and consulted source links. Search the editor for `gpt-6-luna` to review the researched actors.



UESP articles are read through its public wiki API, with text, canonical URL, and revision cached. Disambiguation pages follow the person link. UESP-only web search can supplement missing evidence. Sources must come from retrieved articles or consulted search results. Missing evidence leaves PowerTier unassigned, so automatic rules remain active.



Matching variants can share one researched assessment. They remain separate FormKey entries and can be edited individually. The remaining-actor pass and its representative-to-variant mapping are recorded in `artifacts/luna-remaining-review.json`.



Developer commands, when needed:



```powershell

dotnet run --project tools/ActorResearch -- edit

dotnet run --project tools/ActorResearch -- catalog

dotnet run --project tools/ActorResearch -- research --formkeys research/calibration-actors.txt

```



`--parallelism` controls concurrent lookups (1-8, default 4). `--source-page` selects a verified UESP article title when an exact display-name lookup identifies the wrong form.



The model can be changed in `ResearchModel`. API access reads `OPENAI_API_KEY` from the process or Windows user environment without printing it. Source and research caches are under `research/cache`, separate from the editable configuration. API keys stay out of JSON and chat.


## Guarded actor review

The 108 remaining player-offset actors were reviewed individually: 37 received initial fixed assignments and 71 were explicitly exempted. Exemptions include test and recording records, unused actors, corpses, noncombat soul representations, Haskill, Dyus, Rona Hassildor, and Audens Avidius's scripted class/level transition. Each entry stores its concise decision in `DelevelingReview`, visible in editor details.

Corpserot grummites and Hears-Voices-In-The-Air are real encounters, not corpse/voice helpers. Caminalda's vanilla MG04 script grants a constant health bonus; the player-level branch changes Arielle's equipment. That exact reviewed script source is exempt from the scaling guard; modified versions are reviewed conservatively. Leveled encounter lists remain unchanged.

## Phase 3 stat calculation preservation

Creature offset mode changes the meaning of stored resource and combat values. We therefore fix formerly scaled creature levels using equal nonzero bounds instead of clearing the flag and treating those coefficients as absolute stats. The actor deleveling report marks these decisions `FixedBoundsNativeCalculation`; its summary reports `FixedBoundOffsetActors` separately from `RemainingPlayerDependentActors`. Report-only mode shows proposed changes without applying them.

NPC auto-calc settings remain unchanged. No health compression, damage rebalance, new stat formulas, or encounter-list changes are introduced here. In-game checks should compare fresh creature instances at different player levels and verify both level and combat values. Existing save instances and scripted exceptions still require separate validation.

Reference: [Construction Set creature flags](https://cs.uesp.net/wiki/Category:Creatures) and [creature stats](https://cs.uesp.net/wiki/Stats_Tab_-_Creatures).

## Run from Visual Studio

Select the **BaldursGateStyleOblivion** startup project and the **Oblivion - Install patch** launch profile. Press F5 or Ctrl+F5. It reads the active Oblivion load order and the development settings, and writes `BaldursGateStyleOblivion.esp` directly into the configured game Data folder. The previous output is excluded from input, so repeated runs rebuild from original winning records. Reports remain in `artifacts/phase0-data/Reports`.

The plugin is enabled in this machine's load order. Close the game before rebuilding; restart the game afterward to load the new patch. The launch profile contains this development machine's local paths.

## Creature list editor

The same local editor now has a **Creature list editor** link. Encounter policies and weights save to `BaldursGateStyleOblivion/creature-lists.json`. See [CreatureLists.md](CreatureLists.md) for the Phase 4 rollout, overrides, and diagnostics.


## Equipment distribution (Phase 9)

`equipment.json` is the single equipment configuration. The editor's **Equipment** link opens a paged actor table with profile overrides and delete buttons. Expand **Edit profiles, group rules, and list/item overrides** to edit the same JSON directly. Rerun the patcher after saving; inventory details show the last patcher run.

- `Profiles`: relative quality weights and `EnchantedPercent`. Missing materials are not added to ordinary pools. Each quality band gets its own share, so having many enchanted variants does not inflate that band's total weight.
- `EbonyPerThousand`: separate material chance, normally **1/1000** for `HighQuality` and `Elite`, **0** elsewhere. Glass stays in the `Elite` quality band; Ebony rarity is independent of Glass.
- `DaedricPerThousand`: **750/1000** for `HighDremora`, **0** for mortal and lower-rank profiles. Markynaz, Valkynaz, and named story Dremora Kathutet, Orthe, and Ranyu select this profile when their existing equipment lists offer Daedric items. Named characters' fixed gear is preserved.
- `Groups`: first matching rule wins, with plugin-specific rules considered before general rules. Matches inspect actor ID, faction, class, classification, and observed directly placed NPC locations. Wealth/occupation rules take priority over the actor-tier fallback.
- `TierProfiles`: fallback profile by `EquipmentTier`, or `PowerTier` when no equipment tier is assigned.
- `ActorOverrides`: FormKey entries with `Profile` and/or `Preserve`. These win over automatic actor rules. Delete an entry to resume automatic rules.
- `ListOverrides`: FormKey entries selecting a profile or preserving a list; preservation also protects nested dependencies. List-specific profiles win for that inventory slot.
- `ItemOverrides`: explicit quality classification by item FormKey, particularly useful for mod-added materials. `Artifact` protects the item rather than putting it into automatic pools.

Example actor override:

```json
"ActorOverrides": {
  "012345:Example.esp": { "Name": "Example captain", "Profile": "HighQuality" }
}
```

Automatic distribution changes pure equipment lists carried by actors, using private LVLI copies and actor inventory redirects. Original lists remain available unchanged to merchants, containers, and quest rewards. Chance None, item counts, entry metadata, and per-count flags are retained. Precious-only pools receive compatible mundane gear for their ordinary outcomes, preventing a restricted Daedric-only pool from defeating the rarity policy. No weapon damage, armor ratings, enchantments, or item values are edited.

Mixed loot/consumable lists, test actors, quest-item equipment, scripted items, quest/test lists, UseAll bundles, and equipment branches referenced by inventory-managing scripts are retained with a reason. Scripts that manipulate whole inventories or unresolved dynamic operands retain all equipment; scripts touching only unrelated fixed tokens or other loot do not block equipment changes. Fixed unique equipment and faction uniforms remain intact. The equipment report records every actor-held list, the chosen rule/profile, planned references, fixed equipment, and preserved exceptions. Existing saved inventories need a fresh spawn or inventory reset.

The engine's recursive list selection and Chance None behavior are documented in the [OBSE leveled-list reference](https://obse.silverlock.org/obse_command_doc.html#CalcLeveledItem). All generated eligibility levels are 1; weapon/armor types and native count behavior are preserved while the distribution becomes independent of player level.

Race preferences are a strong bias within an already selected `HighQuality` band: Orcs favor Orcish, and High Elves favor Elven equipment. Matching options get eight times their normal selection weight within their existing branch (`RaceMaterialWeight: 8`, editable from 1–16). The preference is capped when necessary to stay within the native 255-entry limit. `RaceMaterials` maps race EditorIDs to material names; `PreferRaceMaterial: false` disables this for a profile (military and high Dremora defaults). Individual list definitions take precedence. No new equipment types are added, and quality, enchantment, Ebony, and Daedric rates do not increase. Orcish is explicitly classified as `HighQuality`.


## World loot

`loot.json` controls Phase 10. The editor has a **World loot** page at `/loot`, with search, paging, tier/profile sorting, individual override save/delete, and the full configuration editor. Save changes and rerun the patcher to apply them.

Placed containers use the current geographic range, or a dungeon's `LootTierRange` maximum / `BasePowerTier` cap when no cell override applies. Container purpose chooses the profile: household barrels, sacks and cupboards stay modest; treasure/boss containers get a configurable one-tier bonus. Ordinary carried/death loot uses actor power. Pure equipment branches stay under the equipment module; equipment inside mixed loot families follows that family’s loot profile.

Profiles use `PremiumPerThousand`, `GlassPerThousand`, `EbonyPerThousand`, and `DaedricPerThousand`. These are separate chances per source-list selection, only for items already present in that source family. Defaults for danger tiers 2–6 give premium opportunities of 1%, 2.5%, 5%, 8%, and 12%; Ebony opportunities of 0%, 0%, 0.1%, 0.2%, and 0.3%. Daedric profiles allow a 0.1% Daedric opportunity at tiers 5–6, while ordinary profiles allow none. Rare endgame tiers 7/8/9/10 use premium opportunities of 16%/20%/24%/28% and ordinary-context Ebony opportunities of 5%/10%/20%/35%. Daedric-context profiles shift half that Ebony opportunity to Daedric gear: Ebony is 2.5%/5%/10%/17.5% and Daedric is 5%/10%/20%/37.5%, preserving the combined material chance. These material rates require matching items in the original source pool; ordinary profiles never add a Daedric opportunity. Premium-only families add empty outcomes to enforce the profile rarity rather than guarantee expensive gear. Native empty chances can reduce the actual result; repeated rolls can increase the whole-container chance.

`TierProfiles` maps danger tiers to profiles. `Groups` classify purpose and can assign a fixed profile or preservation policy; plugin-specific groups run first. `Overrides` accepts a placed-container FormKey, container base FormKey, or actor FormKey (placement overrides win over base overrides). `ListOverrides` assigns or preserves an individual source list. `ItemTiers` assigns an item's loot class: 0–3 ordinary, 4–10 premium. Glass/Ebony/Daedric remain under their separate material rules.

The patcher creates private static lists and, where necessary, private container bases. Original shared lists and containers remain available to merchants and other uses. Inventory quantities, fixed contents and item stats remain intact. Script/quest references, scripted items, merchant stock, helper storage, unresolved dependencies, cycles and UseAll bundles are retained with reasons. No new artifacts or additional loot rolls are introduced.

The `.world-loot.json` report lists each container placement and actor, its location/profile/rule, preserved branches, redirects and expected item yield. Whole-inventory premium/rare odds and expected base value cover processed branches only; fixed and preserved contents are excluded. Base value is not sale proceeds, and the current value estimate excludes soul gems. Existing inventories need a fresh spawn or normal reset to show changes. `EnableWorldLoot`, `LootConfigurationFile`, plugin filters and report-only mode follow the other modules.

Glass equipment uses its own material branch, excluded from general premium loot. Default Glass opportunities for tiers 3–10 are 0.2%, 1%, 2%, 5%, 12%, 20%, 30%, and 37%. `Daedric10` uses 15% Glass to keep Daedric gear dominant; its existing Ebony and Daedric rates remain intact. Lower tiers and household profiles have no Glass opportunity. Rates apply only to available source items; the report/editor show the actual processed-inventory Glass chance after nested empty chances and multiple rolls.

## Quest rewards and artifacts (Phases 13–14)

Edit `BaldursGateStyleOblivion/rewards.json`, or open the **Rewards / artifacts** editor at `/rewards`. The file contains curated `Lists`, reviewed `Scripts`, and `Artifacts`. Run the patcher after saving. Existing acquired rewards are not retroactively replaced.

- `SelectionLevel` is a constant selection benchmark: choose the version vanilla would give at that level, regardless of the actual player level. It is not a PowerTier or actor level.
- `Variant` optionally selects an exact direct member of that reward list. Clearing it returns to benchmark selection.
- `Count` optionally fixes the quantity for a single reward outcome. Clearing it preserves native quantity.
- `Preserve: true` keeps the original list or script behavior, including any scaling.
- `Fingerprint` protects a reviewed source list/script from unexpected mod changes. Leave it unchanged; a mismatch is reported and preserved rather than overwritten.

Defaults deliberately differ by reward: early Brotherhood bonuses and ordinary bounties remain modest; later guild rewards are stronger. Major relics such as Mehrunes' Razor, Dawnfang/Duskfang, the Crusader relics and the Necromancer's Amulet use their strongest existing versions. The Crusader sword's initial placement is fixed as well. Day/night transformations, powered forms, quest choices, grant counts, timers and native empty chances remain intact. Sigil Stones use the static Transcendent effect pool. Fixed Daedric artifacts retain their existing records.

Artifact metadata includes optional `ArtifactTier`, `Unique`, `QuestRelated`, `Daedric`, `HandTuned`, and `ExcludedFromNormalization`. Tier is descriptive metadata only; it applies no stat formula. Exclusion defaults to true and is enforced by equipment and world-loot builders, including mundane equipment fallback selection. The registry includes named unique reward variants as well as true artifacts. No artifact damage, armor, enchantment, value or script attached to the item is normalized.

The `.quest-rewards.json` report contains original/planned entries, grant-script evidence, selected benchmarks, protections and retention reasons. Mod-added rewards require explicit definitions; unreviewed scripts remain auditable. Quest entry requirements and encounter-selection scripts are outside this reward pass.

Ordinary boss-chest branches referenced only by grant scripts now receive private danger-based loot pools. Runtime list mutations and unavailable source scripts remain guarded. Original shared lists used by scripts are not redirected by this chest change. Scripted/quest containers retain their existing identity; their dedicated reward selectors can still be fixed by the quest-reward module.

The matrix pickup script is explicitly fingerprint-reviewed in `loot.json` (`ReviewedItemScripts`): it only advances the Amber/Madness crafting quest on acquisition. Its script remains attached and unchanged, while the corresponding ordinary loot pools lose their level gates. A changed script returns to preservation.
