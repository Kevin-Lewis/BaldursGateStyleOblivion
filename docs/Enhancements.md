# Enchantments, artifacts and alchemy

The gameplay stage follows physical and magic balance. `EnableEnchantmentBalance` and `EnableAlchemyBalance` default to true; `enchantments.json` and `alchemy.json` are packaged defaults with optional configuration paths. Report-only mode writes analysis without gameplay edits. The candidate script refreshes `.enhancements.json` in the editor reports.

## Equipment and artifacts

Ordinary enchantments have independent tier budgets for permanent bonuses, defense, resources, control and damage per activation. Multi-element effects share a damage allowance. Absorb Health counts both damage and recovery when allocating that allowance. Armor and clothing budgets use occupied slots; a ring's two possible positions count as one item. A complete loadout still requires review: there is no new runtime cap.

Sigil-stone weapon and armor effects are evaluated separately, with a conservative accessory-slot budget for armor and a 1.25 premium over custom constant-effect caps. Original stone effect identities and unlock tiers remain. This covers enchanting through found stones as well as the altar; native charge calculation for applied stones still needs calibration.

[xOBSE’s native form definitions](https://github.com/llde/xOBSE/blob/master/obse/obse/GameForms.h) identify maximum charge on the weapon’s enchantable form. Weapon charge cost is set to provide approximately 35 activations, using the weapon’s stored ANAM maximum charge with a minimum of 100. Both manual enchantment cost fields are set consistently. Artifacts default to approximately 60. Native integer cost rounding can produce fewer uses; measure effective charge consumption in game. Staff damage has a separate primary-attack curve rather than the smaller melee augmentation allowance. Staff tiers initially follow existing damage strength, with explicit item-tier overrides available.

Curated artifacts use tiers 8–9 and current physical baselines, including the rare weapon curve and a modest physical premium. Their original models, scripts, effect codes, actor values, range and area are retained. Explicit physical values can override the baseline. Mehrunes' Razor remains a dagger and Chillrend a shortsword. Enchantment multipliers allow exceptional but bounded effects. Signature scripted or transforming effects, including Wabbajack and the Skull of Corruption, remain exempt pending individual native review. Other protected unique/reward equipment remains unchanged until explicitly curated.

The Artifacts tab offers sortable rows, physical/effect editing, preservation controls and mundane equipment comparisons below the selected artifact. Ring/clothing comparisons include ordinary clothing and jewelry. Original/proposed effects are shown. Saving updates configuration; rerun Synthesis to apply it.

## Custom enchanting and NPC equipment

Dangerous weakness combinations, Chameleon, paralysis, temporary Health drain, reflection/absorption and selected attribute/skill effects are excluded from future custom enchanting through the MGEF Enchanting flag. Existing spells and found enchantment identities are retained. Constant-effect factors and the native magnitude offset are adjusted for modest per-item targets; this avoids globally changing effect base costs and spell costs. Grand-soul magnitude calculations require native calibration. These changes do not repair already-created equipment stored in saves or restrict spellmaking.

Profile probabilities are 1% Poor, 2% Common, 3% Professional, 2% Military, 3% HighQuality, 4% Elite and 8% HighDremora. The existing equipment distributor applies these low chances to eligible themed pools, including weapons and armor. It retains appropriate materials, equipment classes and existing enchantment variety. This changes normal equipment selection rather than adding loot-only items; native AI still chooses equipment and combat behavior. Fixed, quest, scripted and unique equipment safeguards remain in force.

## Alchemy

Existing bottled potions and poisons are normalized within five ranks. Recovery totals start at 40 and rise to 190; poison damage starts at 25 and rises to 135. Buff caps rise from 8 to 25 and resource bonuses from 15 to 65, maximum bottle duration is 90 seconds, poison control lasts at most 3 seconds and weakness magnitude is at most 20. Food, scripted, quest and explicit exceptions are preserved. Effect identities stay unchanged for bottles. Temporary Health drain is excluded from permanent damage totals.

Apparatus quality becomes 10/22/38/52/65 rather than 10/25/50/75/100. This affects native crafting strength, including positive and negative apparatus interactions, without inventing a replacement runtime formula. Catalogue bottle caps do not hard-cap generated player potions.

`fPotionGoldValueMult` becomes 0.12 rather than 0.45. The estimated crafted value is floor((effective Alchemy + mortar quality × 0.25) × multiplier), with Luck adjustment and effective-skill bounds. Existing cached crafted records and engine rounding require in-game verification. The economics tool supports an observed value override.

Common ingredient values have a floor of 5; original higher values and individual overrides remain. At the tested 1.4 purchase / 0.4 sale factors, a master with quality-65 tools produces an estimated 13-value potion. Two purchased common ingredients cost at least 14, while sale proceeds are about 5.2. A master one-ingredient recipe also fails this tested resale loop. Gathered ingredients remain a source of income. Different barter factors, rare recipes, merchant gold and actual stock must be tested separately; profitability is not guaranteed across all possible barter states.

Selected ingredient effects are replaced in the same unlock positions: Weakness to Magic → Weakness to Fire, Chameleon → Invisibility, Reflect Damage → Shield, Reflect Spell → Resist Magic, Spell Absorption → Fortify Magicka. Protected ingredients retain their original effects. Native crafting still determines final magnitudes and durations.

The active-potion limit becomes two at every mastery. Private merchant stock is limited to eight common ingredients, two rare ingredients, three potions and one apparatus per inventory entry. Existing rarity branches, merchant identities and refresh behavior remain; this is not a total-stock or restock-time cap.

## Workbench and verification

The workbench exposes rank/slot budgets, custom enchanting exclusions, NPC probabilities, artifact effects, alchemy strength/economics controls, potion rank/value overrides and ingredient values. Quick recipe analysis shows unlocked effects, estimated value, purchase cost, sale proceeds and profit for bought versus gathered ingredients. One-ingredient recipes require mastery.

The augmentation comparison shows a clean physical hit plus one full enchantment activation or poison application, charge endurance, raw damage over selected activations and recovery potential. It does not simulate native DoT overlap, weakness sequencing, poison immunity, control, AI priorities or combat outcome. Full physical/caster scenarios remain linked separately.

`tests/Enhancements` covers multi-effect budgets, ring coverage, low-rate recovery, temporary drain, script preservation, economics, native binary round trips and written candidate links/effects and equipment-pool rarity checks. Browser checks cover comparison rows, live edits, persistence, validation and request protection. Calibrate fresh items and brews in game, including charge use, crafted price/strength, active potion limits and repeated effects, before treating the first pass as final balance.