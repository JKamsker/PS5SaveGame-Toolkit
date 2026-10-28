using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record DocumentEntry(string Name, byte[] Bytes, LarianResource? Resource);

internal sealed class BaldursGate3Document : BoundSaveDocument
{
    private readonly string _path;
    private readonly byte[] _original;
    private readonly LspkPackage? _package;
    private readonly IReadOnlyList<DocumentEntry> _entries;
    private readonly ResourceExplorer _explorer;

    private BaldursGate3Document(ISaveFile file, byte[] bytes, LspkPackage? package, List<DocumentEntry> entries)
    {
        _path = file.RelativePath;
        _original = bytes;
        _package = package;
        _entries = entries;
        _explorer = new ResourceExplorer(entries, Bind);
        Title = $"Baldur's Gate 3 · {file.FileName}";
    }

    public override string Title { get; }
    public override bool CanEdit => false;

    internal static BaldursGate3Document Open(ISaveFile file)
    {
        var bytes = file.ReadAllBytes();
        var budget = new ResourceBudget();
        LspkPackage? package = null;
        var entries = new List<DocumentEntry>();
        if (bytes.AsSpan().StartsWith("LSPK"u8))
        {
            package = LspkPackage.Read(bytes);
            foreach (var entry in package.Entries)
            {
                var content = package.Extract(entry);
                entries.Add(new DocumentEntry(entry.Name, content, ReadResource(entry.Name, content, budget)));
            }
        }
        else
        {
            var resource = ReadResource(file.FileName, bytes, budget) ?? throw new SaveFormatException("Expected an LSPK, LSOF or LSX file.");
            entries.Add(new DocumentEntry(file.FileName, bytes, resource));
        }

        return new BaldursGate3Document(file, bytes, package, entries);
    }

    public override ViewNode BuildView() => new TabsNode(
    [
        new Tab("Overview",
        [
            new StatsNode(
            [
                new Stat("Format", _package is null ? _entries[0].Resource!.Format : $"LSPK v{_package.Version}"),
                new Stat("Entries", _entries.Count.ToString()),
                new Stat("Resources", _entries.Count(e => e.Resource is not null).ToString()),
                new Stat("Original bytes", _original.Length.ToString("N0")),
            ]),
            new TextNode("Read-only explorer. Package integrity and live character component editing are not verified.") { Tone = Tone.Info },
            .. PackageMetadata(),
            .. _explorer.EntryList(_package),
        ]),
        new Tab("Resources", _explorer.ResourceList()),
        new Tab("Raw", [new HexNode("original", _original.Length) { Title = "Original file (unchanged)" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _explorer.Expand(lazyId, _package);

    // Always preserve original bytes, including unknown metadata, padding, XML spelling and trailing data.
    public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(_path, _original.ToArray())];

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var bytes = blobId == "original" ? _original.AsMemory() : _explorer.Blob(blobId);
        if (offset < 0 || offset > bytes.Length || count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Invalid BG3 blob range.");
        }

        return bytes.Slice((int)offset, Math.Min(count, bytes.Length - (int)offset));
    }

    public override AssetContent? GetAsset(string assetId)
    {
        if (!_explorer.TryEntryAsset(assetId, out var entry))
        {
            return null;
        }

        return new AssetContent("image/webp", entry.Bytes.ToArray());
    }

    private FieldNode Bind(string id, string label, ResourceAttribute attribute)
    {
        var value = AttributeDecoder.Read(attribute);
        return Field(id, label, AttributeDecoder.Spec(value, attribute.Type), () => value) with
        {
            Confidence = Confidence.Unknown,
            Description = $"Stored type: {attribute.Type}. Raw field meaning is not validated against game behavior.",
            Warning = attribute.Type is LarianAttributeType.Float or LarianAttributeType.Double && value.Kind == Sdk.Values.FieldValueKind.Text
                ? "Non-finite floating-point value; displayed as text and preserved unchanged." : null,
        };
    }

    private IEnumerable<ViewNode> PackageMetadata()
    {
        if (_package is not null)
        {
            yield return new TextNode($"Header digest (unverified): {_package.HeaderDigest}") { Monospace = true };
            yield return new TextNode($"Package flags: 0x{_package.Flags:X2}. All payloads decompressed and resource tables validated.");
        }
    }

    private static LarianResource? ReadResource(string name, byte[] bytes, ResourceBudget budget)
    {
        if (name.EndsWith(".lsf", StringComparison.OrdinalIgnoreCase))
        {
            return LsfReader.Read(bytes, budget);
        }

        return name.EndsWith(".lsx", StringComparison.OrdinalIgnoreCase) ? LsxReader.Read(bytes, budget) : null;
    }
}
