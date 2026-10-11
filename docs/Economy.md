# Economy balance

The economy pass runs after equipment, magic, enchantments and alchemy. It changes physical item values, native apparel enchantment barter factors, merchant gold/Mercantile floors, selected service game settings and coin counts in generated adventure loot. It does not multiply equipment damage, actor statistics or spell power.

Open the local editor at `/economy`. Save there, then rerun Synthesis. The plugin configuration is `BaldursGateStyleOblivion/economy.json`; the two crafting-price controls save to the existing `alchemy.json` as well. Both editors use revisions to prevent a stale tab from silently overwriting newer settings.

## Progression and prices

Money becomes easier through access to richer locations and equipment, not a multiplier on player level. A single substantial upgrade should take several modest successful outings in the early/middle game. Buying an entire replacement outfit is a much larger goal. Supply purchases, failed trips and self-repair substantially change the result.

Weapon construction prices use class × material scarcity. Armor uses covered slots × material. Equipment enchantments contribute their native engine value exactly once. Unique equipment adds its lore-tier prestige premium (1–1.6 by default) to the physical component only. Arrows and scrolls use single-use pricing. Jewelry, ordinary valuables and mundane books retain authored differences, so there is no universal gold-per-damage efficiency rule. Skill books get a utility premium; soul gems are priced by capacity and actual contained soul; apparatus by rank. Curated artifacts use their corrected material/class and effects. Protected quest/script exceptions and nonplayable apparel retain their values. Individual exact inventory-total or base-preservation overrides are available in the price table.

Representative unenchanted base values:

| Longsword material | Base gold |
|---|---:|
| Iron | 45 |
| Steel | 86 |
| Dwarven | 180 |
| Elven | 270 |
| Glass | 495 |
| Ebony | 810 |
| Daedric | 1,350 |

The price is the record's base value; actual transactions use the engine's bargaining rules. Ordinary weapon damage remains the existing physical balance's damage.

Generated adventure coin counts use ×0.75. The pass leaves Gold001's value at one and does not alter quest payout scripts, authored fixed gold or merchant inventory rolls. The report includes source-text gold grants for individual review (210 in this load order). Scripted amounts, variables and compiled-only scripts cannot safely be normalized by a blanket text change.

## Native enchantment values

The former economy formula added a second magic premium to the base field and reported that field as the complete price. That double-counted magic in game. The replacement writes the physical component only and reports `written base + native magic = inventory total`. Purchase/sale estimates and loot sampling use that total.

`ApparelEnchantmentValueMultiplier` defaults to 0.20. It multiplies the winning standard magic effects' `ConstantEffectBarterFactor`; it does not change their strength, duration, spell cost or enchanting power cap. It also lowers apparel enchanting altar fees. Both fees and resale magic value use the same factor, so this does not create a new apparel-enchanting resale margin. Soul costs remain an additional expense. Player-created apparel uses these factors too; weapon enchanting and recharge settings remain separate.

The native inventory routine in the installed Oblivion 1.2.0416 executable (0x488E50) confirms:

- Apparel sums magnitude times each effect's barter factor. No-magnitude effects use the integer `fMagicCEEnchantMagOffset`, **not** their stored magnitude. The existing enhancement pass sets that offset to zero; it is otherwise five by default.
- Charged equipment uses `fEnchantmentPointsMult * capacity + fEnchantmentEffectPointsMult * enchantment cost`, in addition to its base. Both multipliers default to 0.4. This change reads winning values and does not alter capacity, damage or uses.
- Scroll magic uses `fScrollCostMult * enchantment cost` (default 0.5).
- Zero-base apparel and charged equipment remain valueless. A positive exact-total override below their magic contribution therefore needs a base of at least one; the editor flags the unattainable target. An explicit zero still means zero for those items.

The reader resolves manual enchantment costs. Auto-calculated costs and scripted/custom effects are explicitly **unverified**: their base fields are preserved, totals/buy/sell estimates are not presented as reliable, and they are excluded from projected loot income. Current candidate coverage is 2,364 modeled enchanted items and 336 unverified (304 scrolls, 28 weapons, one ammunition item and three scripted apparel items). Unverified entries are visible under Include preserved items. The global standard-effect pricing change can still affect their actual totals.

Exact item overrides now specify the desired inventory total. The patch subtracts the native contribution to obtain the written base. Existing unenchanted overrides retain exactly the same meaning. Old arrow/scroll effect-share settings remain readable for configuration compatibility but no longer add a duplicate premium; the three effect-value controls now apply only to sigil stones.

With the current settings, the Rumare ring is 150 physical + 60 magic = **210**, rather than the previous 244 physical + 300 magic = **544**. Wolf/lion/bear pelt manual values remain 25/40/60. Native formulas are checked against exported records; actual inventory display, altar costs and merchant offers still require gameplay calibration, particularly with runtime pricing mods.

## Services and merchants

| Engine setting | Default in this pass | Reason |
|---|---:|---|
| fRepairCostMult | 0.55, from engine default 0.9 | Keep maintenance meaningful without making ordinary paid repairs dominate small trips. |
| fRechargeGoldMult | 0.10, from loaded 1.0 | Charge capacity was already normalized; charging one gold per capacity unit makes refills disproportionately expensive. |
| fTrainingCostMult | 8, from loaded 10 | Training remains costly as skills rise, with a modest reduction. |
| fSpellmakingGoldMult | 3, unchanged | Preserve the established magic power/cost model. |
| fEnchantmentGoldMult | 8, from loaded 10 | Reduce the creation fee modestly; souls and existing custom-effect restrictions still matter. |
| fBarterSellBase | -20, unchanged | Keep native bargaining rather than adding a new universal sale penalty. |
| fBarterBuyBase | 300, unchanged engine default | Expose the native purchase modifier for experimentation. |
| fBarterSellMult | 5, unchanged engine default | Expose the native skill sale modifier. |
| fBarterBuyMult | 9.9, unchanged engine default | Expose the native skill purchase modifier. |
| iPerkExtraBarterGoldMaster | 500, unchanged | Retain the native master merchant's additional transaction capacity. |

