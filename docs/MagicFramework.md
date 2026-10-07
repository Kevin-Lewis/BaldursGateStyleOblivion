# Magic framework

The analysis stage remains read-only. The separate, optional magic gameplay stage adds representative spells, updates eligible NPC kits and base magicka, and applies configured resource settings. It preserves original spell and magic-effect records. `EnableMagicAnalysis` defaults to true and writes `.magic-analysis.json` and `.magic-analysis.md` beside the other patcher reports. The candidate build script refreshes the editor copies. Open **Magic analysis** in the configuration website.

## Progression direction

Mages should use the same level/class attribute and skill estimates as melee actors. Do not add a hidden tier multiplier to every spell. Early mages should survive through positioning, limited control and preparation; their physical defenses remain weak. Their modest magicka pool should force useful choices without making basic casting impractical.

Journeymen gain reliable damage and a broader toolkit. Experts and masters should become dangerous through powerful spells, combinations, area coverage, summons and defensive preparation. Preserve vulnerability when caught unprepared. The rare end should grow more steeply through access to exceptional effects and combinations, with availability and cost constraining repeat use. Apply the project's two-tier/four-tier challenge expectations to ordinary encounters, while measuring resistance and preparation explicitly.

The existing primary mastery target is level 20, with other heavily focused schools approaching mastery by level 30. Supporting schools continue afterward. The report shows a male Imperial Mage without a birthsign or equipment; it does not prescribe spell acquisition or represent every race/build.

## Separate measurements

| Family | Measure | Context needed |
|---|---|---|
| Damage | Total raw damage per target; cost efficiency; pool-limited output | Delivery time, hit rate, resistance, overlapping effects |
| Healing | Total recovery and cost | Recovery timing, overhealing, interruption |
| Defense | Shield magnitude and duration | Armor cap, armor overlap, elemental resistance |
| Control | Duration and target-level magnitude | Immunity, effectiveness, break conditions, opponent actions prevented |
| Summons | Duration and actor reference review | Summoned actor attacks, health, AI, summoning limits |
| Bound equipment | Duration and effect identity | Actual equipment records and combat baseline |
| Utility / stats | Effect, magnitude, duration, range | School-specific usefulness, combinations and exceptions |

Area is recorded as a radius, never silently converted to target count. Drain Health is temporary and is kept out of permanent damage totals. Weakness effects are not converted into bonus damage without a sequence model. Powers, abilities, diseases and test records are separate from ordinary spells. Vendor links are inferred from NPC spell lists and spell-selling services; unlinked spells may still be available through quests or scripts.

