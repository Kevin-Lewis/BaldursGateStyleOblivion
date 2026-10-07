# Character creation balance

This is a targeted pass over vanilla racial and birthsign abilities. Racial attributes, racial skill bonuses, class specializations, favored attributes and major skills are unchanged by default. Utility identities and the existing progression system remain.

`EnableCreationBalance` defaults to true. `creation.json` is packaged with the patcher; `CreationConfigurationFile` can select an edited copy. The module runs before physical combat and magic, so subsequent actor budgets and spell kits use the effective creation records. Report-only mode writes a comparison without record overrides.

## Defaults

| Choice | Change |
| --- | --- |
| Breton | Extra magicka 50 → 25; magic resistance 50% → 25% |
| High Elf | Extra magicka 100 → 60; existing elemental weaknesses retained |
| Mage | Extra magicka 50 → 25 |
| Apprentice | Extra magicka 100 → 50; magic weakness 100% → 25% |
| Atronach | Extra magicka 150 → 75; spell absorption 50% → 25%; stunted regeneration retained |
| Dragon Skin | Shield 50 for 60 seconds → 30 for 30 seconds |
| Woad | Shield 30 for 60 seconds → 25 for 30 seconds |
| Adrenaline Rush | Four attribute bonuses 50 → 15, health 25 → 20; duration 60 → 30 seconds |
| Berserk | Health stays 20; fatigue 200 → 60, Strength 50 → 20, Agility drain 100 → 30; duration 60 → 30 seconds |
| Lover's Kiss | Paralysis 10 → 4 seconds; fatigue cost 120 → 60 |
| Mara's Gift | Restore Health 200 → 100 |

Daily powers keep their original activation types and costs. Other racial/birthsign powers, elemental defenses, poison/disease defenses, water breathing, night vision, summons and utility effects are retained. Changed birthsign descriptions are generated from the effective abilities so the character-creation text shows the edited values.

## Editing and previews

Open `/creation` in the overhaul editor. Choose race, class, birthsign and sex to compare the loaded creation records with current edits at levels 1, 10, 20 and 30. Both columns use the existing combat progression settings. The page shows health, magicka, regeneration, expected elemental spell damage taken, and organized attributes/skills.

Controls edit racial starting attributes and existing racial skill bonuses, class specialization/two favored attributes/seven major skills, and ability magnitudes/durations. Effect identities, actor values, ranges, areas, scripts, and daily/lesser-power types are preserved. Clearing an override restores the loaded value. Saves use revision checking and the editor's local write token. Rerun Synthesis to apply saved gameplay changes.

Saved creation edits feed the physical and magic workbench presets immediately. Native racial abilities affect their existing NPC users. These passive effects are not baked into stored NPC attributes or spell points a second time.

The class comparison uses the closest configured progression profile; its attribute priorities and focused skills are assumptions about typical play. Selecting or editing a class does not force those choices on the player. Custom player classes remain supported by the native game; the catalogue editor covers loaded playable class records.

Incoming elemental spell percentages multiply net magic resistance, net elemental resistance and the average remaining chance after spell absorption. Absorption is probabilistic; these are expected damage factors, not guaranteed damage or a full encounter simulation. Active powers, reflection, absorption-triggered magicka recovery, equipment and potion stacking are not automatically simulated here. Test these in game, especially daily powers under the revised fatigue/health rules.

## Verification

Creation tests cover all 10 races, 21 classes and 13 signs, progression at four levels, native ability values/types/costs/effect identities, default race/class preservation, NPC parity, editable native race/class records, and report-only behavior. Browser tests cover live editing, saving, workbench integration, stunted regeneration and validation. Existing combat, magic and enchantment checks also run.

The candidate is generated for review, not automatically installed. Begin gameplay verification with a fresh character and fresh NPC instances; existing saves can retain actor state.