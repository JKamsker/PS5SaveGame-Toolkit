using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

internal static class AriseExplorer
{
    private const int PageBytes = 1024;
    private const int WordBytes = 4;

    public static IReadOnlyList<ViewNode> Expand(string lazyId, AriseSave save, Func<int, FieldNode> word)
    {
        var parts = lazyId.Split('/');
        if (parts.Length is < 2 or > 3 || !int.TryParse(parts[1], out var sectionIndex)
            || sectionIndex < 0 || sectionIndex >= save.Sections.Count)
        {
            throw new KeyNotFoundException($"Unknown explorer node '{lazyId}'.");
        }

        var section = save.Sections[sectionIndex];
        if (parts[0] == "section" && parts.Length == 2)
        {
            var children = new List<ViewNode>
            {
                new TextNode($"ID 0x{section.Id:X8} · version {section.Version} · data +0x{section.Offset:X} · {section.Size} bytes") { Monospace = true },
            };
            for (var offset = 0; offset < section.Size; offset += PageBytes)
            {
                children.Add(SectionNode.Lazy($"Words +0x{offset:X}–+0x{Math.Min(offset + PageBytes, section.Size) - 1:X}", $"page/{sectionIndex}/{offset}"));
            }

            return children;
        }

        if (parts[0] != "page" || parts.Length != 3 || !int.TryParse(parts[2], out var pageOffset)
            || pageOffset < 0 || pageOffset >= section.Size || pageOffset % PageBytes != 0)
        {
            throw new KeyNotFoundException($"Unknown explorer page '{lazyId}'.");
        }

        var rows = new List<TableRow>();
        var end = Math.Min(pageOffset + PageBytes, section.Size);
        for (var relative = pageOffset; relative + WordBytes <= end; relative += WordBytes)
        {
            var offset = section.Offset + relative;
            rows.Add(new TableRow($"{offset:X}", new Dictionary<string, ViewNode>
            {
                ["offset"] = new TextNode($"+0x{relative:X}"),
                ["hex"] = new TextNode($"0x{AriseFormat.U32(save.Payload, offset):X8}"),
                ["value"] = word(offset),
            }));
        }

        return [new TableNode([new("offset", "Section offset"), new("hex", "Hex"), new("value", "UInt32 (unknown meaning)")], rows)];
    }
}
