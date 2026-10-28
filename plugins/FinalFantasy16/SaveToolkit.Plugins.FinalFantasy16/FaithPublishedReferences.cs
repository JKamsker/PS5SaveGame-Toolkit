using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithPublishedReferences(FaithCatalog catalog)
{
    public IReadOnlyList<ViewNode> Build(FaithContextField field) =>
    [
        new TextNode("Published names include story and DLC spoilers. Browse the reference pickers for names, " +
            "categories and source attribution. These are reference IDs, not saved quest flags, NPC instances or " +
            "proof of game-build availability. No save data is changed."),
        Reference(field, FaithCatalog.PublishedMapsId, "Published map identifiers — variants grouped"),
        Reference(field, FaithCatalog.BattleNpcsId, "Published battle NPC identifiers — partial speaker-linked list"),
        Reference(field, FaithCatalog.EventNpcsId, "Published event NPC identifiers — partial speaker-linked list"),
    ];

    private ViewNode Reference(FaithContextField field, string id, string label)
    {
        var record = catalog.Search(id, new ReferenceQuery { SortBy = ReferenceSortKey.Key, Limit = 1 }).FirstOrDefault();
        return record is null ? new TextNode($"{label}: no reference dataset is available.")
            : field($"references/{id}", label, new ReferenceSpec(id), () => FieldValue.Of(record.Key),
                Confidence.Inferred, FaithCatalog.Describe(record));
    }
}
