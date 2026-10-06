# Physical combat first pass

The first pass writes equipment, explicit NPC and creature stats, combat-style variants, and engine settings into the candidate plugin. Its design target is that fighting two tiers above is only reasonably possible with extreme preparation and excellent execution; a four-tier gap should remain formidable even with allies. These are gameplay targets awaiting playtesting, not guarantees inferred from simulated win rates.

Open http://127.0.0.1:5078/combat. Tier sets a typical level; level remains adjustable. Player presets read loaded race, sex, playable class and passive birthsign bonuses. Enemy presets use the patched NPC training budgets. Specialty skills reach 100 at tier 5 / level 20. A player's actual progression depends on skill use, training, race, and attribute choices; presets do not force player skills to match level.

## Building and playing

1. Save settings in the workbench.
2. Run **Build Combat Candidate.cmd** to rebuild `artifacts/combat-first-pass/BaldursGateStyleOblivion.esp`.
3. Run **Build and Install Combat Candidate.cmd** to rebuild and install it. The script requires Oblivion to be closed and backs up the previous plugin and Plugins.txt before replacing the plugin.
4. Launch Oblivion at midpoint difficulty and use a fresh test character or a clean testing save. Existing actors and generated inventory can be cached in saves; an already-equipped item is not guaranteed to refresh.

The candidate combines combat with the preceding world, equipment distribution, loot, merchant and reward passes. Its settings are `artifacts/combat-first-pass/settings.json`. It is tested against the official plugin load order used by this project. Changing the input load order requires rebuilding and checking compatibility. The build script refreshes the workbench catalog; Reload saved refreshes its data.

## Scenario equipment

Each fighter has independent Weapon material, Armor material, and Shield selectors. Tier default follows the editable QuickTiers gear table; explicit choices survive tier/class changes. No armor removes body equipment and the preset shield, while an explicit shield choice permits shield-only tests. Native heavy/light classification determines which armor skill applies, even when a material override differs from the class's usual armor.

Presets prefer regular material sets over NPC outfits that merely contain a material name. All automatic equipment is unenchanted. Tier-default weapons above tier 6 use the configured rare variant power; an explicit weapon material selects a standard weapon with WeaponPower 1. The quick summary identifies the actual weapon, armor material/type, shield, and any rare variant. Export/import retains these selections.

| Tier | Weapon | Armor default | Light armor default |
| --- | --- | --- | --- |
| 0 | Iron | Fur | Fur |
| 1–2 | Iron | Iron | Leather |
| 3–4 | Steel | Steel | Chainmail |
| 5 | Dwarven | Dwarven | Mithril |
| 6 | Elven | Orcish | Elven |
| 7 | Glass | Ebony | Glass |
| 8 | Ebony | Daedric | Glass |
| 9–10 | Daedric | Daedric | Glass |

These are test-build assumptions, not guarantees of world availability. Edit the saved defaults in Tier targets & fatigue; use scenario overrides for quick comparisons without changing global item balance.

## Editable gameplay model

Everything is in `BaldursGateStyleOblivion/combat.json`.

**Gameplay rebalance** contains:

