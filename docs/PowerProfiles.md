# Power Profiles

Phase 2 provides one shared classification framework. It defines no health, damage, economy formulas, or default record-tier assignments.

| Type | Purpose |
|---|---|
| PowerTier | Validated relative power/importance from 0–10 |
| PowerTierRange | Validated inclusive minimum and maximum; equal bounds are allowed |
| PowerProfile | Shared tier assignment and decision history |
| ActorProfile | Power plus the existing actor dimensions |
| LocationProfile | Power plus optional enemy and loot tier ranges |
| EquipmentProfile | Power plus optional equipment kind and material |

All profiles reuse the existing nine-level rule resolver. They expose Dimensions with Selected and Superseded decisions, including values, rule IDs, priorities, and reasons. Unclassified identifies dimensions without a decision. Tier is a nullable convenience accessor for the selected PowerTier; unknown is distinct from an explicitly assigned Tier 0.

## Assignments

Manual, rule-derived, and fallback assignments use the same API. The caller supplies the matching evidence and appropriate priority; profiles store and resolve decisions rather than independently evaluating rules.

```csharp
var power = new PowerProfile();
power.AssignTier(new PowerTier(1), "fallback", "Fallback classification", RulePriority.Fallback);
power.AssignTier(new PowerTier(4), "creature", "Creature classification rule", RulePriority.RaceCreature);
power.AssignTier(new PowerTier(6), "explicit", "Explicit FormKey override", RulePriority.ExplicitFormKeyOverride);
```

The selected tier is 6. The report retains the other assignments and explains why the explicit override won. Without that override, the creature rule wins; without either higher-priority rule, the fallback wins. No fallback is applied automatically.

Apply accepts the profile's corresponding values object: PowerValues, ActorValues, LocationValues, or EquipmentValues. Only supplied fields contribute assignments. An actor override setting PowerTier preserves its category and combat role. Location and equipment overrides follow the same behavior.

LocationValues supports PowerTier, EnemyTierRange and LootTierRange. A conceptual location can have Tier 3, an enemy range of 2–4, and a loot range of 2–3. These classifications do not generate encounters or loot.

EquipmentValues supports PowerTier, Kind and Material. Kind is Weapon, Armor, Shield, Ammunition, Clothing, Jewelry or Other; omitted kind/material remains unknown. These identity fields are optional classifications, not numerical balance inputs yet.

ActorValues retains its existing JSON shape, including nullable integer tiers. Existing actor configuration and diagnostics continue to use the same rule system. Range values serialize as objects with Minimum and Maximum tier objects, each containing Value. Tier and range constructors validate bounds. Typed configuration readers should use JsonSerializerOptions.RespectRequiredConstructorParameters = true so missing range bounds are rejected; the actor configuration reader enables this setting. Omitted PowerTier in a values object remains unclassified.

Every assignment requires a nonempty rule ID and reason. Reason reporting satisfies Phase 2's confidence-or-reason requirement; no numerical confidence score is introduced. Invalid values are rejected before partially adding their assignments. Range bounds are validated at construction.

## Current Integration

Actor classification uses the shared PowerProfile directly. LocationProfile and EquipmentProfile are ready for later rules; no world-wide location or equipment inference is introduced in Phase 2. Default actor rules still assign no tiers.

The conceptual TierAssignment is represented by the existing RuleDecision/ResolvedField decision records, rather than a second competing assignment type.

## Verification

Run `dotnet run --project tests/PowerProfiles/PowerProfiles.csproj`. The dependency-free checks cover unknown versus zero, manual/rule/fallback precedence, ties, reasons, partial assignments, all profile types, invalid bounds, invalid enum values, atomic validation, and JSON round trips.