Costs use winning magic-effect definitions and game settings. Manual stored costs and calculated effect costs remain visible separately. Skill discounts change casting efficiency, not effect magnitude. The formula follows the [xOBSE cost implementation](https://github.com/llde/xOBSE/blob/master/obse/obse/GameForms.cpp); absent settings use documented fallback assumptions. Its reconstruction is a reference, not proof of every native casting path. Runtime rounding, minimum costs, mixed-school behavior, manual costs and armor effectiveness require calibration before gameplay balancing.

## Review priorities

1. Review vendor-linked damage/healing spells dominated by comparable spells, and large manual-cost exceptions. Check availability and special purpose before changing them.
2. Measure novice/apprentice spell cost, damage and available casts against equal-tier melee health and damage. Include regeneration and real casting cadence in the subsequent scenario model.
3. Review paralysis, silence and level-limited control; weakness stacking; reflect/absorb; and elemental shield overlap. These can overwhelm a damage-only progression curve.
4. Connect summon effects to their summoned actors and our creature balance. Spell duration alone cannot establish summon power.
5. Audit actor spell access against their school skills and available magicka. Raising actor skills does not automatically improve their spell lists.
6. Evaluate spellmaking and racial/birthsign magicka bonuses separately. They must not bypass the intended access curve.

No school-wide numerical multipliers are chosen in this phase. Destruction, Restoration, Alteration, Illusion, Mysticism and Conjuration will receive separate rules after this common audit and engine calibration.

## Validation

`tests/Magic` checks instant effects, damage over time, healing, temporary health drain, summons, self-target effects, area/range costs, skill discounts, zero-cost handling, scripted unknowns and comparable outlier detection. The native adapter reads actual winning records; the integrated candidate run verifies that gameplay adds only new representative spells and leaves original spell and magic-effect records intact.
## Playable first pass

EnableMagicBalance and magic.json control this pass. The Magic page contains quick tier/level/profile scenarios, live previews, rank and kit settings, resource settings, actor exceptions, and the latest distribution report. Save settings and rerun Synthesis to apply changes. The preview uses current edits; distribution rows describe the last patcher run.

Profiles cover Destruction, Conjuration, Necromancy, Illusion, defensive casters, Battlemages and Spellswords. Conjurers and necromancers prioritize Conjuration, illusionists prioritize Illusion, and defensive casters prioritize Restoration. Destruction specialists focus Alteration and Restoration alongside their primary school. Playable native classes retain their creation skill distribution; nonplayable classes use explicit build aliases. Primary mastery remains around level 20, focused mastery around 25–30.

| Rank | Required school skill | Attack damage | Healing | Shield | Base cost |
|---|---:|---:|---:|---:|---:|
| Novice | 0 | 12 | 12 | 8 | 8 |
| Apprentice | 25 | 20 | 24 | 15 | 10 |
| Journeyman | 50 | 36 | 45 | 25 | 28 |
| Expert | 75 | 62 | 75 | 40 | 70 |
| Master | 100 | 100 | 110 | 55 | 160 |
| Exceptional | 100 | 145 | 140 | 65 | 240 |

Slot costs multiply that base: attacks ×1, healing ×1.6, Shield ×1.3, summons ×2.5, paralysis ×1.7, silence ×1, invisibility ×1.3. School skill then discounts the result. Strong utility therefore competes with offense for reserves. Magnitudes, durations and stored costs are rounded to native integers. These are starting balances for testing, not a rewrite of the original spell catalogue.

## Reserves and recovery

NPC stored base spell points are normalized to 2 × Intelligence, with an explicit per-actor Magicka override available. Existing inflated stored pools are not retained automatically. The player preview uses 2 × Intelligence before race/birthsign bonuses. NPC calculated actor values, abilities and runtime behavior can differ from stored base spell points; inspect fresh actors in game and use observed pool/regeneration overrides to calibrate the preview.

The following editable GMSTs are applied when the winning input differs. Defaults preserve native resource factors while the representative costs and stored NPC reserves establish the first balance pass.

| Setting | Default | Purpose |
|---|---:|---|
| fPCBaseMagickaMult | 1 | Player Intelligence contribution; preview uses Intelligence × (1 + setting). |
| fNPCBaseMagickaMult | 1.5 | Native NPC magicka factor; exposed separately from stored base spell points. Verify effective NPC pools in game. |
| fMagickaReturnBase | 0.75 | Base percentage recovery rate. |
| fMagickaReturnMult | 0.02 | Extra percentage recovery per Willpower point. |
| fMagicCasterSkillCostBase | 0.2 | Cost fraction remaining at mastery. |
| fMagicCasterSkillCostMult | 1.2 | Additional cost fraction removed as school skill improves. |

Preview regeneration is pool × (base + Willpower × multiplier) / 100 per second. Race and birthsign Fortify Magicka bonuses are included; Stunted Magicka disables regeneration. The default male Imperial Destruction specialist has approximately 90 magicka at level 1 and 166 at level 20. His selected attack costs approximately 10.4 and 32 respectively: about eight apprentice attacks or five master attacks from a full pool before recovery, buffs or healing. Empty-pool recovery is about 69 seconds early and 36 seconds at capped Intelligence/Willpower. Recovery cannot sustain full-rate master attack spam.

Kit selection requires actual school mastery and affordability. The strongest eligible spell per slot must cost at most 65% of the stored pool. This cap chooses inventory; it is not a native casting restriction. Exceptional NPC spells additionally require tier 8+ and an explicit AllowRareSpells override. Ordinary vendors receive representative additions without exceptional ranks; their original stock remains. No new quest or discovery placements are added here. In the scenario, explicit rare access represents an acquired spell and still checks school skill and affordability.

Eligible NPCs receive compact kits. Ordinary nonscripted combat spells and wholly combat leveled spell lists can be replaced; abilities, powers, diseases, mixed utility lists and scripted effects remain. Vendors receive additions only. Actor preservation rules still apply; actors with no affordable kit keep their original inventory. Generated spell IDs are stable within the patch and shown in the workbench for console testing.

## Testing and limits

The quick scenario models one selected damage spell, Shield upkeep and optional self-healing below 50% health against a tier-equipped warrior. Its cast interval, contact rates and action priorities are explicit assumptions, not changes to native AI. Control, concealment, summons, caster weapon damage, reflection, absorption and weakness combinations are not simulated. Summons use native actor records plus the existing creature patch; no invented summon DPS is included. A kit describes capabilities, not guaranteed AI sequencing or party healing.

Test fresh NPCs at apprentice, journeyman, expert and master ranks. Compare observed maximum magicka, clean spell cost, damage and regeneration with the workbench, then check mixed offense/defense consumption and a two-tier mismatch. Existing saves can retain actor state. Use the displayed generated IDs to acquire representative player spells rather than assuming an original vanilla spell has changed.

Reports: .magic-gameplay.json records kits, removed/retained links, stored reserves, generated IDs and resource settings; .magic-analysis.json/.md retain the full catalogue audit and outlier analysis. Native generation, class parity, resource bounds, binary round trips and browser save/preview behavior are covered by tests/Magic and tests/VerifyMagicBrowser.cjs.
