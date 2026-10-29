using SaveToolkit.Sdk;
using System.Globalization;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Session identities are independent of serial contents and list positions.</summary>
internal sealed class GearboxInventory
{
    public const string Backpack = "inventory_items";
    public const string Bank = "bank_inventory_list";
    public const string LostLoot = "lost_loot_inventory_list";
    internal const int DirectLookupWorkLimit = 4096;
    private readonly GearboxDocument _document;
    private readonly Dictionary<string, List<string>> _ids;
    private readonly Dictionary<string, (string List, int Index)> _locations = new(StringComparer.Ordinal);
    private bool _identitiesInitialized;
    private int _nextId;
    private ProtoMessage? _indexedRoot;
    private ProtoMessage? _directRoot;
    private InventoryEntry[] _entries = [];
    private Dictionary<string, InventoryEntry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InventoryEntry> _directEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] Bytes, SerialInfo Info)> _serials = new(StringComparer.Ordinal);

    internal int LastDirectFieldsVisited { get; private set; }
    internal int LastDirectRecordsVisited { get; private set; }
    internal bool LastDirectLookupExceeded { get; private set; }

    public GearboxInventory(GearboxDocument document)
    {
        _document = document;
        _ids = (document.Profile ? new[] { Bank, LostLoot } : [Backpack])
            .ToDictionary(list => list, _ => new List<string>(), StringComparer.Ordinal);
    }

    public IEnumerable<InventoryEntry> Entries { get { Index(); return _entries; } }
    public InventoryEntry Find(string id)
    {
        if (ReferenceEquals(_indexedRoot, _document.Root) && _byId.TryGetValue(id, out var indexed)) return indexed;
        if (TryFindDirect(id, out var entry)) return entry;
        throw new SaveFormatException("The item identity is no longer present.");
    }

    private bool TryFindDirect(string id, out InventoryEntry entry)
    {
        entry = null!;
        LastDirectFieldsVisited = 0;
        LastDirectRecordsVisited = 0;
        LastDirectLookupExceeded = false;
        if (!ReferenceEquals(_directRoot, _document.Root))
        {
            _directRoot = _document.Root;
            _directEntries.Clear();
            _serials.Clear();
        }
        if (_directEntries.TryGetValue(id, out var cached)) { entry = cached; return true; }
        if (_locations.TryGetValue(id, out var location)) return TryEntry(location.List, location.Index, out entry);
        if (!TryCanonicalItemId(id, out var number))
            return false;
        // Cold targeted lookup uses the stable numeric identity directly. Full identity lists are
        // materialized only by view/structural paths; this keeps one unloaded request bounded.
        if (!_identitiesInitialized && !_document.Profile && number < DirectLookupWorkLimit)
            return TryEntry(Backpack, number, out entry, id);
        if (!_identitiesInitialized && _document.Profile && number < DirectLookupWorkLimit)
        {
            var bankCount = CountBounded(Bank);
            if (bankCount < 0) return false;
            if (number < bankCount) return TryEntry(Bank, number, out entry, id);
            return TryEntry(LostLoot, number - bankCount, out entry, id);
        }
        return false;
    }

    private bool TryEntry(string list, int index, out InventoryEntry entry, string? identity = null)
    {
        entry = null!;
        var seen = 0;
        ProtoField? field = null;
        foreach (var candidate in _document.Root.Fields)
        {
            if (++LastDirectFieldsVisited > DirectLookupWorkLimit)
            {
                LastDirectLookupExceeded = true;
                return false;
            }
            if (candidate.Spec?.Name != list) continue;
            if (++LastDirectRecordsVisited > DirectLookupWorkLimit)
            {
                LastDirectLookupExceeded = true;
                return false;
            }
            if (seen++ == index) { field = candidate; break; }
        }
        if (field is null) return false;
        entry = new InventoryEntry(identity ?? _ids[list][index], list, index, field);
        _directEntries[entry.Id] = entry;
        _directRoot = _document.Root;
        return true;
    }

    private void Index()
    {
        EnsureIdentities();
        if (ReferenceEquals(_indexedRoot, _document.Root)) return;
        _entries = _ids.SelectMany(pair => _document.Root.All(pair.Key)
            .Select((record, index) => new InventoryEntry(pair.Value[index], pair.Key, index, record))).ToArray();
        _byId = _entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        _directEntries.Clear();
        foreach (var entry in _entries) _directEntries[entry.Id] = entry;
        _indexedRoot = _document.Root;
        _directRoot = _document.Root;
    }
    public InventoryIdentity Capture()
    {
        return new(_identitiesInitialized
            ? _ids.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal)
            : _ids.ToDictionary(p => p.Key, _ => Array.Empty<string>(), StringComparer.Ordinal), _nextId, _identitiesInitialized);
    }
    public SerialInfo Info(string id)
    {
        var serial = Find(id).Serial;
        if (_serials.TryGetValue(id, out var cached) && ReferenceEquals(cached.Bytes, serial)) return cached.Info;
        var info = new SerialReader(_document.Root.Schema.Wonderlands).Read(serial);
        _serials[id] = (serial, info);
        return info;
    }
    public void Restore(InventoryIdentity identity)
    {
        if (!identity.Initialized)
        {
            foreach (var list in _ids.Keys.ToArray()) _ids[list].Clear();
            _nextId = identity.NextId;
            _identitiesInitialized = false;
            _locations.Clear();
            _indexedRoot = null;
            _directRoot = null;
            _directEntries.Clear();
            _serials.Clear();
            return;
        }
        EnsureIdentities();
        if (!_ids.Keys.Order().SequenceEqual(identity.Lists.Keys.Order()) ||
            identity.Lists.Any(p => p.Value.Length != _document.Root.All(p.Key).Count()) ||
            identity.Lists.SelectMany(p => p.Value).Distinct().Count() != identity.Lists.Sum(p => p.Value.Length))
            throw new InvalidOperationException("Invalid Gearbox inventory identity snapshot.");
        foreach (var (list, ids) in identity.Lists) _ids[list] = ids.ToList();
        _nextId = identity.NextId;
        _identitiesInitialized = true;
        _indexedRoot = null;
        _directRoot = null;
        _directEntries.Clear();
        _serials.Clear();
        RebuildLocations();
    }

    public void SetSerial(string id, byte[] serial)
    {
        var field = Find(id).Field;
        if (field.Message is { } message) message.Last("item_serial_number")!.Set(serial);
        else field.Set(serial);
    }

    internal void Invalidate()
    {
        _indexedRoot = null;
        _directRoot = null;
        _directEntries.Clear();
        _serials.Clear();
    }

    public void Remove(string id)
    {
        EnsureIdentities();
        var entry = Find(id);
        if (entry.List == Backpack)
        {
            foreach (var slot in _document.Root.Children("equipped_inventory_list"))
            {
                var index = slot.Integer("inventory_list_index");
                if (index == entry.Index) slot.SetInteger("inventory_list_index", -1);
                else if (index > entry.Index) slot.SetInteger("inventory_list_index", index - 1);
            }
        }
        _document.Root.Remove(entry.Field);
        _ids[entry.List].RemoveAt(entry.Index);
        RebuildLocations();
        _serials.Remove(id);
        _indexedRoot = null;
        _directRoot = null;
        _directEntries.Clear();
    }

    public string Append(string list, ProtoField source, string? movedId = null)
    {
        EnsureIdentities();
        EnsureCapacity(list, 1);
        ValidateDestination(list, source);
        if (movedId is not null && _ids.Values.SelectMany(ids => ids).Contains(movedId))
            throw new SaveFormatException("The destination already owns this item identity.");
        var root = _document.Root;
        var spec = root.Schema.Spec(root.Type, list);
        ProtoMessage? message = null;
        var serial = InventoryEntry.SerialOf(source).ToArray();
        if (root.Schema.IsMessage(spec.Type))
        {
            // TTWL shares the exact character/bank item message. Clone every unknown field.
            message = source.Message?.Clone() ?? new ProtoMessage(root.Schema, spec.Type, []);
            if (source.Message is null)
            {
                message.SetInteger("flags", 1); // Upstream's seen flag for imported items.
                var serialSpec = root.Schema.Spec(spec.Type, "item_serial_number");
                message.Add(new ProtoField(root.Schema.Tag(spec.Type, "item_serial_number"), 2, serialSpec, [], serial, null));
            }
            if (list == Backpack)
            {
                var pickup = root.Children(Backpack).Select(m => m.Integer("pickup_order_index")).DefaultIfEmpty(0).Max();
                if (pickup >= int.MaxValue) throw new SaveFormatException("Pickup order would overflow.");
                message.SetInteger("pickup_order_index", pickup + 1);
            }
        }
        root.Add(new ProtoField(root.Schema.Tag(root.Type, list), 2, spec, [], serial, message));
        var id = movedId ?? "item-" + _nextId++;
        _ids[list].Add(id);
        RebuildLocations();
        _indexedRoot = null;
        _directRoot = null;
        _directEntries.Clear();
        return id;
    }

    private void RebuildLocations()
    {
        _locations.Clear();
        foreach (var (list, ids) in _ids)
            for (var index = 0; index < ids.Count; index++)
                _locations[ids[index]] = (list, index);
    }

    private int CountBounded(string list)
    {
        var count = 0;
        foreach (var field in _document.Root.Fields)
        {
            if (++LastDirectFieldsVisited > DirectLookupWorkLimit)
            {
                LastDirectLookupExceeded = true;
                return -1;
            }
            if (field.Spec?.Name != list) continue;
            if (++LastDirectRecordsVisited > DirectLookupWorkLimit)
            {
                LastDirectLookupExceeded = true;
                return -1;
            }
            if (count++ >= DirectLookupWorkLimit)
            {
                LastDirectLookupExceeded = true;
                return -1;
            }
        }
        return count;
    }

    private static bool TryCanonicalItemId(string id, out int number)
    {
        number = -1;
        const string prefix = "item-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var token = id[prefix.Length..];
        if (token.Length == 0 || token.Length > 10 || token[0] == '0' && token.Length > 1) return false;
        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 0) return false;
        return string.Equals(id, prefix + number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    public void ValidateDestination(string list, ProtoField source)
    {
        var root = _document.Root;
        var spec = root.Schema.Spec(root.Type, list);
        if (!root.Schema.IsMessage(spec.Type) && source.Message is { } original && original.Fields.Any(f => f.Spec?.Name switch
        {
            "item_serial_number" or "pickup_order_index" => false,
            "flags" => f.Value is not ulong flags || (flags & ~7UL) != 0,
            "weapon_skin_path" => f.Value is not string text || text.Length != 0,
            _ => true,
        })) throw new SaveFormatException("This byte-only bank cannot retain the item's skin, development or unknown metadata.");
    }

    public void EnsureCapacity(string list, int additional)
    {
        EnsureIdentities();
        if (list == Backpack && additional > 0 && _document.Root.Children(Backpack)
            .Select(m => m.Integer("pickup_order_index")).DefaultIfEmpty(0).Max() > int.MaxValue - additional)
            throw new SaveFormatException("Destination pickup order would overflow.");
        var count = _ids[list].Count;
        var path = list == Backpack ? "Backpack" : list == Bank ? "Bank" : "LostLoot";
        var sdus = _document.Root.Children(_document.Profile ? "profile_sdu_list" : "sdu_list")
            .Where(m => m.Text("sdu_data_path").Contains("SDU_" + path + ".", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sdus.Length == 0 && !_document.Root.Children(_document.Profile ? "profile_sdu_list" : "sdu_list").Any())
        {
            var baseline = GearboxWritePolicies.Capacity(list, 0);
            if (count + additional <= baseline) return;
            throw new SaveFormatException($"No SDU record; destination growth is limited to the base-capacity policy ({baseline}).");
        }
        // Conservative editor policy from the pinned upstream SDU definitions; unknown paths reject growth.
        if (sdus.Length != 1) throw new SaveFormatException("Cannot establish the destination capacity from its existing SDU record.");
        var tier = sdus[0].Integer("sdu_level");
        var capacity = GearboxWritePolicies.Capacity(list, tier);
        var max = GearboxWritePolicies.SduMaximum(sdus[0].Text("sdu_data_path"), _document.Root.Schema.Wonderlands);
        if (tier < 0 || max is null || tier > max || count + additional > capacity)
            throw new SaveFormatException($"Destination capacity policy ({capacity}) would be exceeded; no items were discarded.");
    }

    private void EnsureIdentities()
    {
        if (_identitiesInitialized) return;
        foreach (var list in _ids.Keys.ToArray())
            _ids[list] = Enumerable.Range(0, _document.Root.All(list).Count())
                .Select(_ => "item-" + _nextId++).ToList();
        _identitiesInitialized = true;
        RebuildLocations();
    }
}

internal sealed record InventoryIdentity(Dictionary<string, string[]> Lists, int NextId, bool Initialized);
internal sealed record InventoryEntry(string Id, string List, int Index, ProtoField Field)
{
    public byte[] Serial => SerialOf(Field);
    public static byte[] SerialOf(ProtoField field) =>
        field.Message?.Last("item_serial_number")?.Value as byte[] ?? field.Value as byte[] ?? [];
}
