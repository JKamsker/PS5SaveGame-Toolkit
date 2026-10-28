using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record ScalarField(string Id, string Label, int Offset, IntegerSpec Spec, Confidence Confidence,
    bool Signed = false, bool Byte = false, string? Warning = null, bool Editable = false);

/// <summary>Absolute offsets independently checked against the public PC schemas and every supplied PS5 slot.</summary>
internal sealed class GameplayFields(byte[] bytes, Ff7Schema schema, ReferenceData references)
{
    private readonly MateriaEditPolicy _materiaPolicy = new(bytes, schema);
    private readonly QuantityEditPolicy _quantityPolicy = new(bytes, schema);
    public const int InventoryCount = 2048;
    public const int MateriaCount = 1000;
    public const int RowsPerPage = 128;
    private readonly GameplayLayout _layout = GameplayLayout.For(schema);
    private const int RemakeChapterOffset = 0x2E;
    private const string DemoWarning = "PC retail field mapping is inferred for the PS5 demo; no in-game confirmation.";
    private const string EditWarning = "Experimental in-place edit; keep a backup and do not rely on this until a console load is tested.";

    public int InventoryOffset => _layout.InventoryOffset;
    public int InventoryStride => _layout.InventoryStride;
    public int PlayTimeOffset => _layout.PlayTimeOffset;
    public Confidence MeaningConfidence => schema.IsDemo ? Confidence.Inferred : Confidence.Verified;

    public uint TypeId(bool materia, int slot) => U32(IdOffset(materia, slot));
    public uint Ap(int slot) => U32(IdOffset(true, slot) + GameplayLayout.MateriaApDelta);
    public int Level(int slot) => bytes[_layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride];
    public int Quantity(int slot) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(IdOffset(false, slot) + GameplayLayout.QuantityDelta));
    public IntegerSpec? ApRange(int slot) => _materiaPolicy.Range(slot);

    public IReadOnlyList<ViewNode> Summary(Func<ScalarField, FieldNode> bind)
    {
        var nodes = new List<ViewNode>
        {
            bind(Define("game/playtime", "Play time", PlayTimeOffset,
                IntegerSpec.UInt32 with { Format = IntegerFormat.Duration, UnitsPerSecond = 1 })),
        };
        var gilId = _layout.GilType;
        var gilSlots = Enumerable.Range(0, InventoryCount).Where(i => U32(InventoryOffset + i * InventoryStride) == gilId).ToArray();
        if (gilSlots.Length == 1)
        {
            nodes.Add(bind(Define("game/gil", "Gil", InventoryOffset + gilSlots[0] * InventoryStride + GameplayLayout.QuantityDelta,
                _quantityPolicy.Range(gilSlots[0]) ?? IntegerSpec.Int32, signed: true,
                editable: _quantityPolicy.Range(gilSlots[0]) is not null, warning: $"{EditWarning} Limits are editor policy, not proven game caps.")));
        }
        else
        {
            nodes.Add(new TextNode($"Gil entry count is {gilSlots.Length}; inspect the inventory instead of assuming a balance.") { Tone = Tone.Warning });
        }

        if (schema.IsRemake)
        {
            nodes.Add(bind(Define("game/chapter-index", "Current chapter ID (zero based)", RemakeChapterOffset,
                IntegerSpec.UInt8, singleByte: true)));
        }

        return nodes;
    }

    public IReadOnlyList<ViewNode> Pages(bool materia)
    {
        var count = materia ? MateriaCount : InventoryCount;
        var category = materia ? "materia" : "inventory";
        return [.. Enumerable.Range(0, (count + RowsPerPage - 1) / RowsPerPage).Select(page =>
            SectionNode.Lazy($"Slots {page * RowsPerPage}-{Math.Min(count, (page + 1) * RowsPerPage) - 1}",
                $"{category}/{page}"))];
    }

    public TableNode Page(bool materia, int page, Func<ScalarField, FieldNode> bind)
    {
        var count = materia ? MateriaCount : InventoryCount;
        if (page < 0 || page >= (count + RowsPerPage - 1) / RowsPerPage)
        {
            throw new KeyNotFoundException("Unknown gameplay page.");
        }

        var rows = new List<TableRow>();
        for (var i = page * RowsPerPage; i < Math.Min(count, (page + 1) * RowsPerPage); i++)
        {
            rows.Add(Row(materia, i, bind));
        }

        List<TableColumn> columns = [new("slot", "Slot"), new("name", "Name"), new("id", "ID"), new("quantity", materia ? "AP" : "Quantity")];
        if (materia)
        {
            columns.Add(new("level", "Level"));
        }

        return new TableNode(columns, rows)
        {
            Title = materia ? "Materia" : "Inventory",
            Description = (schema.IsDemo ? DemoWarning : "Empty slots and unknown IDs are retained.") + " " + EmbeddedCatalogs.Attribution,
        };
    }

    public TableRow Row(bool materia, int i, Func<ScalarField, FieldNode> bind)
    {
        var idOffset = IdOffset(materia, i);
        var id = U32(idOffset);
        var key = $"{(materia ? "materia" : "inventory")}/{i}";
        var cells = new Dictionary<string, ViewNode>(StringComparer.Ordinal)
        {
            ["slot"] = new TextNode(i.ToString()),
            ["name"] = new TextNode(id is 0 or uint.MaxValue ? "Empty" : materia ? references.Materia(id) : references.Item(id)),
            ["id"] = bind(Define($"{key}/id", "Item ID", idOffset, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex })),
        };
        if (materia)
        {
            var apOffset = _layout.MateriaApOffset + i * GameplayLayout.MateriaStride;
            var levelOffset = _layout.MateriaLevelOffset + i * GameplayLayout.MateriaStride;
            var range = _materiaPolicy.Range(i);
            var editable = range is not null;
            cells["quantity"] = bind(Define($"{key}/ap", "AP", apOffset,
                range ?? IntegerSpec.UInt32, editable: editable,
                warning: editable ? $"{EditWarning} AP is restricted to the original star tier; star transitions are disabled."
                    : "Write disabled: empty, unknown, summon or AP/level mismatch. The original bytes are retained."));
            cells["level"] = bind(Define($"{key}/level", "Level", levelOffset, IntegerSpec.UInt8, singleByte: true));
        }
        else
        {
            var isGil = id == (_layout.GilType);
            var range = _quantityPolicy.Range(i);
            var editable = range is not null;
            cells["quantity"] = bind(Define($"{key}/quantity", "Quantity", idOffset + GameplayLayout.QuantityDelta,
                range ?? IntegerSpec.Int32, signed: true, editable: editable,
                warning: ItemWarning(id, editable, isGil)));
        }

        return new TableRow(key, cells);
    }

    private ScalarField Define(string id, string label, int offset, IntegerSpec spec, bool signed = false,
        bool singleByte = false, bool editable = false, string? warning = null) =>
        new(id, label, offset, spec, MeaningConfidence, signed, singleByte,
            JoinWarnings(schema.IsDemo ? DemoWarning : null, warning), editable && !schema.IsDemo);

    private static string? JoinWarnings(string? first, string? second) => first is null ? second : second is null ? first : $"{first} {second}";

    private string ItemWarning(uint id, bool editable, bool gil) => !editable
        ? $"Write disabled: {(id == 0 || id == uint.MaxValue ? "empty" : "unmapped, equipment or progression")} row; raw value is retained."
        : $"{EditWarning} {(gil ? "Gil" : "item quantity")} cap is an editor bound, not a proven game limit.";

    private int IdOffset(bool materia, int slot) => materia
        ? _layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride
        : InventoryOffset + slot * InventoryStride;
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
