# Power Tiers

PowerTier is a universal internal classification from 0–10 describing relative power or importance.

| Tier | General meaning |
|---|---|
| 0 | Negligible power or threat |
| 1–2 | Low power |
| 3–4 | Moderate power |
| 5–6 | Strong |
| 7–8 | Exceptional |
| 9–10 | Highest levels of power |

These are broad classification anchors shared across NPCs, creatures, weapons, armor, spells, potions, enchantments, dungeons, encounters, loot containers, and quest rewards.

## Scope

- PowerTier is separate from Oblivion's actor Level field.
- Tiers initially have no gameplay numbers or formulas attached.
- Equal tier increments do not require equal increases in gameplay power.
- Mortal and supernatural distinctions belong in NPC and creature classification and balancing rules, rather than in PowerTier itself.
- Unclassified is distinct from Tier 0; an unknown record must not silently receive the lowest tier.

## Supporting Concepts

- PowerTierRange: a minimum and maximum tier, each within 0–10, with minimum no greater than maximum.
- TierAssignment: the assigned tier, applied rule, and reason.

The initial domain model establishes these concepts and validates their bounds. It does not assign records or change gameplay.
