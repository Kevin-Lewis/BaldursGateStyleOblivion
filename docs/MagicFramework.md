# Magic framework

Phase 21 is analysis only. It does not change spells, magic effects, actor spell lists, costs, mastery requirements or magic settings. `EnableMagicAnalysis` defaults to true and writes `.magic-analysis.json` and `.magic-analysis.md` beside the other patcher reports. The candidate build script refreshes the editor copies. Open **Magic analysis** in the configuration website.

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

`tests/Magic` checks instant effects, damage over time, healing, temporary health drain, summons, self-target effects, area/range costs, skill discounts, zero-cost handling, scripted unknowns and comparable outlier detection. The native adapter reads actual winning records; the integrated candidate run confirms report generation without adding spell or magic-effect overrides.