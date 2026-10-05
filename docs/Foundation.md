# Project Foundation Settings

General settings are registered with Synthesis's generated settings UI as Settings and stored in the patcher's supplemental data folder as `settings.json`. Missing settings use these defaults:

```json
{
  "ReportOnly": true,
  "EnableDiagnostics": true,
  "EnableActorClassification": true,
  "EnableRecordDiscovery": true,
  "IncludedPlugins": [],
  "ExcludedPlugins": [],
  "ReportDirectory": "Reports"
}
```

## Processing Controls

- IncludedPlugins: empty means all originating plugins; otherwise only listed plugins' records are processed.
- ExcludedPlugins: listed plugins' records are skipped, even if also included. Plugin names are case-insensitive filenames, including `.esm` or `.esp`.
- Filters apply to every record report and actor classification. The complete load order remains available for resolving references and looking up class, faction, race, and magic-effect data. They do not remove plugins from the game or discard overrides of included records.
- EnableActorClassification: controls actor inference and overrides. Diagnostics still report original actor data when this is disabled.
- EnableDiagnostics: controls JSON reports. Classification can run independently. A console/file log is always written.
- EnableRecordDiscovery: controls Phase 1 discovery reports. Discovery runs only when diagnostics are also enabled.
- ReportOnly: gameplay modules produce plans and reports while skipping writes to the patch. Module registration must explicitly declare whether it modifies gameplay when it cannot provide its own planning mode.

Synthesis still writes its pipeline ESP in report-only mode. On a standalone run this is empty; when building on a previous patcher's output, that input is retained. Report-only is not a promise to suppress Synthesis's output file or undo previous patchers' work.

## Reports and Logs

The default destination is `Reports` inside the supplemental data folder supplied by Synthesis. Without that folder, the fallback is `%LOCALAPPDATA%\BaldursGateStyleOblivion\Reports`.

ReportDirectory can specify an absolute path, or a path relative to the supplemental data folder (or fallback folder). The patcher prints the resolved destination on every run. Reports and the `.log` use the output ESP's filename stem. The log captures module selection, effective plugin filters, report locations, completion, and patcher exceptions.

Files from previous runs are retained; disabled modules do not refresh their reports. Check the current log and settings before reading an older report. Completed JSON reports are overwritten on a subsequent run, and the log is replaced each run.

Custom `actor-classification.json` in the supplemental data folder takes precedence over the bundled classification defaults. General settings do not replace that rule configuration.

The Synthesis supplemental-data API and settings registration were verified through CLI runs with `-e`; the desktop UI has not been exercised here.

## Verification

After building, run `tests/VerifyFoundation.ps1` with DataFolder and LoadOrder paths. It checks native configuration loading, inclusion/exclusion precedence, full reference context, module switches, report-only gating, deterministic summary output, persistent logs, and empty standalone ESPs.


Equipment distribution is enabled by `EnableEquipmentDistribution`; `EquipmentConfigurationFile` can select a different `equipment.json`. Report-only mode plans equipment changes and writes `.equipment-distribution.json` without adding inventory overrides or private lists to the patch. See [Actor configuration](ActorConfiguration.md#equipment-distribution-phase-9) for profiles and material rarity.

World loot uses `EnableWorldLoot` and `LootConfigurationFile` (`loot.json`). It runs after equipment and geography, writes `.world-loot.json`, and honors report-only mode without inserting private lists, containers or inventory overrides. See [Actor configuration](ActorConfiguration.md#world-loot) for scarcity profiles and the World loot editor.

Quest rewards and artifact handling are enabled by `EnableQuestRewards`. `RewardConfigurationFile` selects the editable `rewards.json`; reviewed lists/scripts are processed after world loot and before diagnostics. Report-only mode produces the same decisions without writing reward or artifact gameplay records. Artifact protections are also enforced by equipment and loot distribution. The dedicated report is `.quest-rewards.json`.
