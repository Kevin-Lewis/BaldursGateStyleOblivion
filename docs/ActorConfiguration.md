# Actor Tier Editor

Double-click **Edit Actor Tiers.cmd** in the repository root. It opens the local browser editor. Keep its terminal window open while editing.

Everything is saved in **BaldursGateStyleOblivion/actor-classification.json**:

- `ResearchModel`: OpenAI API model, currently `gpt-6.1-sol`.
- `Groups`: group rules, organized by actor family.
- `FormKeyOverrides`: individual actors, with their names, tiers, handling, concise AI notes, and source links.

There are no approval states or acceptance commands. A saved tier is an assignment. Individual assignments take precedence over group rules.

## Editing

In the actor table, enter a tier from 0–10 and click Save. Change Handling when needed. Leave an individual tier or handling blank to use the group rules. Existing descriptions, reasons, sources, and other classification fields are preserved.

Expand **Edit group rules** to set a group's tier and enable or disable a rule. Search and filter the actor table to inspect the result. Narrower rules at the same priority must appear first. Matching conditions can be edited directly in the JSON.

You can also open the JSON and change `PowerTier` directly. Entries have Name next to PowerTier so they are easy to find. After external JSON edits, refresh the browser editor. Invalid tiers or configuration are rejected before the file is replaced.

The generated `artifacts/actor-catalog.html` remains a read-only snapshot; the local editor is the version with save controls. It listens only on this computer at `http://127.0.0.1:5078`.

## Patcher configuration

The development run in `artifacts/phase0-data` is configured to read this exact JSON file through `ActorConfigurationFile`, so saves take effect on its next patcher run without rebuilding. In Synthesis, set that field to this file's absolute path, or place the single JSON in the patcher's extra-data directory. A blank field uses the extra-data copy when present, otherwise the bundled copy.

This remains a classification framework. Saving tiers does not yet delevel actors or introduce gameplay formulas.

## Research

The utility writes new researched actors directly into the same file. Existing actor entries are never overwritten, including when research is refreshed. Remove an entry if you want it generated again.

New research focuses on lore; record identity identifies the intended actor form. Existing four entries retain their original GPT-5.5 notes and tiers until edited or regenerated. The proposed expanded tier rubric is still awaiting agreement.

UESP articles are read through its public wiki API, with text, canonical URL, and revision cached. Disambiguation pages follow the person link. UESP-only web search can supplement missing evidence. Sources must come from retrieved articles or consulted search results. Missing evidence leaves PowerTier unassigned.

Developer commands, when needed:

```powershell
dotnet run --project tools/ActorResearch -- edit
dotnet run --project tools/ActorResearch -- catalog
dotnet run --project tools/ActorResearch -- research --formkeys research/calibration-actors.txt
```

The model can be changed in `ResearchModel`. API access reads `OPENAI_API_KEY` from the process or Windows user environment without printing it. Source and research caches are under `research/cache`, separate from the editable configuration. API keys stay out of JSON and chat.
