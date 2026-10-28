using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed class SuckerPunchDocument : BoundSaveDocument
{
    private const string ChecksumId = "container/word2";
    private const string InventoryBlockId = "blocks/inventory";
    private const string ProgressBlockId = "blocks/progress";
    private readonly ISaveFile _file;
    private readonly byte[] _bytes;
    private readonly SaveContainer _container;
    private readonly TsushimaInventory? _inventory;
    private readonly TsushimaProgress? _progress;
    private readonly string? _subtitle;

    private SuckerPunchDocument(ISaveFile file, byte[] bytes, SaveContainer container)
    {
        _file = file;
        _bytes = bytes;
        _container = container;
        _subtitle = file.Directory.ParamSfo?.SubTitle;
        if (container.Kind == ContainerKind.Tsushima)
        {
            var content = bytes.AsSpan(0, (int)container.Word1);
            _inventory = TsushimaInventory.Parse(content);
            _progress = TsushimaProgress.Parse(content, container.Word3);
        }
    }

    public override string Title => string.IsNullOrWhiteSpace(_subtitle)
        ? $"{_file.Directory.TitleId} · {_file.FileName}"
        : $"{_file.Directory.TitleId} · {_subtitle}";

    public override bool CanEdit => _inventory is not null;

    public static SuckerPunchDocument Open(ISaveFile file)
    {
        var bytes = file.ReadAllBytes();
        return new SuckerPunchDocument(file, bytes, SaveContainer.Parse(bytes, file.Directory.TitleId));
    }

    public override ViewNode BuildView()
    {
        var isTsushima = _container.Kind == ContainerKind.Tsushima;
        var format = _container.Kind switch
        {
            ContainerKind.Tsushima => $"Tsushima container v{_container.Word3}",
            ContainerKind.YoteiEmpty => "Yōtei empty placeholder",
            _ => "Yōtei container (unmapped)",
        };
        var overview = new List<ViewNode>
        {
            new StatsNode(
            [
                new Stat("Title", _file.Directory.TitleId),
                new Stat("File", _file.PathInDirectory),
                new Stat("Size", $"{_bytes.Length:N0} bytes"),
                new Stat("Format", format),
                new Stat("Checksum", _container.ChecksumValid ? "Verified" : "Unknown"),
            ]),
            new SectionNode("Container header", BuildHeaderFields(isTsushima)),
        };
        if (!string.IsNullOrWhiteSpace(_subtitle))
        {
            overview.Add(new TextNode($"param.sfo SUBTITLE: {_subtitle}") { Tone = Tone.Info });
        }

        if (_progress is { } progress)
        {
            overview.Add(Field("progress/difficulty", "Difficulty", new TextSpec(),
                () => FieldValue.Of(progress.Difficulty)) with
            {
                Confidence = Confidence.Inferred,
                Description = "Length-prefixed name before the difficulty timer table; matches all slot subtitles.",
            });
            if (progress.PlaySeconds is { } seconds)
            {
                overview.Add(Field("progress/play-seconds", "Play time", FloatSpec.Single with { Min = 0, Unit = "seconds" },
                    () => FieldValue.Of(seconds)) with
                {
                    Confidence = Confidence.Inferred,
                    Description = "Float32 at difficulty-table + 231; its whole minutes match every v48/v49 slot subtitle.",
                });
            }
        }

        var tabs = new List<Tab> { new("Overview", overview) };
        if (_inventory is not null)
        {
            tabs.Add(new Tab("Inventory", [new TextNode(
                "Experimental edits: only existing unlocked records can change. Ammo updates both count copies. " +
                "The game may clamp quantities to its capacity limits.") { Tone = Tone.Warning }, BuildInventory()]));
            tabs.Add(new Tab("Blocks", BuildBlocks()));
        }

        tabs.Add(new Tab("Raw bytes", [new HexNode("content", _bytes.Length) { Title = "Complete save bytes" }]));
        return new TabsNode(tabs);
    }

    protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied)
    {
        if (applied.Count == 0)
        {
            return [];
        }

        var old = ReadWord(8);
        var checksum = SaveContainer.CalculateTsushimaChecksum(_bytes, _container.Word1, _container.Magic, _container.Word3);
        WriteWord(8, checksum);
        return old == checksum ? [] : [new ValueChange(ChecksumId, FieldValue.Of((ulong)old), FieldValue.Of((ulong)checksum))];
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var outcome = base.ApplyEdits(edits);
        // The lazy raw block contains snapshot text, so invalidate it when the quantities change.
        return outcome with { StructureChanged = outcome.Succeeded && outcome.Changes.Count > 0 };
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = _bytes.ToArray();
        var parsed = SaveContainer.Parse(output, _file.Directory.TitleId);
        if (parsed.Kind == ContainerKind.Tsushima)
        {
            var content = output.AsSpan(0, (int)parsed.Word1);
            _ = TsushimaInventory.Parse(content);
            _ = TsushimaProgress.Parse(content, parsed.Word3);
        }

        return [new FileOutput(_file.RelativePath, output)];
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId switch
    {
        InventoryBlockId when _inventory is not null => [BuildRawInventory()],
        ProgressBlockId when _progress is not null => [new TextNode(
            $"Difficulty block: 0x{_progress.Start:X}–0x{_progress.End:X} (exclusive). " +
            "Four length-prefixed difficulty names each precede a float32 counter. Counter semantics are inferred; " +
            "their sum does not match subtitle play time. The separate v48/v49 play timer is at table + 231.")],
        _ => throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'."),
    };

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        if (blobId != "content")
        {
            throw new KeyNotFoundException($"Unknown blob '{blobId}'.");
        }

        if (offset < 0 || count < 0 || offset > _bytes.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        // A caller must not be able to mutate the document through the returned memory.
        return _bytes.AsSpan((int)offset, count).ToArray();
    }

    private IReadOnlyList<ViewNode> BuildHeaderFields(bool isTsushima)
    {
        string[] labels = isTsushima ? ["Magic", "Content size", "Checksum", "Format version"] :
            ["Header word 0", "Header word 1", "Header word 2", "Header word 3"];
        return Enumerable.Range(0, labels.Length).Select(index => (ViewNode)(Field(
            $"container/word{index}", labels[index], IntegerSpec.UInt32 with { Format = IntegerFormat.Hex },
            () => FieldValue.Of((ulong)ReadWord(index * sizeof(uint)))) with
        {
            Confidence = isTsushima ? Confidence.Verified : Confidence.Unknown,
            Description = $"Little-endian uint32 at offset 0x{index * sizeof(uint):X2}.",
            Warning = isTsushima ? null : "Yōtei game fields remain unmapped; editing is disabled.",
        })).ToArray();
    }

    private TableNode BuildInventory()
    {
        var rows = _inventory!.Records.Select(record =>
        {
            var flags = ReadWord(record.DataOffset);
            var quantity = ReadWord(record.QuantityOffset);
            var mirrorsMatch = !record.IsAmmo || quantity == ReadWord(record.QuantityOffset + sizeof(uint));
            var editable = (record.IsAmmo || record.IsResource) && flags is 1 or 3 && mirrorsMatch &&
                quantity <= InventoryRecord.MaximumQuantity;
            var quantityField = Field(record.ValueId, record.Name, new IntegerSpec(0,
                Math.Max(quantity, InventoryRecord.MaximumQuantity)),
                () => FieldValue.Of((ulong)ReadWord(record.QuantityOffset)),
                editable ? value => SetQuantity(record, value.AsUInt32()) : null,
                value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 ||
                    value.AsInteger() > InventoryRecord.MaximumQuantity ? "Quantity must be between 0 and 32767." : null) with
            {
                Confidence = record.IsAmmo ? Confidence.Verified : Confidence.Inferred,
                Description = $"Named record at 0x{record.Start:X}; count at 0x{record.QuantityOffset:X}. " +
                    (record.IsAmmo ? "Apollo/Save Wizard targets these two count words." :
                    "Resource count inferred from the named record and consistent slot values; internal identifier retained."),
                Warning = editable ? "In-game capacity limits are unknown; large quantities may be clamped." :
                    "Unmapped, locked, oversized, or inconsistent record: inspection only.",
            };
            return new TableRow(record.Name, new Dictionary<string, ViewNode>
            {
                ["name"] = new TextNode(record.Name),
                ["quantity"] = quantityField,
            });
        }).ToArray();
        return new TableNode([new TableColumn("name", "Internal item name"), new TableColumn("quantity", "Quantity")], rows)
        { Title = "Named inventory", Description = "Located relative to LegendsInvestment; 48 length-prefixed records." };
    }

    private void SetQuantity(InventoryRecord record, uint quantity)
    {
        WriteWord(record.QuantityOffset, quantity);
        if (record.IsAmmo)
        {
            WriteWord(record.QuantityOffset + sizeof(uint), quantity);
        }
    }

    private IReadOnlyList<ViewNode> BuildBlocks()
    {
        var nodes = new List<ViewNode>
        {
            new TextNode($"Inventory: 0x{_inventory!.Start:X}–0x{_inventory.End:X} (exclusive). " +
                "uint32 count, then 48 records: uint32 name length, ASCII name, five little-endian words."),
            SectionNode.Lazy("Inventory record words", InventoryBlockId, "48 records"),
        };
        if (_progress is not null)
        {
            nodes.Add(SectionNode.Lazy("Difficulty and play-time block", ProgressBlockId));
        }

        return nodes;
    }

    private TableNode BuildRawInventory()
    {
        var rows = _inventory!.Records.Select(record =>
        {
            var cells = new Dictionary<string, ViewNode> { ["name"] = new TextNode(record.Name) };
            for (var index = 0; index < InventoryRecord.WordCount; index++)
            {
                var offset = record.DataOffset + index * sizeof(uint);
                cells[$"word{index}"] = new TextNode($"0x{ReadWord(offset):X8} @ 0x{offset:X}") { Monospace = true };
            }

            return new TableRow(record.Name, cells);
        }).ToArray();
        return new TableNode([new TableColumn("name", "Record"),
            .. Enumerable.Range(0, InventoryRecord.WordCount).Select(i => new TableColumn($"word{i}", $"Word {i}"))], rows)
        { Description = "Unknown raw words. Edits invalidate this block so its snapshot can be refreshed." };
    }

    private uint ReadWord(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));
    private void WriteWord(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(offset), value);
}