General stores have 600 gold per item; poor blacksmiths 400; professional smiths 1,000; luxury merchants 1,800; rare-goods merchants 2,000. Other specialties have their own values. These are configurable by profile or individual merchant. Higher native/trainer Mercantile skills are retained. The native gold limit is per transaction, not a wallet depleted by sales; stacks can be split. The analyzer includes the master merchant's extra gold and the player's skill-50 general trading perk. [Commerce mechanics](https://en.uesp.net/wiki/Oblivion:Commerce).

Paid recharge depends on missing charge; the analyzer exposes both charge spent and the multiplier. Repair estimates use gear value × wear × repair multiplier and omit disposition/repair-skill details. Set wear to zero when doing your own repairs and add hammer costs under Other costs. [Recharge setting](https://cs.uesp.net/wiki/FRechargeGoldMult), [repair setting](https://cs.uesp.net/wiki/FRepairCostMult).

Spell prices remain tied to native spell magicka cost, school skill, Luck and bargaining. There is no separate spell price field to normalize independently; the pass does not change casting costs to achieve a gold target. The tab shows estimates and flags manual-cost spells for calibration. [Spell purchase mechanics](https://en.uesp.net/wiki/Oblivion:Spells).

## Crafting

The shared crafted potion value multiplier is 0.18 (previous overhaul 0.12; vanilla 0.45); common ingredients have a minimum base value of 8 (previous overhaul 5). At Alchemy 100, Luck 50 and the current master mortar quality 65, estimated created value is 20 gold. A one-ingredient master recipe bought at ×2 and sold at ×0.25 loses money; gathered inputs can add value through crafting. Perfect 1:1 trading yields a small master-crafter margin, so this does not claim to eliminate every profitable recipe. Stock and restocking limit purchased-input turnover. The Alchemy recipe tester handles actual ingredient effect matches and observed-value calibration.

The Economy crafting table shows purchased profit, the opportunity cost of selling gathered ingredients instead, and perfect-trading margins. Apparatus purchase costs are a separate investment; the simple recipe table does not amortize them. Potion/ingredient economic controls do not alter their established strength budgets.

## Analyzer and evidence

The October 10 economy candidate records 6,188 items, 11,655 final loot lists, 3,054 adventure sources and 270 merchants. The analyzer runs 256 repeatable samples through those final inventories with nested empty chances, counts, UseAll and per-item rolls. It excludes one-time unique rewards, nonplayable apparel and test/helper containers. The nearest available world tier is used for a missing theme/tier combination; this is disclosed in source names. The same-character curve holds carrying capacity, trade factors, equipment costs and purchase target fixed while varying world danger. Some themes stop at lower tiers, so their curves naturally plateau.

Historical pre-correction examples (not current price promises; rerun the analyzer with the current report):

| Test | Typical net gold | Purchase goal | Estimated outings |
|---|---:|---|---:|
| Tier 2 / level 5 warrior; short bandit outing | 44.8 | Steel longsword, ~189 gold | 4.2 |
| Tier 3 / level 10 warrior; short bandit outing | 32.4 | Steel longsword, ~187 gold | 5.8 |
| Tier 4 / level 15 warrior; short bandit outing | 83.7 | Dwarven longsword, ~390 gold | 4.7 |

A low result can be a loss. Higher-danger Daedric delves can be much richer because recovered Daedric armor is expensive; the loot manifest makes that cause visible rather than hiding it behind a tier bonus. These numbers are estimates of a successful sample trip, not guaranteed earnings or full-dungeon spawn simulations.

Trade factors are editable approximations, not a verified reconstruction of every native haggle/disposition modifier. Condition, carrying, selected supplies, repair wear, recharge use, travel hours and quest gold remain explicit assumptions. Leveled branches retained for scripting/other safeguards and unresolved/cyclic dependencies are excluded and counted; their contents can make actual income higher. Existing saves may retain old inventory/actor data until new/reset instances are used.

## Validation

- Candidate builds successfully; native economy checks compare all written item values, merchant gold/skill floors, 427 generated coin entries and service GMSTs against the report.
- Native checks confirm price writes retain damage, armor rating, durability and weight.
- Existing 2,289-enchantment and all-111-unique-family checks pass against the combined candidate.
- Economy unit checks cover bounded prestige, currency/quest preservation, empty/UseAll loot, deterministic sampling, carrying limits, merchant transaction limits, charge costs and one-ingredient master crafting.
- Browser checks cover quick tier/level/class controls, live capacity/expense changes, search/sorting, exact price exceptions, shared alchemy saving, stale revisions, request authorization and desktop/mobile layout.
- A standalone ReportOnly run writes zero records and an empty ESP.

Run `dotnet run --project tests/Economy/Economy.Tests.csproj -- --candidate artifacts/combat-first-pass/BaldursGateStyleOblivion.esp` and `node tests/VerifyEconomyBrowser.cjs`. The corrected native-pricing candidate also passes 2,364 exported enchanted-total/effect/charge checks and `node tests/VerifyEconomyPrices.cjs` (exact totals, minimum warnings, live factor changes, saving, preserved manual choices). Runtime inventory values, transaction offers and real net profits remain gameplay calibration.