- Weapon class baselines: absolute damage, speed, reach, weight and durability. Known materials multiply these baselines, then class and individual modifiers apply. Unknown shapes/materials retain their source basis.
- Armor slot coverage: independent budgets for body pieces and shields. Heavy/light classification and material factors determine protection, weight and durability.
- Actor tiers: slow health growth, attributes, specialty/secondary skills, and natural creature damage.
- Actor builds: class health and attribute factors, specialties and native style selection. Fatigue is Strength + Endurance + Agility + Willpower. Class health multipliers start neutral, so humanoids at the same tier share a health budget. Knights emphasize Block and heavy equipment; Barbarians emphasize Strength and aggression. Both retain robust Endurance. No inverse health/Endurance tradeoff is imposed. Explicit NPC health uses the modest tier budget; actual player health still follows engine progression. The player's class-creation records are not rewritten by these NPC archetypes.
- Creature builds: default, frail, supernatural, and large. Large creatures receive twice the health, 1.3 times natural damage, and 0.75 times the speed budget. Nature/classification and recognizable creature identities select the initial profile.
- Native combat styles: attack, block, power-attack and dodge decision chances plus idle/hold timers. Existing advanced settings, movement ranges and flags are retained. Eligible major physical bosses use Boss. These are engine decision weights, not measured contact rates.
- Engine coefficients: actual GMST writes used by the proposed analyzer. The global weapon multiplier is 3 to produce a quicker first-pass combat pace; skill contributes 0.1–1.0 and Strength 0.5–1.0. Armor caps at 80%, active blocking at 75%, and weapon blocking has half the shield factor. At empty fatigue, the configured damage factor is 35% of full-fatigue damage. Attack costs are 6 + weight × 0.45; regeneration is 3 + Endurance × 0.07 before other engine behavior. Power attacks use double normal damage and four times normal attack cost. Novice block cost is configured as a constant 8; engine Block mastery remains relevant.
- Individual actor exceptions: preserve, build and health overrides. Script/quest/deleveling safeguards still apply; a combat override cannot silently bypass them.

Armor weight is slot rating × 0.9 for heavy and × 0.35 for light, before editable class/material factors. Durability is slot rating × 20 or × 10. Armor condition coefficients are explicit. The full-light master rating bonus and armor/athletics mastery weight/resource multipliers are set to 1, retaining predictable equipment burdens. Luck's engine relationship is retained rather than zeroing its coefficient while leaving its separate integer offset active. The difficulty slider sensitivity is reduced; midpoint remains the comparison baseline.

Material damage potential rises from Iron 1, Steel 1.12, Dwarven 1.28, Elven/Orcish approximately 1.35–1.4, Glass 1.75, Ebony 1.9, to Daedric 2.6. Material armor, weight and durability factors vary independently. Availability remains governed by the existing distribution rules; material is not actor tier.

Above tier 6, eligible NPC/creature inventory weapons and nested weapon lists receive actual tier variants with damage growth of 1.5 per tier. These variants are real equipment and can be recovered from enemies. Registered artifacts, scripted and quest weapons retain their protections; enchantments are copied unchanged on eligible ordinary enchanted variants. Variant prices are retained pending the economy pass. Rare variants are shown explicitly as WeaponPower in quick tests, not as a hidden actor-level multiplier.

**Equipment formulas** edits material/class multipliers and item exceptions. **Tier targets & fatigue** selects quick gear and contains optional design benchmarks. **Combat styles** contains estimated scenario behavior rates and native record inspection. Saving does not rebuild or install the plugin.

## Scope and accuracy

Actor normalization reuses the current deleveling pass's eligibility decisions, including script scaling, technical helpers, protected actors, and uncurated quest/named exceptions. NPC auto-calculation is disabled after explicit stats are assigned. Creatures receive absolute stored stats and fixed levels rather than retaining offset-based stat multiplication. Existing abilities, resistances, spells, scripts, AI packages and racial records are retained; magic/enchantment effects are not comprehensively rebalanced yet.

Both comparison columns share explicit combatant inputs. Current uses incoming item fields and engine constants; Proposed uses normalized items and proposed engine constants. Optional tier targets additionally replace proposed health and multiply proposed offense: this benchmark mode is disabled in quick tests and is not applied to actors. Generated rare weapon variants already contain their power; use WeaponPower 1 when selecting an actual generated variant.

The normal-hit breakdown uses the same arithmetic as the scenario simulation: base damage, weapon/Strength/skill/condition/fatigue factors, armor, normal-weapon resistance and explicit outgoing multipliers. Heavy/light armor uses the corresponding skill. Native resistance-bypass flags and enchantment presence are retained; merely changing a material name grants no bypass. Native integer damage and armor hundredths are matched by binary roundtrip checks. Heavy/light classification reads the native BMDT flag byte; this compensates for the installed record API's shifted enum definition without rewriting the native flags. Equipment fallback choices preserve heavy/light type as well as body slots.

