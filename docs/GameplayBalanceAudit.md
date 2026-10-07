# Equipment balance correction and audit

The previous artifact calculation compounded a standard material/class baseline with exponential actor-tier growth and another physical premium. That violated the equipment-relative design. The artifact calculation now uses the standard baseline directly, with PhysicalPower constrained to 0.1–1.15. Explicit raw overrides remain available for deliberate experiments. The current researched catalogue uses 1.0 physical and enchantment multipliers; signature effects provide its exceptional abilities.

The same exponential factor also applied to ordinary NPC weapon copies and the warrior reference used for creature natural attacks. RareWeaponGrowth now defaults to 1.0, so an identical ordinary weapon does not gain damage from its holder's tier. Creature species factors (1.3 large, 1.15 supernatural) remain intentional native-engine compensation for armed creatures; they do not receive exponential tier growth.

## Written candidate examples

All values are raw equipment fields. Effect damage is an unresisted per-hit total; it excludes weakness amplification, temporary Drain Health and scripts.

| Item / class | Tier | Previous physical | Corrected physical | Enchantment damage |
| --- | ---: | ---: | ---: | ---: |
| Chillrend (Shortsword) | 6 | 45 | 18 | 15 |
| Nerveshatter (Warhammer) | 7 | 168 | 68 | 20 |
| Shadowrend (Claymore) | 8 | 129 | 52 | 28 |
| Shadowrend (Battleaxe) | 8 | 168 | 60 | 28 |
| Goldbrand (Longsword) | 8 | 115 | 31 | 22 |
| Umbra (Longsword) | 8 | 77 | 31 | 0 |
| Volendrung (Warhammer) | 8 | 168 | 68 | 0 |
| Mace of the Crusader (Mace) | 8 | 145 | 39 | 28 |

Regular Daedric comparators: shortsword 26, longsword 31, claymore 52, battleaxe 60, mace 39, warhammer 68. Chillrend has 15 frost damage and 16% weakness to frost for 20 seconds. Shadowrend's axe uses Battleaxe, rather than the prior Warhammer fallback. Light artifact armor uses a light-armor comparison rather than importing a heavy-armor material advantage. Legacy variants share a fixed physical and ordinary-effect profile within matching class/slot/effect identities; quest states and scripted effects retain their original identities.

The physical scenario analyzer also needed a correction: it was using protected uniques' source values. Proposed scenarios now use saved artifact physical rules, and the browser test confirms Chillrend uses 18 rather than its old source damage. The analyzer explicitly labels its physical-only calculations; enchantments and scripted effects have a separate impact tool in the Artifacts tab.

## Other systems checked

- Weapon-class modifiers are all 1.0: the absolute class baselines are not multiplied by a second class damage curve. Material progression remains intentional (Iron 1.0, Steel 1.12, Daedric 2.6).
- Full-fatigue melee retains the measured calibration: Strength 45 / Blade 35 iron longsword against the test armor = 10.51; Daedric longsword = 27.14. Strength 55 / Blunt 55 iron warhammer against no armor = 35.97; weapon-blocked at Block 30 = 30.57. These are the supplied testing measurements, not new native playtesting.
- At maximum ordinary offensive stats, Daedric longsword clean damage is 93 against no armor, 18.6 against the 80% armor cap. Daedric warhammer is 204 / 40.8 respectively; its slower attacks and heavier fatigue cost remain the tradeoff. These values explain why raw equipment damage should not be compared across weapon classes.
- NPC health plans top out at 250. Creature natural attacks top out at 93; large bodies retain their health and mobility tradeoffs. Existing explicit health overrides and safeguarded quest actors remain separate exceptions.
- The built ordinary/translated staff outputs top out at 99 total unresisted damage. Shared damage budgets prevent a three-element staff from receiving three full allowances. Higher unused configurable ceilings were not mistaken for actual output.
- Enchantment damage sharing, Absorb Health's damage-plus-recovery accounting, slot-weighted apparel bonuses, reflection/absorption allowances, sigil limits and crafting restrictions pass existing checks. Complete-loadout stacking and control/weakness interactions still require native testing.
- Potion strength/value and crafted resale tests pass. No analogous exponential multiplier was found there, so their current settings were retained.
- Shared player/NPC progression, 110 trainer skill floors, character-creation inputs, spell access, NPC reserves and affordability checks pass. No broad rebalance of those systems was made during this correction.

## Verification and limits

The full candidate built successfully. Native checks cover all 111 unique families / 556 records, same-class physical limits, fixed legacy variants, private enchantment links, charge accounting, native effect identities and script metadata. Another 2,289 item enchantments plus potions, sigils and custom-enchanting restrictions pass candidate checks. Browser checks cover live preview, standard-equipment comparisons, lore source links, editing, save/reload and effect-identity protection.

This is a targeted formula/configuration audit with native record verification. It does not prove every combat encounter or scripted artifact power balanced in live play. Paired day/night forms and armor sets received a joint LLM consistency review; Dawnfang/Duskfang share tier 7 and their Superior forms share tier 8. Seven quest/signature-script items remain explicit exemptions; their reasons are in the unique catalogue. Four registered non-equipment artifacts (Azura's Star, Oghma Infinium, Skeleton Key and the Wabbajack book record) are outside this equipment translation.
