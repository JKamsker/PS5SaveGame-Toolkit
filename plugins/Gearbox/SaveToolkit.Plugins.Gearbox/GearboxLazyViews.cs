using System.Globalization;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Lazy callbacks resolve the current root after transactional rollback or view rebuild.</summary>
internal sealed class GearboxLazyViews
{
    internal const int PageSize = 100;
    private static readonly string[] RestrictedNames = ["token", "password", "secret", "account", "player_id", "net_id", "friend_id"];
    private readonly Dictionary<string, Func<IReadOnlyList<ViewNode>>> _sections = new(StringComparer.Ordinal);

    public SectionNode Register(string id, string label, Func<IReadOnlyList<ViewNode>> build, string? badge = null)
    {
        _sections[id] = build;
        return SectionNode.Lazy(label, id, badge);
    }

    public IReadOnlyList<ViewNode> Expand(string id) => _sections.TryGetValue(id, out var build)
        ? build() : throw new KeyNotFoundException($"Unknown Gearbox section '{id}'.");

    public SectionNode RegisterRaw(Func<ProtoMessage> get, string id = "raw") => Register(id, get().Type,
        () => RawPage(get, id, 0), get().Fields.Count.ToString(CultureInfo.InvariantCulture));

    private IReadOnlyList<ViewNode> RawPage(Func<ProtoMessage> get, string id, int offset)
    {
        var message = get();
        var result = new List<ViewNode>();
        foreach (var (field, index) in message.Fields.Select((f, i) => (f, i)).Skip(offset).Take(PageSize))
        {
            var label = $"{field.Number}: {field.Spec?.Name ?? "unknown"} [{index}]";
            if (field.Message is not null)
            {
                Func<ProtoMessage> child = () => get().Fields[index].Message!;
                result.Add(Register($"{id}/{index}", label, () => RawPage(child, $"{id}/{index}", 0)));
            }
            else result.Add(new SectionNode(label, [Primitive(field)]));
        }
        if (offset + PageSize < message.Fields.Count)
            result.Add(Register($"{id}:page:{offset + PageSize}", $"Next fields ({offset + PageSize} onwards)", () => RawPage(get, id, offset + PageSize)));
        return result;
    }

    private static TextNode Primitive(ProtoField field)
    {
        // Account identifiers and session-like profile metadata stay opaque, including in raw views.
        var name = field.Spec?.Name ?? string.Empty;
        var restricted = RestrictedNames.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));
        var value = restricted ? "Opaque account metadata (preserved)" : field.Value switch
        {
            string text => text.Length > 512 ? text[..512] + "…" : text,
            byte[] bytes => $"{bytes.Length} opaque bytes (preserved)",
            ulong integer when field.Spec?.Type == "bool" => integer != 0 ? "true" : "false",
            ulong integer when field.Spec?.Type is "int32" or "enum" => unchecked((int)integer).ToString(CultureInfo.InvariantCulture),
            ulong integer when field.Spec?.Type == "int64" => unchecked((long)integer).ToString(CultureInfo.InvariantCulture),
            uint bits when field.Spec?.Type == "float" => BitConverter.UInt32BitsToSingle(bits).ToString("R", CultureInfo.InvariantCulture),
            ulong bits when field.Spec?.Type == "double" => BitConverter.UInt64BitsToDouble(bits).ToString("R", CultureInfo.InvariantCulture),
            null => "Opaque protobuf group (preserved)",
            _ => Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
        return new TextNode(value) { Monospace = true, Tone = field.Spec is null ? Tone.Muted : Tone.Normal };
    }
}
