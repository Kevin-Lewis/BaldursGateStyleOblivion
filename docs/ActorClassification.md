# Actor Classification

Classification produces diagnostics only. Each assigned dimension records its value, rule, and reason; the Unclassified array lists dimensions without evidence. Unknown differs from an explicit Other or None value.

Dimensions are PowerTier, ActorCategory, ActorNature, CombatRole, FactionRole, BossStatus, QuestImportance, EquipmentTier, MagicTier, and LocationTier. Category, nature, combat role, and boss status are independent. Tiers are validated against the [PowerTier scale](PowerTiers.md).

## Configuration

Edit `BaldursGateStyleOblivion/actor-classification.json` before building. It is copied beside the executable. A file with the same name in the Synthesis supplemental data folder takes precedence over that bundled file.

Rules match exact EditorIDs for Faction, Class, and Race; CreatureType matches the game's type name. Plugin matches the originating plugin filename. Optional SourcePlugin restricts any rule to records originating in that plugin, regardless of the winning override plugin. Matches are case-insensitive.

Precedence is resolved per dimension, highest first:

1. ExplicitFormKeyOverride
2. PluginSpecific
3. NamedSpecialActor
4. Faction
5. Class
6. RaceCreature
7. Location
8. GenericArchetype
9. Fallback

Rules require a unique Id, explicit Priority, Evidence, Match, and partial Values. Reason is optional; when omitted, diagnostics explain the matched evidence. ExplicitFormKeyOverride is reserved for FormKeyOverrides. SourcePlugin restricts matching but does not change the configured priority.

Within one priority, the first matching rule in configuration order wins each dimension. Omitted values preserve assignments from other rules. Each reported dimension has a Selected decision and a Superseded array, including values, rule IDs, priorities, and reasons.

Supported evidence also includes exact EditorID, Name, FormKey, and RecordType (NPC or Creature). Always matches every actor and is useful for deliberate fallback rules. Location and Archetype are reserved evidence inputs: no values are collected for them yet, so those conditions cannot currently match. Priority controls precedence independently of evidence.

Example rule:

```json
{
  "Id": "special-mage",
  "Priority": "NamedSpecialActor",
  "Evidence": "EditorID",
  "Match": "ExampleActor",
  "Reason": "Curated combat role for this actor",
  "Values": { "CombatRole": "Mage" }
}
```
Example partial override (replace the illustrative key with a real actor FormKey):

```json
"FormKeyOverrides": {
  "012345:Example.esp": {
    "PowerTier": 7,
    "BossStatus": "Major"
  }
}
```

Initial rules classify a few faction identities, class combat roles, and explicit Daedra, Undead, and Horse creature types. Generic Creature types are left unknown rather than assumed to be animals. Boss status, quest importance, faction role, and tiers remain unclassified unless configured. Location tier is reserved for later context analysis; base actors can appear in multiple locations.

Output: `<patch-name>.actor-classifications.json`, in the configured persistent report directory (see [foundation settings](Foundation.md)). These are preliminary classifications for review, not a complete actor taxonomy.



Grouped configuration, actor handling categories, and AI-assisted review are documented in [Actor Configuration and Research](ActorConfiguration.md).
