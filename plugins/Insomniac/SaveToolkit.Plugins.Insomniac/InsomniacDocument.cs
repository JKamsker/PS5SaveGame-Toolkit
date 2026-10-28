using System.Globalization;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class InsomniacDocument : BoundSaveDocument
{
    private const int StringLimit = 24;
    private readonly string _path;
    private readonly string _fileName;
    private readonly Dat1Container _container;
    private readonly RiftPlayerView _player;
    private readonly DdlSections _sections;
    private readonly DdlTreeView _tree;
    private readonly RiftReadOnlyViews _readOnly;
    private readonly RiftWorldView _world;
    private readonly InventoryView _inventory;
    private readonly Dictionary<string, Func<IReadOnlyList<ViewNode>>> _lazy = new(StringComparer.Ordinal);

    internal InsomniacDocument(string path, Dat1Container container, string fileName, IReferenceContext references)
    {
        _path = path;
        _fileName = fileName;
        _container = container;
        _sections = new DdlSections(container);
        _player = new RiftPlayerView(container, _sections);
        var schema = references.Catalog("rift-schema");
        _tree = new DdlTreeView(BindReadOnly, _lazy, schema);
        _readOnly = new RiftReadOnlyViews(_sections, references.Catalog("rift-weapons"), schema, BindReadOnly);
        _world = new RiftWorldView(_sections, BindReadOnly);
        _inventory = new InventoryView(InventoryReader.Read(container),
            references.Catalog("rift-item-types"), BindReadOnly, _lazy);
    }

    public override string Title => _fileName.Equals("i29-userprefs.save", StringComparison.OrdinalIgnoreCase)
        ? "Rift Apart · User preferences"
        : "Rift Apart · Gameplay save";

    public override bool CanEdit => false;

    public override ViewNode BuildView()
    {
        _lazy.Clear();
        var tabs = new List<Tab>();
        if (_player.HasCampaign) tabs.Add(new Tab("Player", _player.Build(BindReadOnly)));
        if (_player.HasCampaign)
        {
            tabs.Add(new Tab("Loadout", _readOnly.Loadout()));
            tabs.Add(new Tab("Inventory", _inventory.Build()));
            tabs.Add(new Tab("World", _world.Build()));
            tabs.Add(new Tab("Campaign details", _readOnly.CampaignDetails()));
        }
        if (_sections.Roots.ContainsKey(DdlSections.PreferencesTag))
            tabs.Add(new Tab("Preferences", _readOnly.Preferences()));
        tabs.Add(new Tab("Systems", _sections.Roots.Select(root =>
        {
            var id = $"system/{root.Key:x8}";
            _lazy[id] = () => _tree.Fields($"ddl/{root.Key:x8}", root.Value);
            return (ViewNode)new SectionNode(_sections.Label(root.Key), [])
            {
                LazyId = id,
                Badge = $"{root.Value.Count} root fields",
                Description = DdlSections.Attribution,
            };
        }).ToArray()));
        tabs.AddRange([
            new Tab("Overview", [
                new StatsNode([
                    new Stat("File size", FormatBytes(_container.Bytes.Length)),
                    new Stat("Sections", _container.SectionCount.ToString(CultureInfo.InvariantCulture)),
                    new Stat("Auxiliary records", _container.AuxiliaryCount.ToString(CultureInfo.InvariantCulture)),
                    new Stat("DAT1 word", $"0x{_container.Word:X8}"),
                ]),
                new SectionNode("Container header", HeaderFields()),
                new TextNode("This document is read-only. Selected gameplay fields are named and validated; complete integrity coverage remains unproven. All bytes are preserved verbatim.")
                {
                    Tone = Tone.Warning,
                },
            ]),
            new Tab("Sections", BuildSectionsView()),
            new Tab("Raw bytes", [new HexNode("file", _container.Bytes.Length) { Title = "Complete DAT1 file" }]),
        ]);
        return new TabsNode(tabs);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) =>
        _lazy.TryGetValue(lazyId, out var load)
            ? load()
            : throw new KeyNotFoundException($"Unknown DAT1 section '{lazyId}'.");

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        ReadOnlySpan<byte> source = blobId switch
        {
            "file" => _container.Bytes,
            "prefix" => _container.Bytes.AsSpan(_container.DescriptorEnd, _container.PrefixLength),
            _ when TrySection(blobId, out var section) => _container.SectionBytes(section),
            _ => throw new KeyNotFoundException($"Unknown DAT1 blob '{blobId}'."),
        };

        if (offset < 0 || count < 0 || offset > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var length = Math.Min(count, source.Length - checked((int)offset));
        return source.Slice(checked((int)offset), length).ToArray();
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var reparsed = Dat1Container.Parse(_container.Bytes);
        _ = new RiftPlayerView(reparsed);
        _ = InventoryReader.Read(reparsed);
        return [new FileOutput(_path, _container.Bytes.ToArray())];
    }

    private FieldNode BindReadOnly(string id, string label, FieldSpec spec, FieldValue value) =>
        Field(id, label, spec, () => value);

    private IReadOnlyList<ViewNode> HeaderFields() =>
    [
        HeaderField("header/magic", "Magic", 0x4441_5431u, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }),
        HeaderField("header/word", "Header word", _container.Word, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }),
        HeaderField("header/total-size", "Total size", _container.TotalSize, IntegerSpec.UInt32),
        HeaderField("header/section-count", "Section count", _container.SectionCount, IntegerSpec.UInt16),
        HeaderField("header/auxiliary-count", "Auxiliary count", _container.AuxiliaryCount, IntegerSpec.UInt16),
        new TextNode($"Descriptor table: 0x{_container.DescriptorEnd:X} bytes; section data starts at 0x{_container.DataStart:X}.")
        {
            Monospace = true,
            Tone = Tone.Muted,
        },
        new HexNode("prefix", _container.PrefixLength) { Title = "String-table and padding region" },
    ];

    private FieldNode HeaderField(string id, string label, ulong value, IntegerSpec spec) =>
        Field(id, label, spec, () => FieldValue.Of(value)) with
        {
            Confidence = Confidence.Verified,
            Description = "DAT1 framing value validated while opening the file.",
        };

    private IReadOnlyList<ViewNode> BuildSectionsView()
    {
        var rows = _container.Sections.Select(section => new TableRow(section.Index.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, ViewNode>
            {
                ["index"] = new TextNode(section.Index.ToString(CultureInfo.InvariantCulture)),
                ["tag"] = new TextNode($"0x{section.Tag:X8}") { Monospace = true },
                ["offset"] = new TextNode($"0x{section.Offset:X}") { Monospace = true },
                ["size"] = new TextNode(section.Size.ToString(CultureInfo.InvariantCulture)),
                ["detail"] = LazySection(section),
            })).ToArray();

        return [
            new TextNode("Validated DDL roots include nested world state and preferences. Expand a row's details to explore its fields and bytes. Opaque payloads retain their original hex explorer.") { Tone = Tone.Info },
            new TableNode(
                [new("index", "#"), new("tag", "Tag"), new("offset", "Offset"), new("size", "Bytes"), new("detail", "Explorer")],
                rows)
            {
                Title = "DAT1 section descriptors",
            },
        ];
    }

    private SectionNode LazySection(Dat1Section section)
    {
        var id = $"section/{section.Index}";
        _lazy[id] = () => SectionDetails(section);
        return SectionNode.Lazy($"Section {section.Index} · 0x{section.Tag:X8}", id, $"{section.Size} bytes");
    }

    private IReadOnlyList<ViewNode> SectionDetails(Dat1Section section)
    {
        var bytes = _container.SectionBytes(section).ToArray();
        var strings = FindAsciiRuns(bytes).Take(StringLimit).Select((value, index) =>
            (ViewNode)new TextNode($"0x{value.Offset:X}: {value.Text}") { Monospace = true }).ToArray();
        var result = new List<ViewNode>
        {
            new TextNode($"Tag 0x{section.Tag:X8}; offset 0x{section.Offset:X}; length {section.Size} bytes.") { Monospace = true },
            new HexNode($"section/{section.Index}/bytes", bytes.Length) { Title = "Section bytes" },
        };
        var fields = _sections.Roots.TryGetValue(section.Tag, out var root)
            ? _tree.Fields($"ddl/{section.Tag:x8}", root) : [];
        if (fields.Count > 0) result.Add(new SectionNode("Named DDL root fields", fields));
        if (strings.Length > 0)
        {
            result.Add(new SectionNode("Printable ASCII runs (untyped)", strings) { Badge = strings.Length.ToString(CultureInfo.InvariantCulture) });
        }
        else
        {
            result.Add(new TextNode("No printable ASCII runs were found in this section.") { Tone = Tone.Muted });
        }

        return result;
    }

    private bool TrySection(string blobId, out Dat1Section section)
    {
        section = default!;
        const string prefix = "section/";
        if (!blobId.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(blobId[prefix.Length..].Split('/')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        section = _container.Sections.FirstOrDefault(value => value.Index == index)!;
        return section is not null && blobId.EndsWith("/bytes", StringComparison.Ordinal);
    }

    private static IEnumerable<(int Offset, string Text)> FindAsciiRuns(byte[] bytes)
    {
        var start = -1;
        for (var index = 0; index <= bytes.Length; index++)
        {
            var printable = index < bytes.Length && bytes[index] is >= 0x20 and <= 0x7E;
            if (printable && start < 0)
            {
                start = index;
            }
            else if (!printable && start >= 0)
            {
                if (index - start >= 4)
                {
                    yield return (start, Encoding.ASCII.GetString(bytes, start, index - start));
                }
                start = -1;
            }
        }
    }

    private static string FormatBytes(int count) => count >= 1024 * 1024
        ? $"{count / (1024d * 1024d):0.0} MiB"
        : $"{count / 1024d:0.0} KiB";
}
