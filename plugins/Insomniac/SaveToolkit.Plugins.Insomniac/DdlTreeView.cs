using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Each expansion stays bounded, including scalar arrays and maps.</summary>
internal sealed class DdlTreeView(
    Func<string, string, FieldSpec, FieldValue, FieldNode> bind,
    Dictionary<string, Func<IReadOnlyList<ViewNode>>> lazy,
    IReferenceCatalog? schema)
{
    private const int PageSize = 100;

    internal IReadOnlyList<ViewNode> Fields(string path, IReadOnlyDictionary<string, DdlField> fields) =>
        fields.Values.Select(field => (ViewNode)FieldSection(path, field)).ToArray();

    private SectionNode FieldSection(string path, DdlField field)
    {
        var id = $"{path}/{Uri.EscapeDataString(field.Name)}";
        lazy[id] = () => Values(id, field);
        return new SectionNode(field.Name, [])
        {
            LazyId = id,
            Badge = $"{field.Kind} · {field.Count}",
            Description = Description(field),
        };
    }

    private IReadOnlyList<ViewNode> Values(string path, DdlField field)
    {
        if (field.Count == 0) return [new TextNode("Empty stored collection (0 entries).") { Tone = Tone.Muted }];
        if (field.Count <= PageSize) return Range(path, field, 0, field.Count);
        return Enumerable.Range(0, (field.Count + PageSize - 1) / PageSize).Select(page =>
        {
            var start = page * PageSize;
            var count = Math.Min(PageSize, field.Count - start);
            var id = $"{path}/page/{start}";
            lazy[id] = () => Range(path, field, start, count);
            return (ViewNode)new SectionNode($"Entries {start}–{start + count - 1}", []) { LazyId = id };
        }).ToArray();
    }

    private IReadOnlyList<ViewNode> Range(string path, DdlField field, int start, int count) =>
        Enumerable.Range(start, count).Select(index =>
        {
            if (field.Kind == DdlValueKind.Object)
            {
                var child = field.Objects[index];
                var id = $"{path}/{index}";
                lazy[id] = () => Fields(id, child.Fields);
                return (ViewNode)new SectionNode($"{field.Name}[{index}]", [])
                {
                    LazyId = id,
                    Badge = $"{child.Fields.Count} fields",
                    Description = $"Validated object at 0x{child.Offset:X}, {child.Size} bytes. Map keys are stored identities; array positions do not prove ownership.",
                };
            }
            return bind($"{path}/{index}", field.Count == 1 ? field.Name : $"{field.Name}[{index}]",
                field.Spec, field.Values[index]) with
            {
                Confidence = Confidence.Unknown,
                Description = Description(field),
            };
        }).ToArray();

    private string Description(DdlField field) =>
        $"Stored name/hash and {field.Kind} encoding validated at 0x{field.Offset:X}; gameplay interpretation remains unknown. " +
        (schema?.Find(field.Name)?.AttributionText ?? DdlSections.Attribution);
}