Use native fatigue coefficients is enabled by default; disabling it permits explicit resource experiments. Attack cadence, contact, block uptime and fatigue consumed during blocks are approximations requiring in-game calibration. The engine's animations, staggering, mastery perks, movement, equipment wear, spell/enchantment effects, reflect damage and runtime modifiers are not fully simulated. Creature natural-attack and ranged/magic scenarios require separate analyzer models even though the plugin includes creature and archer/mage stat profiles. Fight durations are estimates, not validated encounter outcomes.

Level and class presets are reproducible build assumptions. They cannot uniquely reconstruct an actual player. OBSE's [actor-value and equipment commands](https://obse.silverlock.org/obse_command_doc.html) can support controlled engine measurements; [xOBSE actor wrappers](https://github.com/llde/xOBSE/blob/master/obse/obse/Commands_Actor.cpp) distinguish current actor values from base records. The existing damage relationship reference is [the documented damage formula](https://pt.uesp.net/wiki/Oblivion:The_Complete_Damage_Formula); its equations and this simulator still need comparison against this installed candidate.

For calibration, start with unenchanted normal attacks, no active effects, fixed equipment condition and midpoint difficulty. Measure health loss and fatigue, varying one factor at a time. Test shields, weapon blocking, mastery thresholds and power attacks separately. Record executable version, load order, candidate hash, runtime plugins, actor values, equipment and measurement method. Then test repeated encounters and preparation against tier gaps. Do not use arbitrary health inflation to force matchup results.

## Player and NPC training baselines

Player level 1 is calculated from the winning creation records and native starting-bonus settings. The default male Imperial Warrior without a passive birthsign bonus has Strength 45, Blade 35 and Block 30. Race, sex and birthsign are quick selectors; individual scenario stats remain editable. Passive attribute and health abilities are included; active powers, equipment enchantments and scripted effects require manual scenario adjustments.

Later player levels use `Gameplay.PlayerProgression`: major skills approach 100 at MasteryLevel (20), specialization-only skills approach SecondarySkillAtMastery (55), and other skills gain UntrainedSkillPerLevel (0.5). Favored attributes gain PrimaryAttributePerLevel (2.5), other attributes gain OtherAttributePerLevel (1), and Luck stays at its starting value. Player health estimates accumulate the configured Endurance-derived level gains. These are development assumptions, not changes to the game's advancement system or a guarantee that every player follows this path.

NPC tier specialties now use 30, 40, 50, 65, 85, 100 through tiers 0–5. Supporting skills use 15, 20, 25, 35, 45, 55; untrained skills start at 5. The base attribute curve starts at 40. Each class lists primary and supporting skills, with SkillMultiplier available for weak civilian profiles. Humanoid health budgets are retained. Creature species modifiers and natural damage budgets are retained, but their shared attribute baseline also starts higher.

Eligible balanced trainers receive a floor only in their taught skill: max(training budget, configured training ceiling). NPC training services take precedence over class defaults. Training ceilings, services, referral quests, and health are not increased by this safeguard. Protected/skipped actors keep their existing records and safeguards.

The workbench shows stat provenance and separates Input baseline from Configured overhaul. Creation choices and material overrides are retained in exported experiments. Rebuild the patch for NPC changes, restart the game, and use a fresh test game to avoid old saved actor stats.

Calibration: with neutral Luck and full condition/fatigue, Strength 45 / Blade 35 predicts iron sword 10.51 and Daedric sword 27.14 damage with approximately 3% target armor mitigation. Strength 55 / Blunt 55 predicts iron warhammer damage 35.97 against no armor, or 30.57 with weapon blocking at Block 30. These reproduce the reported 10.5 / 27.14 / 36 / 30.6 measurements; the target's 3% armor mitigation remains inferred. Attack contact, cadence, positioning and stagger still require gameplay calibration.