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
    private readonly GearboxInventoryIdentities _identity;
    private ProtoMessage? _indexedRoot;
    private ProtoMessage? _directRoot;
    private InventoryEntry[] _entries = [];
    private Dictionary<string, InventoryEntry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InventoryEntry> _directEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] Bytes, SerialInfo Info)> _serials = new(StringComparer.Ordinal);

    internal long DirectFieldsVisited { get; private set; }
    internal int LastDirectFieldsVisited { get; private set; }
    internal int LastDirectRecordsVisited { get; private set; }
    internal bool LastDirectLookupExceeded { get; private set; }

    public GearboxInventory(GearboxDocument document)
    {
        _document = document;
        _identity = new(document);
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
        // A scalar can remove another root tag between setters. The model maintains each tag's
        // own ordinals during that mutation, so a category cannot shift an inventory identity.
        var located = _identity.TryLocation(id, out var location);
        if (!located && !_identity.Initialized && _byId.TryGetValue(id, out var opening))
        {
            // Warm opening rows remain directly addressable across scalar root clones without
            // turning a read cache into persistent identity allocation.
            location = (opening.List, opening.Index);
            located = true;
        }
        if (located)
        {
            var field = _document.Root.FieldAt(location.List, location.Index);
            if (field is null) return false;
            LastDirectFieldsVisited++;
            DirectFieldsVisited++;
            if (field.Spec?.Name != location.List) return false;
            LastDirectRecordsVisited++;
            entry = new InventoryEntry(id, location.List, location.Index, field);
            _directEntries[id] = entry;
            return true;
        }
        if (!TryCanonicalItemId(id, out var number))
            return false;
        // Cold targeted lookup uses the stable numeric identity directly. Full identity lists are
        // reserved only by structural mutations; this keeps one unloaded request bounded.
        if (!_identity.Initialized && !_document.Profile && number < DirectLookupWorkLimit)
            return TryEntry(Backpack, number, out entry, id);
        if (!_identity.Initialized && _document.Profile && number < DirectLookupWorkLimit)
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
            _document.Root.CountTargetedVisit();
            DirectFieldsVisited++;
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
        entry = new InventoryEntry(identity ?? _identity.At(list, index), list, index, field);
        _directEntries[entry.Id] = entry;
        _directRoot = _document.Root;
        return true;
    }

    private void Index()
    {
        if (ReferenceEquals(_indexedRoot, _document.Root)) return;
        // Cold views/plans use positional opening identities without reserving them. Only a
        // committed structural mutation materializes persistent lists and advances the counter.
        var next = _identity.NextId;
        _entries = _identity.Lists.SelectMany(list => _document.Root.All(list)
            .Select((record, index) => new InventoryEntry(_identity.Initialized ? _identity.At(list, index)
                : "item-" + (next++).ToString(CultureInfo.InvariantCulture), list, index, record))).ToArray();
        _byId = _entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        _directEntries.Clear();
        foreach (var entry in _entries) _directEntries[entry.Id] = entry;
        _indexedRoot = _document.Root;
        _directRoot = _document.Root;
    }
    public InventoryIdentity Capture() => _identity.Capture();
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
        _identity.Restore(identity);
        _byId.Clear();
        _entries = [];
        Invalidate();
        // Restore is an explicit full-state boundary. Rebuild transient locations so already
        // established late rows bind to the restored model without reserving persistent IDs.
        Index();
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
        _identity.Initialize();
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
        _identity.Remove(entry.List, entry.Index);
        _serials.Remove(id);
        _indexedRoot = null;
        _directRoot = null;
        _directEntries.Clear();
    }

    public string Append(string list, ProtoField source, string? movedId = null)
    {
        EnsureCapacity(list, 1);
        ValidateDestination(list, source);
        ValidateMovedIdentity(movedId);
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
        _identity.Initialize();
        root.Add(new ProtoField(root.Schema.Tag(root.Type, list), 2, spec, [], serial, message));
        var id = _identity.Append(list, movedId);
        _indexedRoot = null;
        _directRoot = null;
        _directEntries.Clear();
        return id;
    }

    private int CountBounded(string list)
    {
        var count = 0;
        foreach (var field in _document.Root.Fields)
        {
            _document.Root.CountTargetedVisit();
            DirectFieldsVisited++;
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
        ValidateList(list);
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
        ValidateList(list);
        GearboxInventoryCapacity.Ensure(_document, list, additional);
    }

    internal void ValidateMovedIdentity(string? movedId)
    {
        if (movedId is not null && Entries.Any(entry => entry.Id == movedId))
            throw new SaveFormatException("The destination already owns this item identity.");
    }

    private void ValidateList(string list)
    {
        if (!_identity.Owns(list)) throw new SaveFormatException("The destination container does not belong to this document.");
    }
}

internal sealed record InventoryEntry(string Id, string List, int Index, ProtoField Field)
{
    public byte[] Serial => SerialOf(Field);
    public static byte[] SerialOf(ProtoField field) =>
        field.Message?.Last("item_serial_number")?.Value as byte[] ?? field.Value as byte[] ?? [];
}
