using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Name overrides must explicitly match the pinned binary database, not just the title.</summary>
internal sealed class ItemReferences(bool wonderlands, IReferenceContext context)
{
    public SerialDatabase Database { get; } = SerialDatabase.For(wonderlands);
    private readonly IReferenceCatalog? _balances = context.Catalog(ItemCatalogs.Balances);
    private readonly IReferenceCatalog? _indices = context.Catalog(ItemCatalogs.Indices);
    private readonly IReferenceCatalog? _cards = context.Catalog(ItemCatalogs.LocalizedCards);
    private readonly IReferenceCatalog? _officialCards = context.Catalog(ItemCatalogs.ItemCards);
    private readonly IReferenceCatalog? _parts = context.Catalog(ItemCatalogs.PartLabels);

    public ReferenceRecord? Index(string id) => _indices?.Find(id) ?? Database.Find(ItemCatalogs.Indices, id);

    public ReferenceRecord? Balance(string? path)
    {
        if (path is null) return null;
        var id = path.ToLowerInvariant();
        var candidate = _balances?.Find(id);
        if (candidate?.Attributes.GetValueOrDefault("databaseVersion") == Database.MaximumVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            return candidate;
        return Database.Find(ItemCatalogs.Balances, id);
    }

    public string Label(ReferenceRecord? record, string? raw)
    {
        var card = record is null ? null : FindCard(record.Key);
        return card?.Name ?? (record?.Attributes.GetValueOrDefault("displayKind") == "family" ? record.Name : raw ?? "Unknown item");
    }

    public string? CardDescription(ReferenceRecord? record) => record is not null && FindCard(record.Key) is { } card
        ? card.Description + " " + card.AttributionText : null;

    public ViewNode Image(ReferenceRecord? record, bool part = false) => GearboxImages.Cell(context,
        record is null || part ? record : FindCard(record.Key) ?? record, part);

    public ReferenceRecord? Part(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var key = path.ToLowerInvariant().Split('.').Last();
        return _parts?.Find(key) ?? Database.TryFind(ItemCatalogs.PartLabels, key);
    }

    private ReferenceRecord? FindCard(string id) => _officialCards?.Find(id) ?? Database.TryFind(ItemCatalogs.ItemCards, id)
        ?? _cards?.Find(id) ?? Database.TryFind(ItemCatalogs.LocalizedCards, id);
}
