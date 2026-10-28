using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>
/// Smallest example of a plugin that reads a reference pack: "ITMS" magic, uint8 build, uint16 count,
/// then count × (uint32 item id, uint16 quantity). Names, limits and icons come from the embedded pack
/// <c>Fakes/ReferencePacks/test.items.en</c>; without a pack the plugin falls back to raw ids.
/// </summary>
public sealed class ItemsPlugin : IGamePlugin
{
    public const string TitleId = "PPSA99998";
    private const int HeaderSize = 7;
    private const int EntrySize = 6;
    private static readonly byte[] Magic = "ITMS"u8.ToArray();

    public PluginInfo Info { get; } = new("test.items", "Items test format", "1.0.0")
    {
        Games = [new GameTitle(TitleId, "Items Game")],
        References = ["https://github.com/JKamsker/PS5SaveGame-Toolkit"],
    };

    public ProbeResult Probe(ISaveFile file) =>
        file.ReadHeader(4).AsSpan().SequenceEqual(Magic) ? ProbeResult.Match(ProbeScore.Title, "Items save") : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        var bytes = file.ReadAllBytes();
        if (bytes.Length < HeaderSize || !bytes.AsSpan(0, 4).SequenceEqual(Magic)
            || bytes.Length != HeaderSize + (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5)) * EntrySize))
        {
            throw new SaveFormatException("Not an items save.");
        }

        // The save names its build; the pack for that build (or a build-agnostic one) is chosen.
        var catalog = references.Catalog("items", gameBuild: $"1.0{bytes[4]}");
        return new ItemsDocument(file, bytes, catalog, references);
    }

    public static byte[] Create(byte build, params (uint Id, ushort Quantity)[] items)
    {
        var bytes = new byte[HeaderSize + (items.Length * EntrySize)];
        Magic.CopyTo(bytes, 0);
        bytes[4] = build;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(5), (ushort)items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var at = HeaderSize + (i * EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), items[i].Id);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 4), items[i].Quantity);
        }

        return bytes;
    }

    private sealed class ItemsDocument(ISaveFile file, byte[] bytes, IReferenceCatalog? catalog, IReferenceContext references) : BoundSaveDocument
    {
        public override string Title => "Items";

        private static ReferenceSpec ItemSpec => new("items")
        {
            // Synthetic example: this inventory slot accepts consumables, not equipment.
            Assess = candidate => candidate.Category == "weapon"
                ? new ReferenceEligibility { Eligible = false, Reasons = ["This slot only accepts consumables."] }
                : new ReferenceEligibility(),
        };

        public override ViewNode BuildView()
        {
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5));
            var rows = Enumerable.Range(0, count).Select(Row).ToList();
            return new SectionNode("Inventory",
            [
                new TableNode([new TableColumn("icon", "Icon"), new TableColumn("item", "Item"), new TableColumn("quantity", "Quantity")], rows),
                new TextNode(catalog is null ? "No reference pack: showing raw item ids." : $"Names: {catalog.Dataset.Attribution.ToShortText()}")
                {
                    Tone = Tone.Muted,
                },
                new ActionNode("replace-first", "Replace first item")
                {
                    Parameters = [new FieldNode("replacement", "Replacement", ItemSpec, FieldValue.Of("1001"))],
                },
            ]);
        }

        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            var result = base.ApplyEdits(edits);
            // Names and quantity limits depend on the chosen id; rebuild their specs after replacement.
            return result with { StructureChanged = result.Changes.Any(c => c.ValueId.EndsWith("/id", StringComparison.Ordinal)) };
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            if (actionId != "replace-first")
            {
                throw new KeyNotFoundException(actionId);
            }

            var result = ApplyEdits([new ValueEdit("items/0/id", parameters["replacement"])]);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Message)));
            }

            return new ActionOutcome { Changes = result.Changes, StructureChanged = result.StructureChanged };
        }

        public override AssetContent? GetAsset(string assetId) => references.GetAsset(assetId);

        public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(file.RelativePath, bytes.ToArray())];

        private TableRow Row(int index)
        {
            var at = 7 + (index * 6);
            var id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
            var record = catalog?.Find(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var spec = IntegerSpec.UInt16 with { Max = record?.Limit ?? ushort.MaxValue };
            var quantity = Field($"items/{index}/quantity", record?.Name ?? $"Item {id}", spec,
                () => FieldValue.Of((long)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 4))),
                v => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 4), (ushort)v.AsInt32())) with
            {
                Description = record?.AttributionText,
                Confidence = record is null ? Confidence.Unknown : Confidence.Verified,
            };

            var cells = new Dictionary<string, ViewNode>
            {
                ["item"] = Field($"items/{index}/id", "Item", ItemSpec,
                    () => FieldValue.Of(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    v => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), uint.Parse(v.AsString(), System.Globalization.CultureInfo.InvariantCulture))) with
                {
                    Description = record?.AttributionText,
                },
                ["quantity"] = quantity,
                ["icon"] = record?.AssetId is { } asset ? new ImageNode(asset, record.Name) : new TextNode(string.Empty),
            };
            return new TableRow(index.ToString(System.Globalization.CultureInfo.InvariantCulture), cells);
        }
    }
}
