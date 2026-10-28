using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed record InventoryItem(string Key, string Container, TaggedProperty Element,
    string RawAsset, long InstanceId, long Amount, TaggedProperty Asset, TaggedProperty Quantity, string ContainerId);

/// <summary>Existing slot values only. No allocation, identity, equipment or ownership changes.</summary>
internal sealed class InventoryState
{
    internal const string WriteWarning = "Not validated on console. Stored Kraken blocks and unknown inner integrity mean the game may reject the edited save. Gameplay effects, ownership and safe limits are unverified; validate on a disposable copy (issue #31).";
    private readonly Dictionary<int, FieldValue> values = [];
    private readonly Dictionary<string, string> savedAssets = new(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguousAssets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> containerLimits = new(StringComparer.Ordinal);
    internal IReadOnlyList<InventoryItem> Items { get; }
    internal IReferenceCatalog? Catalog { get; }

    internal InventoryState(RecordIndex index, IReferenceCatalog? catalog)
    {
        Catalog = catalog;
        var items = new List<InventoryItem>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in index.Objects.Where(r => r.Kind == RecordKinds.Component &&
            (r.ClassPath.EndsWith(InventoryClasses.Player, StringComparison.Ordinal) ||
             r.ClassPath.EndsWith(InventoryClasses.Storage, StringComparison.Ordinal))))
        foreach (var property in record.Properties)
        {
            var entries = property.Children.FirstOrDefault(p => p.Name is "Slots" or "Elements");
            if (entries is null) continue;
            var container = record.Id + "/" + property.Name;
            foreach (var element in entries.Children)
            {
                // Ambiguous duplicate tags stay in the raw inspector; never pick one to overwrite.
                var asset = Unique(element, "ItemDataAsset");
                var quantity = Unique(element, "Amount");
                var instance = Unique(element, "ItemId");
                if (asset?.Value is not { Kind: FieldValueKind.Text } text ||
                    quantity?.Value is not { Kind: FieldValueKind.Integer } amount ||
                    instance?.Value is not { Kind: FieldValueKind.Integer } instanceId) continue;
                var key = $"{record.Id}/{property.Name}/item/{instanceId.AsInt64()}";
                if (!keys.Add(key))
                {
                    key += $"/slot/{element.Name}";
                    if (!keys.Add(key)) throw new SaveFormatException("Duplicate inventory slot identity.");
                }
                var item = new InventoryItem(key, record.Name + "/" + property.Name, element,
                    text.AsString(), instanceId.AsInt64(), amount.AsInt64(), asset, quantity, container);
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
        Items = items;
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
    internal bool CanReplace(InventoryItem item) => Populated(item) && Eligibility(item, ShortId(item.RawAsset)).Eligible;

    internal string? ValidateAmount(InventoryItem item, FieldValue value) => value.Kind != FieldValueKind.Integer ||
        value.AsInteger() < 0 || value.AsInteger() > Limit(item)
        ? $"Amount must be an integer from 0 to {Limit(item)} (observed container maximum, not a proven game cap)." : null;

    internal ReferenceEligibility Eligibility(InventoryItem item, string id)
    {
        var original = Catalog?.Find(ShortId(item.RawAsset));
        var candidate = Catalog?.Find(id);
        var eligible = original?.Category is not null && candidate is not null && candidate.Category == original.Category &&
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
