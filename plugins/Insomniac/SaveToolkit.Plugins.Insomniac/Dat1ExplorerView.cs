using System.Globalization;
using System.Text;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Container diagnostics, separate from gameplay mutation and action orchestration.</summary>
internal sealed class Dat1ExplorerView(Dat1Container container, DdlSections sections, DdlTreeView tree,
    Dictionary<string, Func<IReadOnlyList<ViewNode>>> lazy,
    Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
{
    private const int StringLimit = 24;
    private const int MaxAsciiPreviewLength = 256;
    internal IReadOnlyList<ViewNode> HeaderFields() =>
    [
        HeaderField("header/magic", "Magic", 0x4441_5431u, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }),
        HeaderField("header/word", "Header word", container.Word, IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }),
        HeaderField("header/total-size", "Total size", container.TotalSize, IntegerSpec.UInt32),
        HeaderField("header/section-count", "Section count", container.SectionCount, IntegerSpec.UInt16),
        HeaderField("header/auxiliary-count", "Auxiliary count", container.AuxiliaryCount, IntegerSpec.UInt16),
        new TextNode($"Descriptor table: 0x{container.DescriptorEnd:X} bytes; section data starts at 0x{container.DataStart:X}.")
        {
            Monospace = true,
            Tone = Tone.Muted,
        },
        new HexNode("prefix", container.PrefixLength) { Title = "String-table and padding region" },
    ];

    private FieldNode HeaderField(string id, string label, ulong value, IntegerSpec spec) =>
        bind(id, label, spec, FieldValue.Of(value)) with
        {
            Confidence = Confidence.Verified,
            Description = "DAT1 framing value validated while opening the file.",
        };

    internal IReadOnlyList<ViewNode> BuildSectionsView()
    {
        var rows = container.Sections.Select(section => new TableRow(section.Index.ToString(CultureInfo.InvariantCulture),
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
        lazy[id] = () => SectionDetails(section);
        return SectionNode.Lazy($"Section {section.Index} · 0x{section.Tag:X8}", id, $"{section.Size} bytes");
    }

    private IReadOnlyList<ViewNode> SectionDetails(Dat1Section section)
    {
        var bytes = container.Bytes.AsMemory(checked((int)section.Offset), checked((int)section.Size));
        var strings = FindAsciiRuns(bytes).Take(StringLimit).Select((value, index) =>
            (ViewNode)new TextNode($"0x{value.Offset:X}: {value.Text}") { Monospace = true }).ToArray();
        var result = new List<ViewNode>
        {
            new TextNode($"Tag 0x{section.Tag:X8}; offset 0x{section.Offset:X}; length {section.Size} bytes.") { Monospace = true },
            new HexNode($"section/{section.Index}/bytes", bytes.Length) { Title = "Section bytes" },
        };
        var fields = sections.Roots.TryGetValue(section.Tag, out var root)
            ? tree.Fields($"ddl/{section.Tag:x8}", root) : [];
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

    internal bool TrySection(string blobId, out Dat1Section section)
    {
        section = default!;
        const string prefix = "section/";
        if (!blobId.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(blobId[prefix.Length..].Split('/')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        section = container.Sections.FirstOrDefault(value => value.Index == index)!;
        return section is not null && blobId.EndsWith("/bytes", StringComparison.Ordinal);
    }

    private static IEnumerable<(int Offset, string Text)> FindAsciiRuns(ReadOnlyMemory<byte> bytes)
    {
        var start = -1;
        for (var index = 0; index <= bytes.Length; index++)
        {
            var printable = index < bytes.Length && bytes.Span[index] is >= 0x20 and <= 0x7E;
            if (printable && start < 0)
            {
                start = index;
            }
            else if (!printable && start >= 0)
            {
                if (index - start >= 4)
                {
                    var length = index - start;
                    var text = Encoding.ASCII.GetString(bytes.Span.Slice(start, Math.Min(length, MaxAsciiPreviewLength)));
                    yield return (start, length > MaxAsciiPreviewLength ? text + "…" : text);
                }
                start = -1;
            }
        }
    }

}
