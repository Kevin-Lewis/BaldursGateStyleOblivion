using System.Security.Cryptography;
using System.Text;
using BaldursGateStyleOblivion.Core;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;

namespace BaldursGateStyleOblivion.Modules;

internal static class RewardRecords
{
    internal static string PathFor(PatcherRun run)
    {
        if (!string.IsNullOrWhiteSpace(run.Settings.RewardConfigurationFile)) return Path.GetFullPath(run.Settings.RewardConfigurationFile, run.DataDirectory);
        var path = Path.Combine(run.DataDirectory, "rewards.json");
        return File.Exists(path) ? path : Path.Combine(AppContext.BaseDirectory, "rewards.json");
    }
    internal static HashSet<FormKey> Protected(RewardSettings settings) => settings.Artifacts
        .Where(pair => pair.Value.ExcludedFromNormalization).Select(pair => FormKey.Factory(pair.Key)).ToHashSet();
    internal static string Fingerprint(ILeveledItemGetter list) => Hash($"{(int)(list.Flags ?? 0)}|{list.ChanceNone}\n" +
        string.Join("\n", list.Entries.Select(e => $"{e.Level}|{e.Reference.FormKey}|{e.Count}")));
    internal static string Fingerprint(IScriptFieldsGetter fields) => Hash((fields.SourceCode ?? "") + "\n" +
        Convert.ToHexString(fields.CompiledScript.GetValueOrDefault().Span));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
