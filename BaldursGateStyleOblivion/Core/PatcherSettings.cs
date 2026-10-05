namespace BaldursGateStyleOblivion.Core;

public sealed class PatcherSettings
{
    public bool ReportOnly { get; set; } = true;
    public bool EnableRecordDiscovery { get; set; } = true;
    public bool EnableDiagnostics { get; set; } = true;
    public bool EnableActorDeleveling { get; set; } = true;
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


