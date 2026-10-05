namespace BaldursGateStyleOblivion.Core;

public sealed class PatcherSettings
{
    public bool ReportOnly { get; set; } = true;
    public bool EnableRecordDiscovery { get; set; } = true;
    public bool EnableDiagnostics { get; set; } = true;
    public bool EnableQuestRewards { get; set; } = true;
    public string RewardConfigurationFile { get; set; } = "";
    public bool EnableMerchantStock { get; set; } = true;
    public string MerchantConfigurationFile { get; set; } = "";
    public bool EnableWorldLoot { get; set; } = true;
    public string LootConfigurationFile { get; set; } = "";
    public bool EnableEquipmentDistribution { get; set; } = true;
    public string EquipmentConfigurationFile { get; set; } = "";
    public bool EnableActorDeleveling { get; set; } = true;
    public bool EnableDungeonDifficulty { get; set; } = true;
    public string DungeonConfigurationFile { get; set; } = "";
    public bool EnableGeographicEncounters { get; set; } = true;
    public bool EnableGeographicDiscovery { get; set; } = true;
    public string GeographicConfigurationFile { get; set; } = "";
    public bool EnableCreatureListDeleveling { get; set; } = true;
    public string CreatureListConfigurationFile { get; set; } = "";
    public bool EnableActorClassification { get; set; } = true;
    public List<string> IncludedPlugins { get; set; } = new();
    public List<string> ExcludedPlugins { get; set; } = new();
    public string ActorConfigurationFile { get; set; } = "";
    public string ReportDirectory { get; set; } = "Reports";

    public bool AllowsModule(bool enabled, bool modifiesGameplay) => enabled && (!ReportOnly || !modifiesGameplay);
}
