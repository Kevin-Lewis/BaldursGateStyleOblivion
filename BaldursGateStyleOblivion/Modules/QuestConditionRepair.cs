using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;

namespace BaldursGateStyleOblivion.Modules;

// Oblivion conditions expose quest parameters as raw uints, so the normal writer
// cannot remap them. Repair reviewed quest overrides after the final masters exist.
internal static class QuestConditionRepair
{
    private sealed record Plan(Dictionary<uint, FormKey> References, HashSet<ushort> Functions, int Count);
    private static readonly Dictionary<FormKey, Plan> Plans = new();
    public static void Reset() => Plans.Clear();

    public static void Remember(IQuestGetter quest, IOblivionModGetter source)
    {
        var references = new Dictionary<uint, FormKey>();
        var functions = new HashSet<ushort>();
        var count = 0;
        var conditions = quest.Conditions.Concat(quest.Stages.SelectMany(stage => stage.LogEntries).SelectMany(entry => entry.Conditions))
            .Concat(quest.Targets.SelectMany(target => target.Conditions));
        foreach (var condition in conditions)
        {
            if (condition.Function.ToString() is not ("GetStageDone" or "GetStage" or "GetQuestRunning" or "GetQuestVariable")) continue;
            var raw = unchecked((uint)condition.FirstParameter);
            if (raw == 0) continue;
            var index = (int)(raw >> 24);
            if (index > source.MasterReferences.Count) throw new InvalidDataException("Quest condition has an invalid source master index.");
            var mod = index == source.MasterReferences.Count ? source.ModKey : source.MasterReferences[index].Master;
            references[raw] = new FormKey(mod, raw & 0xFFFFFF);
            functions.Add((ushort)condition.Function);
            count++;
        }
        if (count > 0) Plans[quest.FormKey] = new(references, functions, count);
    }

    public static int Apply(string path)
    {
        if (Plans.Count == 0) return 0;
        Dictionary<ModKey, uint> indexes;
        using (var output = OblivionMod.CreateFromBinaryOverlay(path, OblivionRelease.Oblivion))
        {
            indexes = output.MasterReferences.Select((master, index) => (master.Master, Index: (uint)index))
                .ToDictionary(pair => pair.Master, pair => pair.Index);
            indexes[output.ModKey] = (uint)output.MasterReferences.Count;
        }
        uint Encode(FormKey key) => indexes.TryGetValue(key.ModKey, out var index) ? index << 24 | key.ID
            : throw new InvalidDataException($"Quest condition master missing from output: {key.ModKey}.");
        var plans = Plans.ToDictionary(pair => Encode(pair.Key), pair => pair.Value);
        var bytes = File.ReadAllBytes(path);
        var repaired = 0;
        var found = new HashSet<uint>();
        uint UInt(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        ushort Short(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
        string Tag(int offset) => Encoding.ASCII.GetString(bytes, offset, 4);

        void Record(int start, int end, Plan plan)
        {
            var matches = 0;
            for (var offset = start; offset < end;)
            {
                if (offset + 6 > end) throw new InvalidDataException("Truncated quest subrecord.");
                var tag = Tag(offset);
                var size = (int)Short(offset + 4);
                if (tag == "XXXX")
                {
                    size = checked((int)UInt(offset + 6));
                    offset += 10;
                    tag = Tag(offset);
                }
                var data = offset + 6;
                if (data + size > end) throw new InvalidDataException("Invalid quest subrecord size.");
                if (tag == "CTDA" && size >= 20 && plan.Functions.Contains(Short(data + 8))
                    && plan.References.TryGetValue(UInt(data + 12), out var target))
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(data + 12, 4), Encode(target));
                    matches++;
                }
                offset = data + size;
            }
            if (matches != plan.Count) throw new InvalidDataException("Written quest conditions do not match the reviewed source.");
            repaired += matches;
        }
        void Group(int start, int end)
        {
            for (var offset = start; offset < end;)
            {
                if (offset + 20 > end) throw new InvalidDataException("Truncated plugin record.");
                var tag = Tag(offset);
                var size = checked((int)UInt(offset + 4));
                var next = checked(offset + (tag == "GRUP" ? size : 20 + size));
                if (next > end || next <= offset) throw new InvalidDataException("Invalid plugin record size.");
                if (tag == "GRUP") Group(offset + 20, next);
                else if (tag == "QUST" && plans.TryGetValue(UInt(offset + 12), out var plan))
                {
                    if ((UInt(offset + 8) & 0x40000) != 0) throw new InvalidDataException("Reviewed quest override unexpectedly compressed.");
                    Record(offset + 20, next, plan);
                    found.Add(UInt(offset + 12));
                }
                offset = next;
            }
        }
        Group(0, bytes.Length);
        if (found.Count != plans.Count) throw new InvalidDataException("Reviewed quest override missing from output.");
        File.WriteAllBytes(path, bytes);
        return repaired;
    }
}