using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record ScalarField(string Id, string Label, int Offset, IntegerSpec Spec, Confidence Confidence,
    bool Signed = false, bool Byte = false, string? Warning = null);

/// <summary>Absolute offsets independently checked against the public PC schemas and every supplied PS5 slot.</summary>
internal sealed class GameplayFields(byte[] bytes, Ff7Schema schema)
{
    public const int InventoryCount = 2048;
    public const int MateriaCount = 1000;
    public const int RowsPerPage = 128;
    private const int MateriaStride = 32;
    private const int QuantityOffset = 4;
    private const int RemakeChapterOffset = 0x2E;
    private const int RemakeInventoryOffset = 0x34DD8;
    private const int RebirthInventoryOffset = 0x4FA70;
    private const int RemakePlayTimeOffset = 0x2B0;
    private const int RebirthPlayTimeOffset = 0x3C30;
    private const int RemakeMateriaIdOffset = 0x1844;
    private const int RebirthMateriaIdOffset = 0xBFA6C;
    private const int RemakeMateriaApOffset = 0x1848;
    private const int RebirthMateriaApOffset = 0xBFA70;
    private const int RemakeMateriaLevelOffset = 0x1840;
    private const int RebirthMateriaLevelOffset = 0xBFA78;
    private const int RemakeInventoryStride = 24;
    private const int RebirthInventoryStride = 32;
    private const uint RemakeGilId = 20;
    private const uint RebirthGilId = 1;
    private const string DemoWarning = "PC retail field mapping is inferred for the PS5 demo; no in-game confirmation.";

    public int InventoryOffset => schema.IsRemake ? RemakeInventoryOffset : RebirthInventoryOffset;
    public int InventoryStride => schema.IsRemake ? RemakeInventoryStride : RebirthInventoryStride;
    public int PlayTimeOffset => schema.IsRemake ? RemakePlayTimeOffset : RebirthPlayTimeOffset;
    public Confidence MeaningConfidence => schema.IsDemo ? Confidence.Inferred : Confidence.Verified;

    public IReadOnlyList<ViewNode> Summary(Func<ScalarField, FieldNode> bind)
    {
        var nodes = new List<ViewNode>
        {
            bind(Define("game/playtime", "Play time", PlayTimeOffset,
                IntegerSpec.UInt32 with { Format = IntegerFormat.Duration, UnitsPerSecond = 1 })),
        };
        var gilId = schema.IsRemake ? RemakeGilId : RebirthGilId;
        var gilSlots = Enumerable.Range(0, InventoryCount).Where(i => U32(InventoryOffset + i * InventoryStride) == gilId).ToArray();
        if (gilSlots.Length == 1)
        {
            nodes.Add(bind(Define("game/gil", "Gil", InventoryOffset + gilSlots[0] * InventoryStride + QuantityOffset,
                IntegerSpec.Int32, signed: true)));
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
            SectionNode.Lazy($"Slots {page * RowsPerPage}–{Math.Min(count, (page + 1) * RowsPerPage) - 1}",
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
            var idOffset = materia ? (schema.IsRemake ? RemakeMateriaIdOffset : RebirthMateriaIdOffset) + i * MateriaStride
                : InventoryOffset + i * InventoryStride;
            var id = U32(idOffset);
            var key = $"{(materia ? "materia" : "inventory")}/{i}";
            var cells = new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["slot"] = new TextNode(i.ToString()),
                ["name"] = new TextNode(id == 0 ? "Empty" : ItemNames.Lookup(schema, materia, id)),
                ["id"] = bind(Define($"{key}/id", "Item ID", idOffset, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex })),
            };
            if (materia)
            {
                var apOffset = (schema.IsRemake ? RemakeMateriaApOffset : RebirthMateriaApOffset) + i * MateriaStride;
                var levelOffset = (schema.IsRemake ? RemakeMateriaLevelOffset : RebirthMateriaLevelOffset) + i * MateriaStride;
                cells["quantity"] = bind(Define($"{key}/ap", "AP", apOffset, IntegerSpec.UInt32));
                cells["level"] = bind(Define($"{key}/level", "Level", levelOffset, IntegerSpec.UInt8, singleByte: true));
            }
            else
            {
                cells["quantity"] = bind(Define($"{key}/quantity", "Quantity", idOffset + QuantityOffset, IntegerSpec.Int32, signed: true));
            }

            rows.Add(new TableRow(key, cells));
        }

        List<TableColumn> columns = [new("slot", "Slot"), new("name", "Name"), new("id", "ID"), new("quantity", materia ? "AP" : "Quantity")];
        if (materia)
        {
            columns.Add(new("level", "Level"));
        }

        return new TableNode(columns, rows)
        {
            Title = materia ? "Materia" : "Inventory",
            Description = schema.IsDemo ? DemoWarning : "Names and fields follow the cited game-specific schemas; unknown IDs remain raw. Empty slots are retained.",
        };
    }

    private ScalarField Define(string id, string label, int offset, IntegerSpec spec, bool signed = false, bool singleByte = false) =>
        new(id, label, offset, spec, MeaningConfidence, signed, singleByte, schema.IsDemo ? DemoWarning : null);

    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
