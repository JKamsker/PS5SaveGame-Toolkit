using System.Globalization;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Persistent item identities are allocated only by committed append/remove operations.</summary>
internal sealed class GearboxInventoryIdentities(GearboxDocument document)
{
    private readonly Dictionary<string, List<string>> _lists = (document.Profile
        ? new[] { GearboxInventory.Bank, GearboxInventory.LostLoot } : [GearboxInventory.Backpack])
        .ToDictionary(list => list, _ => new List<string>(), StringComparer.Ordinal);
    private readonly Dictionary<string, (string List, int Index)> _locations = new(StringComparer.Ordinal);
    internal bool Initialized { get; private set; }
    internal int NextId { get; private set; }
    internal IEnumerable<string> Lists => _lists.Keys;
    internal bool Owns(string list) => _lists.ContainsKey(list);
    internal string At(string list, int index) => _lists[list][index];
    internal bool TryLocation(string id, out (string List, int Index) location) => _locations.TryGetValue(id, out location);

    internal InventoryIdentity Capture() => new(_lists.ToDictionary(pair => pair.Key,
        pair => pair.Value.ToArray(), StringComparer.Ordinal), NextId, Initialized);

    internal void Restore(InventoryIdentity state)
    {
        if (!_lists.Keys.Order(StringComparer.Ordinal).SequenceEqual(state.Lists.Keys.Order(StringComparer.Ordinal)) ||
            state.NextId < 0 || state.Lists.Any(pair => pair.Value.Length !=
                (state.Initialized ? document.Root.All(pair.Key).Count() : 0)) ||
            state.Lists.SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).Count() != state.Lists.Sum(pair => pair.Value.Length))
            throw new InvalidOperationException("Invalid Gearbox inventory identity snapshot.");
        foreach (var (list, ids) in state.Lists) _lists[list] = ids.ToList();
        NextId = state.NextId;
        Initialized = state.Initialized;
        RebuildLocations();
    }

    internal void Initialize()
    {
        if (Initialized) return;
        foreach (var list in _lists.Keys.ToArray())
            _lists[list] = Enumerable.Range(0, document.Root.All(list).Count()).Select(_ => Allocate()).ToList();
        Initialized = true;
        RebuildLocations();
    }

    internal void Remove(string list, int index)
    {
        _lists[list].RemoveAt(index);
        RebuildLocations();
    }

    internal string Append(string list, string? movedId)
    {
        var id = movedId ?? Allocate();
        _lists[list].Add(id);
        RebuildLocations();
        return id;
    }

    private string Allocate() => "item-" + (NextId++).ToString(CultureInfo.InvariantCulture);

    private void RebuildLocations()
    {
        _locations.Clear();
        foreach (var (list, ids) in _lists)
            for (var index = 0; index < ids.Count; index++) _locations.Add(ids[index], (list, index));
    }
}

internal sealed record InventoryIdentity(Dictionary<string, string[]> Lists, int NextId, bool Initialized);
