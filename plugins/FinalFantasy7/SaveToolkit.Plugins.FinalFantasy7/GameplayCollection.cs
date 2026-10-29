using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Queries native slots; only requested rows build dictionaries and bind fields.</summary>
internal sealed class GameplayCollection : ICollectionProjection
{
    private const string EmptyDescription = "Unused slot; original fields and bytes are preserved.";
    private readonly GameplayFields _gameplay;
    private readonly ReferenceData _references;
    private readonly Func<ScalarField, FieldNode> _bind;
    private readonly bool _materia;
    private readonly string _catalog;
    private readonly InMemoryCollection<int> _collection;

    public GameplayCollection(bool materia, GameplayFields gameplay, ReferenceData references, Func<ScalarField, FieldNode> bind)
    {
        _materia = materia;
        _gameplay = gameplay;
        _references = references;
        _bind = bind;
        _catalog = materia ? "ff7r-materia" : "ff7r-items";
        var id = materia ? "materia" : "inventory";
        var count = materia ? GameplayFields.MateriaCount : GameplayFields.InventoryCount;
        List<QueryableColumn> columns =
        [
            new("slot", "Slot", IntegerSpec.UInt32), new("name", "Name"), new("id", "Type ID"),
            new("category", "Category"), new("quantity", materia ? "AP" : "Quantity", IntegerSpec.Int32),
            new("description", "Description / source"), new("details", "Details / optional image"),
        ];
        if (materia) columns.Add(new("level", "Stored stars", IntegerSpec.UInt8));
        Metadata = new(id, materia ? "Materia" : "Inventory", columns, count)
        {
            EmptyRows = EmptySlots.Presentation,
            Description = "Empty slots are hidden by default; Show reveals every fixed slot without changing saved data. Existing mapped non-equipment quantities are experimental; counts do not update reward/acquisition flags. " + EmbeddedCatalogs.Attribution,
        };
        // Stable identities need no string formatting during query snapshots or tie-breaking.
        var keys = Enumerable.Range(0, count).Select(slot => $"{id}/{slot}").ToArray();
        _collection = new(Metadata, () => Enumerable.Range(0, count), slot => keys[slot],
            [.. columns.Select(Column)], Row, new()
            {
                ClampPaging = false,
                ReverseDefaultOrderWhenDescending = false,
                NormalizeSearchText = text => string.IsNullOrWhiteSpace(text) ? null : text,
                IsEmpty = slot => gameplay.IsEmpty(materia, slot),
            });
    }

    public QueryableCollection Metadata { get; }
    public QueryableTableNode View() => Metadata.ToTableNode();

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        CollectionQuery.Validate(Metadata, query);
        return _collection.Query(query);
    }

    private InMemoryColumn<int> Column(QueryableColumn column) => new(column.Key)
    {
        SearchText = slot => Text(slot, column.Key),
        FilterText = slot => Text(slot, column.Key),
        Comparer = column.Spec is IntegerSpec
            ? Comparer<int>.Create((left, right) => Number(left, column.Key).CompareTo(Number(right, column.Key)))
            : Comparer<int>.Create((left, right) => StringComparer.OrdinalIgnoreCase.Compare(Text(left, column.Key), Text(right, column.Key))),
    };

    private long Number(int slot, string column) => column switch
    {
        "slot" => slot,
        "quantity" => _materia ? _gameplay.Ap(slot) : _gameplay.Quantity(slot),
        "level" => _gameplay.Level(slot),
        _ => throw new ArgumentException("Unknown numeric slot column.", nameof(column)),
    };

    private string? Text(int slot, string column)
    {
        if (column is "slot" or "quantity" or "level") return Number(slot, column).ToString(CultureInfo.InvariantCulture);
        if (column == "details") return null; // Decorative cells have never participated in search/filtering.
        var type = _gameplay.TypeId(_materia, slot);
        var empty = _gameplay.IsEmpty(_materia, slot);
        var record = _references.Record(_catalog, type);
        return column switch
        {
            "name" => empty ? EmptySlots.Label : record?.Name ?? $"Unknown ID 0x{type:X}",
            "id" => $"0x{type:X8}",
            "category" => record?.Category ?? "Unknown / empty",
            "description" => empty ? EmptyDescription : _references.Describe(_materia, type),
            _ => null,
        };
    }

    private TableRow Row(int slot)
    {
        var row = _gameplay.Row(_materia, slot, _bind);
        var cells = row.Cells.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        cells["category"] = new TextNode(Text(slot, "category")!);
        cells["description"] = new TextNode(Text(slot, "description")!);
        var type = _gameplay.TypeId(_materia, slot);
        cells["details"] = row.IsEmpty ? cells["description"] :
            DocumentCollections.Details(_references.Record(_catalog, type), _references.Source(_catalog, type));
        return row with { Cells = cells };
    }
}
