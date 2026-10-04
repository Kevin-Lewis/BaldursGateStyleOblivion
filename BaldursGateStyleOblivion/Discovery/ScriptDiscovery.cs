using System.Text.RegularExpressions;
using Mutagen.Bethesda.Oblivion;

namespace BaldursGateStyleOblivion.Discovery;

internal sealed record ScriptSignal(int Line, string Command, string? Receiver, string Kind, string Source, string? ActorValue = null);

internal static class ScriptDiscovery
{
    private static readonly Regex Commands = new(
        @"(?:(?<receiver>\b[a-z_][a-z0-9_]*)\s*\.\s*)?\b(?<command>AddItemNS|AddItem|AddSpell|GetLevel|SetStage|SetLevel|SetActorValue|ModActorValue|ForceActorValue|SetAV|ModAV|ForceAV)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ScriptSignal[] Scan(string? source)
    {
        if (source is null) return [];
        var matches = new List<ScriptSignal>();
        var lines = source.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // Ignore comments and quoted text without changing token positions.
            var code = lines[i].ToCharArray();
            var quoted = false;
            for (var j = 0; j < code.Length; j++)
            {
                if (code[j] == '"') { quoted = !quoted; code[j] = ' '; }
                else if (!quoted && code[j] == ';') { Array.Fill(code, ' ', j, code.Length - j); break; }
                else if (quoted) code[j] = ' ';
            }
            var sanitized = new string(code);
            foreach (Match match in Commands.Matches(sanitized))
            {
                var command = match.Groups["command"].Value;
                var receiver = match.Groups["receiver"].Success ? match.Groups["receiver"].Value : null;
                var kind = command.Equals("GetLevel", StringComparison.OrdinalIgnoreCase)
                    ? string.Equals(receiver, "player", StringComparison.OrdinalIgnoreCase) ? "PlayerLevelRead" : "ActorLevelRead"
                    : command.Equals("SetStage", StringComparison.OrdinalIgnoreCase) ? "QuestStageChange"
                    : command.Equals("SetLevel", StringComparison.OrdinalIgnoreCase) ? "ActorLevelWrite"
                    : command.Contains("ActorValue", StringComparison.OrdinalIgnoreCase) || command.EndsWith("AV", StringComparison.OrdinalIgnoreCase) ? "ActorStatWrite" : "GrantCandidate";
                var actorValue = kind == "ActorStatWrite" ? Regex.Match(sanitized[(match.Index + match.Length)..], @"^\s+(\w+)").Groups[1].Value : null;
                matches.Add(new(i + 1, command, receiver, kind, lines[i].Trim(), actorValue));
            }
        }
        return matches.ToArray();
    }

    public static object Inspect(IScriptFieldsGetter? script) => new
    {
        SourceAvailable = !string.IsNullOrWhiteSpace(script?.SourceCode),
        SourceCode = script?.SourceCode,
        CompiledBytes = script?.CompiledScript?.Length,
        ReferencedFormKeys = script?.EnumerateFormLinks().Select(link => link.FormKey)
            .Where(key => !key.IsNull).Distinct().OrderBy(key => key.ToString(), StringComparer.Ordinal)
            .Select(key => key.ToString()).ToArray(),
        Signals = Scan(script?.SourceCode)
    };
}
