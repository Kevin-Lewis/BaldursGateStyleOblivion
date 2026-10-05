# Phase 5: geographic review

Location profiles now drive encounter weighting when EnableGeographicEncounters is enabled and ReportOnly is false. Actor tiers and levels remain unchanged.

Open the local editor and choose **Geographic review** (`/geography`). It defaults to cells with recorded actors and uses server-side paging. Search by name, cell FormKey, worldspace or plugin. Filter categories and overrides. The proposed typical range and observed possible-threat range are separate: exceptions are allowed, and guards/civilians do not automatically make a settlement dangerous. Possible actor records are not simultaneous spawn counts.

Save and Delete override edit `BaldursGateStyleOblivion/geography.json` immediately. Group categories, ranges, reasons and enable flags are editable in the page; selectors can be edited directly in JSON. Saved settings take effect in gameplay after rerunning the patcher. There is no pending/approval workflow. Rerun the patcher to refresh observed data after load-order, actor or creature-list changes.

Configuration priority is an individual cell FormKey override, then a matching plugin-specific group, then the first matching ordinary group. A group can require a location regex, worldspace regex, region regex and/or evidence signal. All specified selectors must match. Unmatched locations remain unclassified. Proposals and ranges are provisional; Safe and Extreme are available without forcing locations into them.

Discovery reads winning non-deleted records, preserving plugin exclusions. It maps NPCs, creatures and general placed encounter-list references to their containing cells. Persistent worldspace references are assigned to their physical grid cells when available. Nested list leaves include the current Phase 4 helper pools; original pools and shared pool locations are reported separately. Geographic variants use private helper pools; only applicable placed encounter references are redirected.

Evidence includes cell/worldspace/region identities, road geometry, factions, possible actor tiers, original/current pool references, static quest references, door-connected rooms and nearby entrance markers. A nearby entrance marker is a heuristic; it is not proof of dungeon identity. A door-connected interior site can contain multiple danger profiles. Quest associations are references, not a claim that every encounter is required by that quest.

Reports:

- `*.geography.json`: cell proposals, observed encounters, reasons, confidence, review notes, connections and evidence.
- `*.geographic-encounters.json`: placement redirects, generated pool entries, ranges, applied rules and preservation reasons. ReportOnly plans these changes without applying them.
- `*.geographic-shared-pools.json`: original encounter pools and the cells reaching them; identifies pools that cannot safely be changed globally for one location.

`EnableGeographicDiscovery` and `GeographicConfigurationFile` control this module independently of diagnostics. Reports are deterministic. Road proximity is measured from cell center to road points/segments; it does not make the entire cell safe. Conditional and initially disabled placements are included and identified. Script-created encounters, moving actors and dynamic teleport destinations may not be fully represented. Region links are explicit cell data; polygon-only region coverage is not inferred.

Review typical ranges first, then exceptional/quest locations and locations with missing evidence. Geographic pool transformations favor in-range entries up to 3:1, retain out-of-range entries, native counts and ChanceNone, and reuse copies for equal ranges. Quest-associated locations, scripted actors or lists, templates, UseAll, cycles, unresolved references, excluded plugins, explicit Preserve and CuratedPool decisions remain guarded. No stat or loot balancing belongs to this deliverable.

## Category defaults and custom ranges

Each danger category supplies an editable default typical tier range: Civilized 0–2, Safe 0–1, LowDanger 1–3, Moderate 2–4, Dangerous 3–5, Severe 3–6, Extreme 5–9. Special has no default and is handcrafted. These ranges describe the tiers to favor, not hard eligibility limits; probability adjustments favor available in-range actors.

The editor updates the range when you change a category. Check **Custom range** to keep different limits for a group or individual location. Uncheck it to return to category defaults. The **Default ranges by danger category** section edits the global defaults. In JSON, `CategoryDefaults` contains those ranges; null `MinimumTier` and `MaximumTier` inherit the selected category, while two explicit values define a custom range. Existing ranges that differed from the starting defaults were preserved.

Tier 7+ actors are rare exceptional individuals. An upper typical-range limit permits their relevance; it does not call for equal representation of every tier or adding legendary actors to ordinary pools. Existing encounter availability determines whether these actors can appear; no special tier 7+ rule is added.

Road children resolve independently of worldspace headers: a DLC header-only override does not hide the base game road network. NearRoad uses a 10240-unit (2.5-cell) cell-center distance. NearSettlement uses active City, Settlement and Tavern markers in the same worldspace within 6144 units (1.5 cells). Both are proposals; existing hostile-site rules and individual overrides retain priority. Details show the road source plugin and nearest settlement name and distance. Disabled markers do not establish settlement proximity.
