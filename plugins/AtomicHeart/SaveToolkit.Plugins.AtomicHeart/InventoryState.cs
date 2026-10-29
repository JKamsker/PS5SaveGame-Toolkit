using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed record InventoryItem(string Key, string Container, TaggedProperty Element,
    string RawAsset, long InstanceId, long Amount, TaggedProperty Asset, TaggedProperty Quantity, string ContainerId);

/// <summary>Existing slot values only. No allocation, identity, equipment or ownership changes.</summary>
internal sealed class InventoryState
{
    private const string ReferenceOnlyAttribute = "referenceOnly";
    private const string EmptyAssetPath = "None";
    private const long EmptyAmount = 0;
    private const long DefaultEmptyItemId = 0;
    private const long SentinelEmptyItemId = -1;
    internal const string WriteWarning = "Not validated on console. The game may reject or overwrite the edited save. Stored Kraken block acceptance and inner integrity remain unverified. Gameplay effects, ownership and safe limits are unverified; validate on a disposable copy (issue #31).";
    private readonly Dictionary<int, FieldValue> values = [];
    private readonly Dictionary<string, string> savedAssets = new(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguousAssets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> containerLimits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> itemOrdinals = new(StringComparer.Ordinal);
    internal IReadOnlyList<InventoryItem> Items { get; }
    internal IReferenceCatalog? Catalog { get; }

    internal InventoryState(RecordIndex index, IReferenceCatalog? catalog)
    {
        Catalog = catalog;
        var items = new List<InventoryItem>();
        foreach (var record in index.Objects.Where(r => r.Kind == RecordKinds.Component &&
            (r.ClassPath.EndsWith(InventoryClasses.Player, StringComparison.Ordinal) ||
             r.ClassPath.EndsWith(InventoryClasses.Storage, StringComparison.Ordinal))))
        foreach (var group in record.Properties.GroupBy(p => p.Name, StringComparer.Ordinal))
        {
            // Repeated container tags or collection tags have no proven authoritative occurrence.
            // Keep all of them in the raw inspector rather than choosing or merging write targets.
            if (group.Count() != 1) continue;
            var property = group.Single();
            var collections = property.Children.Where(p => p.Name is "Slots" or "Elements").Take(2).ToArray();
            if (collections.Length != 1) continue;
            var entries = collections[0];
            var container = record.Id + "/" + property.Name;
            ReadContainer(items, record, property.Name, container, entries);
        }
        Items = items;
    }

    private void ReadContainer(List<InventoryItem> items, ObjectRecord record, string propertyName,
        string container, TaggedProperty entries)
    {
        foreach (var element in entries.Children)
        {
            // Ambiguous duplicate tags stay in the raw inspector; never pick one to overwrite.
            var asset = Unique(element, "ItemDataAsset");
            var quantity = Unique(element, "Amount");
            var instance = Unique(element, "ItemId");
            if (asset?.Value is not { Kind: FieldValueKind.Text } text ||
                quantity?.Value is not { Kind: FieldValueKind.Integer } amount ||
                instance?.Value is not { Kind: FieldValueKind.Integer } instanceId) continue;
            var storedAmount = ReadInteger(amount, quantity.Name);
            var storedInstanceId = ReadInteger(instanceId, instance.Name);
            var key = $"{record.Id}/{propertyName}/item/{storedInstanceId}";
            if (!itemOrdinals.TryAdd(key, items.Count))
            {
                key += $"/slot/{element.Name}";
                if (!itemOrdinals.TryAdd(key, items.Count)) throw new SaveFormatException("Duplicate inventory slot identity.");
            }
            var item = new InventoryItem(key, record.Name + "/" + propertyName, element,
                text.AsString(), storedInstanceId, storedAmount, asset, quantity, container);
            items.Add(item);
            values[asset.Payload.Offset] = text;
            values[quantity.Payload.Offset] = amount;
            if (Populated(item))
            {
                containerLimits[container] = Math.Max(containerLimits.GetValueOrDefault(container), item.Amount);
                var id = ShortId(item.RawAsset);
                if (savedAssets.TryGetValue(id, out var existing) && existing != item.RawAsset) ambiguousAssets.Add(id);
                else savedAssets.TryAdd(id, item.RawAsset);
            }
        }
    }

    internal bool TryGetOrdinal(string key, out int ordinal) => itemOrdinals.TryGetValue(key, out ordinal);

    private static long ReadInteger(FieldValue value, string name)
    {
        var integer = value.AsInteger();
        if (integer < long.MinValue || integer > long.MaxValue)
            throw new SaveFormatException($"Inventory {name} is outside the supported signed 64-bit range.");
        return (long)integer;
    }

    private static TaggedProperty? Unique(TaggedProperty element, string name)
    {
        var matches = element.Children.Where(p => p.Name == name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static string ShortId(string path) => path[(path.LastIndexOf('.') + 1)..];
    internal static bool Populated(InventoryItem item) => item.RawAsset is not ("" or "None") && item.Amount > 0;
    internal bool CanSetAmount(InventoryItem item) => Populated(item) && item.Quantity.Type is "IntProperty" or "Int64Property";
    internal long Limit(InventoryItem item) => Math.Min(containerLimits.GetValueOrDefault(item.ContainerId),
        item.Quantity.Type == "IntProperty" ? int.MaxValue : long.MaxValue);
    internal FieldValue Current(TaggedProperty property) => values.GetValueOrDefault(property.Payload.Offset, property.Value!.Value);
    internal void Set(TaggedProperty property, FieldValue value) => values[property.Payload.Offset] = value;
    internal string Asset(InventoryItem item) => Current(item.Asset).AsString();
    // Both sentinel identities occur with None/zero in the supplied slot arrays.
    // Unknown assets, blank paths and zero amounts alone are not proven vacancies.
    internal bool IsEmpty(InventoryItem item) => Asset(item) == EmptyAssetPath &&
        Current(item.Quantity).AsInt64() == EmptyAmount && item.InstanceId is DefaultEmptyItemId or SentinelEmptyItemId;
    internal bool CanReplace(InventoryItem item) => Populated(item) && Eligibility(item, ShortId(item.RawAsset)).Eligible;

    internal string? ValidateAmount(InventoryItem item, FieldValue value) => value.Kind != FieldValueKind.Integer ||
        value.AsInteger() < 0 || value.AsInteger() > Limit(item)
        ? $"Amount must be an integer from 0 to {Limit(item)} (observed container maximum, not a proven game cap)." : null;

    internal ReferenceEligibility Eligibility(InventoryItem item, string id)
    {
        var original = Catalog?.Find(ShortId(item.RawAsset));
        var candidate = Catalog?.Find(id);
        var eligible = original?.Category is not null && candidate is not null &&
            !original.Attributes.ContainsKey(ReferenceOnlyAttribute) && !candidate.Attributes.ContainsKey(ReferenceOnlyAttribute) &&
            candidate.Category == original.Category &&
            savedAssets.ContainsKey(id) && !ambiguousAssets.Contains(id);
        return new ReferenceEligibility { Eligible = eligible,
            Reasons = eligible ? [] : ["Choose a same-category catalog asset whose complete object path already occurs in this save. No path, DLC availability or dependent item state is invented."] };
    }

    internal string? ValidateAsset(InventoryItem item, FieldValue value) => value.Kind != FieldValueKind.Text ||
        !Eligibility(item, value.AsString()).Eligible ? "Unsupported replacement asset for this existing slot." : null;
    internal void SetAsset(InventoryItem item, FieldValue value) => Set(item.Asset, FieldValue.Of(savedAssets[value.AsString()]));

    internal IReadOnlyDictionary<TaggedProperty, FieldValue> Edits() => Items
        .SelectMany(item => new[] { item.Asset, item.Quantity }).DistinctBy(p => p.Payload.Offset)
        .Where(p => Current(p) != p.Value).ToDictionary(p => p, Current);
}
