using System.Text;
using System.Text.RegularExpressions;
using BaldursGateStyleOblivion.Discovery;
using Mutagen.Bethesda.Oblivion;

namespace BaldursGateStyleOblivion.Modules;

// Vanilla expressions encode numbers as ASCII. An eight-digit constant occupies the same
// space as r[index] X[GetLevel opcode] [zero argument bytes], preserving all jump offsets.
internal static class RewardScriptEditor
{
    public static (byte[] Bytes, string Source, int Replacements) FixLevel(IScriptFieldsGetter fields, int level, string fingerprint)
    {
        if (RewardRecords.Fingerprint(fields) != fingerprint) throw new InvalidDataException("Reviewed script fingerprint changed; retained.");
        var source = fields.SourceCode ?? throw new InvalidDataException("Script source unavailable.");
        var count = ScriptDiscovery.Scan(source).Count(signal => signal.Kind == "PlayerLevelRead");
        var references = fields.References.ToArray();
        var playerIndexes = references.Select((r, i) => (r, i)).Where(pair => pair.r is IScriptObjectReferenceGetter form &&
            form.Reference.FormKey.ToString() == "000014:Oblivion.esm").Select(pair => pair.i + 1).ToHashSet();
        var bytes = fields.CompiledScript.GetValueOrDefault().ToArray();
        var replacement = Encoding.ASCII.GetBytes(level.ToString("D8", System.Globalization.CultureInfo.InvariantCulture));
        var changed = 0;
        for (var offset = 0; offset + 8 < bytes.Length; offset++)
        {
            if (bytes[offset] != (byte)'r' || !playerIndexes.Contains(BitConverter.ToUInt16(bytes, offset + 1)) ||
                bytes[offset + 3] != (byte)'X' || BitConverter.ToUInt16(bytes, offset + 4) != 0x1050 ||
                BitConverter.ToUInt16(bytes, offset + 6) != 0 || bytes[offset + 8] != (byte)' ') continue;
            replacement.CopyTo(bytes, offset); changed++; offset += 7;
        }
        if (count == 0 || count != changed) throw new InvalidDataException("Compiled player-level expressions did not match reviewed source; retained.");
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var comment = lines[index].IndexOf(';');
            var code = comment < 0 ? lines[index] : lines[index][..comment];
            if (ScriptDiscovery.Scan(code).Any(signal => signal.Kind == "PlayerLevelRead"))
                lines[index] = Regex.Replace(code, @"\bplayer\s*\.\s*getlevel\b", level.ToString(), RegexOptions.IgnoreCase) +
                    (comment < 0 ? "" : lines[index][comment..]);
        }
        return (bytes, string.Join("\n", lines), changed);
    }
}
