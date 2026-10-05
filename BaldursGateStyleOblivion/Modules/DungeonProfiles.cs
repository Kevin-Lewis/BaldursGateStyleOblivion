using System.Text.RegularExpressions;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;

namespace BaldursGateStyleOblivion.Modules;
internal sealed record DungeonSite(string FormKey, string Name, string[] Cells, DungeonDefinition Profile, string Rule, string Evidence);
internal static class DungeonProfiles
{
    internal static Dictionary<FormKey, DungeonSite> BuildSites(DungeonSettings settings,
        IReadOnlyDictionary<FormKey, FormKey[]> connected, IReadOnlyDictionary<FormKey, ICellGetter> cells,
        IReadOnlyDictionary<FormKey, List<IPlacedObjectGetter>> entrances,
        IReadOnlyDictionary<FormKey, string>? encounterNames = null)
    {
        var result = new Dictionary<FormKey, DungeonSite>();
        void Add(string key, string name, FormKey[] rooms, DungeonDefinition profile, string rule, string evidence = "")
        {
            var site = new DungeonSite(key, rule == "Individual dungeon override" ? profile.Name ?? name : name, rooms.Select(room => room.ToString()).ToArray(), profile, rule, evidence);
            foreach (var room in rooms) result[room] = site;
        }
        foreach (var pair in settings.FormKeyOverrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var rooms = (pair.Value.Cells.Count == 0 && connected.TryGetValue(FormKey.Factory(pair.Key), out var inferred)
                ? inferred.Select(key => key.ToString()) : pair.Value.Cells.Count == 0 ? [pair.Key] : pair.Value.Cells).Select(value => FormKey.Factory(value)).ToArray();
            if (rooms.Any(key => !cells.TryGetValue(key, out var cell) || cell.Flags?.HasFlag(Cell.Flag.IsInteriorCell) != true))
                throw new InvalidDataException($"Manual dungeon {pair.Key} references a missing or exterior cell.");
            if (rooms.Any(result.ContainsKey)) throw new InvalidDataException("Manual dungeon sites overlap.");
            Add(pair.Key, pair.Key, rooms, pair.Value, "Individual dungeon override");
        }
        foreach (var rooms in connected.Values.DistinctBy(rooms => rooms[0]).OrderBy(rooms => rooms[0].ToString(), StringComparer.Ordinal))
        {
            var remaining = rooms.Where(room => !result.ContainsKey(room)).ToArray();
            if (remaining.Length == 0) continue;
            // Development warehouses and holding cells are not playable dungeon identities.
            if (remaining.Any(room => Regex.IsMatch(cells[room].EditorID ?? "", @"Warehouse|HoldingCell|^Test|^Dummy", RegexOptions.IgnoreCase))) continue;
            var markers = remaining.SelectMany(room => entrances.GetValueOrDefault(room, [])).DistinctBy(marker => marker.FormKey).ToArray();
            var text = string.Join(" ", remaining.Select(room => $"{cells[room].EditorID} {cells[room].Name} {encounterNames?.GetValueOrDefault(room)}"));
            var dungeon = markers.Any(marker => marker.MapMarker!.Types?.Any(type => type.ToString() is "Cave" or "FortRuin" or "ElvenRuin" or "Mine") == true);
            if (!dungeon && !Regex.IsMatch(text, @"Cave|Mine|Fort|Ruin|Ayleid", RegexOptions.IgnoreCase)) continue;
            var key = remaining[0].ToString();
            var name = markers.FirstOrDefault()?.MapMarker?.Name ?? cells[remaining[0]].Name ?? cells[remaining[0]].EditorID ?? key;
            var evidence = text + " " + string.Join(" ", markers.Select(marker => marker.MapMarker!.Name + " " + string.Join(" ", marker.MapMarker.Types ?? [])));
            var (profile, rule) = DungeonConfiguration.Select(settings, key, evidence);
            Add(key, name, remaining, profile, rule, evidence);
        }
        return result;
    }
}
