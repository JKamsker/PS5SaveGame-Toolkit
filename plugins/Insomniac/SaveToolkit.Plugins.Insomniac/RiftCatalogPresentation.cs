using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal static class RiftCatalogPresentation
{
    internal const string ArtAttribution = "Artwork: Insomniac Games / Sony Interactive Entertainment, " +
        "via Ratchet & Clank Wiki contributors. Rights reserved; used with attribution per owner decision 2026-10-29.";

    internal static RiftCell Image(ReferenceRecord? record, string kind = "Weapon reference icon") =>
        record?.AssetId is { } assetId
            ? new(record.Name, () => new ImageNode(assetId, $"{kind}: {record.Name}. {ArtAttribution}"))
            : RiftCell.TextOnly("No confirmed reference image");

    internal static QueryableTableNode? Upgrades(IReferenceCatalog? catalog, RiftCollections collections)
    {
        if (catalog is null) return null;
        var records = new List<ReferenceRecord>();
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
            records.AddRange(catalog.Search(new() { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items);
        return collections.Add("arsenal/reference-upgrades", "Upgraded weapon reference forms (not saved level)",
            [new("icon", "Reference preview"), new("name", "Reference form"), new("description", "Description and source")],
            records.Select(record => new RiftRow(record.Key, new Dictionary<string, RiftCell>
            {
                ["icon"] = Image(record, "Upgraded weapon reference preview"),
                ["name"] = RiftCell.TextOnly(record.Name),
                ["description"] = RiftCell.TextOnly(record.Description + " " + record.AttributionText),
            })), "Reference catalogue only. Neither illustrations nor descriptions establish this save's weapon level, " +
                "ownership, upgrade purchases or grid-node identities. " + ArtAttribution);
    }
}
