using System.Text.Json;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis;

namespace BaldursGateStyleOblivion.Core;

internal sealed class PatcherRun : IDisposable
{
    public PatcherSettings Settings { get; }
    public string DataDirectory { get; }
    public string ReportDirectory { get; }
    private readonly string reportStem;
    private readonly HashSet<ModKey> included;
    private readonly HashSet<ModKey> excluded;
    private readonly StreamWriter log;

    public PatcherRun(IPatcherState<IOblivionMod, IOblivionModGetter> state, PatcherSettings settings)
    {
        Settings = settings;
        included = settings.IncludedPlugins.Select(name => ModKey.FromNameAndExtension(name)).ToHashSet();
        excluded = settings.ExcludedPlugins.Select(name => ModKey.FromNameAndExtension(name)).ToHashSet();
        DataDirectory = state.ExtraSettingsDataPath?.ToString()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaldursGateStyleOblivion");
        if (string.IsNullOrWhiteSpace(settings.ReportDirectory))
            throw new ArgumentException("ReportDirectory must not be empty.");
        ReportDirectory = Path.GetFullPath(Path.IsPathRooted(settings.ReportDirectory)
            ? settings.ReportDirectory : Path.Combine(DataDirectory, settings.ReportDirectory));
        Directory.CreateDirectory(ReportDirectory);
        reportStem = Path.GetFileNameWithoutExtension(state.OutputPath.ToString());
        log = new StreamWriter(Path.Combine(ReportDirectory, reportStem + ".log"), append: false) { AutoFlush = true };
        Log($"Reports and log: {ReportDirectory}");
        Log($"ReportOnly: {settings.ReportOnly}. Synthesis still writes its pipeline ESP.");
        Log($"Included plugins: {(included.Count == 0 ? "all" : string.Join(", ", included.OrderBy(key => key.ToString())))}");
        Log($"Excluded plugins: {string.Join(", ", excluded.OrderBy(key => key.ToString()))}");
    }

    // Filter by the originating plugin; excluded plugins always win.
    public bool Includes(ModKey plugin) => !excluded.Contains(plugin) && (included.Count == 0 || included.Contains(plugin));

    public void Module(string name, bool enabled, bool modifiesGameplay, Action action)
    {
        if (!Settings.AllowsModule(enabled, modifiesGameplay))
        {
            Log($"Skipped {name}: {(!enabled ? "disabled" : "report-only mode")}");
            return;
        }
        Log($"Running {name}.");
        action();
    }

    public string ReportPath(string extension) => Path.Combine(ReportDirectory, reportStem + extension);

    public void WriteReport<T>(string extension, T report, JsonSerializerOptions options)
    {
        var path = ReportPath(extension);
        using var output = File.Create(path);
        JsonSerializer.Serialize(output, report, options);
        Log($"Report: {path}");
    }

    public void Log(string message)
    {
        Console.WriteLine(message);
        log.WriteLine(message);
    }

    public void Dispose() => log.Dispose();
}


