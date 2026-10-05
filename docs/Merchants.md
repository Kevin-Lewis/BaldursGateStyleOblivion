# Merchant stock

Phase 12 assigns static stock by shop identity, independently of actor PowerTier. Enable it with `EnableMerchantStock`; `MerchantConfigurationFile` optionally selects another `merchants.json`. The module runs after actor equipment/world loot and before quest rewards.

## Configuration

`BaldursGateStyleOblivion/merchants.json` contains editable profiles, ordered group rules, individual merchant `Overrides`, `PreservedLists`, racial material preferences, and their weight. Edit JSON directly or use `http://127.0.0.1:5078/merchants`. Individual assignments save/delete directly. Rerun the patcher after edits.

- `SelectionLevel` is a constant vanilla list eligibility benchmark for existing variants, not actor level or PowerTier. Lists using highest-eligible selection keep that behavior at this fixed benchmark; all-eligible lists retain eligible variety.
- `MaxEquipmentQuality` caps ordinary equipment, at most HighQuality. Ebony and Daedric are excluded from generated random stock regardless of this cap.
- `GlassPercent` (0–5) gives a separate Glass chance per source-list selection where that family contains Glass. Original quantities and nested empty chances still matter.
- `PreferredMaterial` overrides the race preference: Orcish, Elven, empty to disable, or null for automatic. Existing preferred material choices up to HighQuality receive extra weight. No unrelated equipment family is imported.
- Individual `GlassItems` specifies a narrow, concrete Glass offer at the profile's GlassPercent. One item is drawn from those alternatives per stock refresh, independently of ordinary stock quantities. Only ordinary, unenchanted Glass weapons/armor/ammunition are accepted.
- `Preserve` retains stock processing for that merchant. `PreservedLists` retains particular source branches.

Stock/class/faction/location rules are ordered; individual overrides win. General Store is the conservative fallback. Spell services are a final fallback rather than overriding archery, alchemy, or other established specialties. Barter gold is not treated as proof of wealth. Material recognition currently uses EditorIDs and the existing equipment quality classifier.

## Initial assignments

Ordinary general traders supply everyday goods; smiths supply conventional equipment with their existing blade/blunt/heavy/light distinctions. Rindir is a magical staff specialist, Seed-Neeus a general trader, Palonirya a luxury clothier, Melliwin an archery supplier, and Khafiz a fence. Brotherhood suppliers use Guild Vendor stock. Expert alchemists keep stronger static consumable variants.

Daenlin has a 1% chance of one plain Glass bow. Tertia Viducia has a 1% chance of one Glass boots-or-gauntlets item. These are design assignments to their recorded specialties, not claims that the original game already supplied these goods. No recurring Ebony/Daedric exceptions are configured. Existing individually authored fixed sales remain intact.

## Scope and safeguards

Private lists and stock container copies preserve shared adventure loot and unrelated shops. Personal inventory is processed only for categories the merchant sells; unsold carried equipment remains under actor equipment distribution. That module defers personal sale lists while merchant processing is enabled. Mixed personal sale/carried branches are retained for review.

Native counts, entry data, ChanceNone, calculate-for-each-count behavior, and eligible UseAll bundles are retained. All generated entry levels become one. Protected unique items and quest items are excluded from generated random pools; direct fixed sales remain. Scripts, unresolved/excluded dependencies, list mutation, cycles, and conflicting policies on a shared stock reference retain the affected branch with a reason. A Glass probability on a UseAll root requires individual review.

The already-static `VendorBooksRare` family is explicitly preserved for Phintias/Mach-Na because Modern Heretics has an on-read quest script. No merchant price, item stats, barter gold, spell skill requirements, quest entry conditions, or restock timer is changed. Existing merchant stock must refresh/reset in game; player-sold goods are not removed.

## Reports and validation

`.merchant-stock.json` reports profiles, evidence, resolved locations, personal/container plans, offered spells, scripts, and remaining level-gated sale branches. Script candidates are audited rather than rewritten. Fingerprint-matched contexts already curated by quest rewards are identified as belonging to that module.

- Native fixture checks: `dotnet run --project tests/Merchants`.
- Official candidate checks: `dotnet run --project tests/Merchants -- --check-patch <patch.esp> <game Data folder>`.
- Editor persistence/validation checks: `./tests/VerifyMerchantEditor.ps1` (uses an isolated configuration copy on port 5079).
- Earlier snapshot audit: `./tools/ActorResearch/AuditMerchants.ps1`; options `-Reports`, `-Output`, `-RewardConfig`. It writes searchable HTML, JSON, and a compact roster. Reachability there is not sale availability or refresh probability.

Gameplay verification remains necessary: compare freshly refreshed stock at low/high player levels, inspect specialist Glass rarity over restocks, test the Modern Heretics purchase/read trigger, and exercise DLC merchant unlocks. Mod-added custom scripts may need explicit exceptions.

## Current validation result

The official-load-order merchant pass made 267 branches static across 266 service actors, with 318 private lists and 61 private stock containers. The two preserved rare-book branches are already static; the coverage report finds zero remaining level-gated sale branches. The three merchant-associated scripts with player-level reads are fingerprint-reviewed quest reward contexts handled by the existing rewards module.

Main/editor builds, native merchant fixtures, written focused and combined candidate checks, equipment/merchant integration, deterministic merchant repeat, empty report-only output, editor persistence/deletion/invalid-input/stale-revision checks, and existing loot/reward fixtures passed. The final combined candidate is `artifacts/merchant-review/combined/BaldursGateStyleOblivion.esp` and was not installed into the live game. General Diagnostics/RecordDiscovery were disabled for that final run after an optional general diagnostic attempt was stopped during slow record checks. This is not a complete fresh general diagnostics result.
