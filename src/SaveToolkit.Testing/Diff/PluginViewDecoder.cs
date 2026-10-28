using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Example adapter that reuses an existing plugin as a semantic decoder: it opens the file, walks the
/// view (expanding lazy sections within a budget) and emits every <see cref="FieldNode"/> with its
/// <see cref="FieldNode.ValueId"/> as path. Plugins already promise value ids that are unique and
/// stable, and well-behaved ones derive them from record identity (<c>inventory/2227368435/quantity</c>),
/// so no plugin changes are needed. The document is only read, never edited or serialised.
/// </summary>
public sealed class PluginViewDecoder(IGamePlugin plugin, int lazyExpansionBudget = 10_000) : ISaveDecoder
{
    public IGamePlugin Plugin => plugin;

    public string Id => plugin.Info.Id;

    public bool CanDecode(ISaveFile file)
    {
        try
        {
            return plugin.Probe(file).Score > ProbeScore.Fallback;
        }
        catch (Exception)
        {
            // Probe must not throw; a plugin that does simply does not decode this file.
            return false;
        }
    }

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes)
        {
            throw new InvalidDataException($"File is {file.Length} bytes; the decode limit is {limits.MaxFileBytes}.");
        }

        using var document = plugin.Open(file);
        var builder = new SemanticTreeBuilder(Id, limits);
        var lazy = new Queue<(string Id, string Path)>();
        if (!Visit(document.BuildView(), "", builder, lazy))
        {
            return builder.Build();
        }

        var budget = lazyExpansionBudget;
        while (lazy.Count > 0)
        {
            if (budget-- <= 0)
            {
                builder.MarkTruncated($"{lazy.Count} lazy section(s) not expanded (budget {lazyExpansionBudget}).");
                break;
            }

            var (lazyId, path) = lazy.Dequeue();
            foreach (var child in document.ExpandNode(lazyId))
            {
                if (!Visit(child, path, builder, lazy))
                {
                    return builder.Build();
                }
            }
        }

        return builder.Build();
    }

    /// <returns>False once the value limit is reached.</returns>
    private static bool Visit(ViewNode node, string labelPath, SemanticTreeBuilder builder, Queue<(string, string)> lazy)
    {
        switch (node)
        {
            case FieldNode field:
                // A value may legitimately be shown twice (table and tree); keep its first occurrence.
                return builder.Contains(field.ValueId) || builder.Add(field.ValueId, KindOf(field.Spec), field.Value.ToString(), Join(labelPath, field.Label));
            case SectionNode { LazyId: { } id } section:
                lazy.Enqueue((id, Join(labelPath, section.Title)));
                return true;
            case SectionNode section:
                return section.Children.All(c => Visit(c, Join(labelPath, section.Title), builder, lazy));
            case TabsNode tabs:
                return tabs.Tabs.All(t => t.Children.All(c => Visit(c, Join(labelPath, t.Title), builder, lazy)));
            case TableNode table:
                return table.Rows.All(r => r.Cells.Values.All(c => Visit(c, Join(labelPath, table.Title ?? "table"), builder, lazy)));
            default:
                // Text, stats, images, actions and hex views carry no addressable values.
                return true;
        }
    }

    private static string Join(string path, string label) => path.Length == 0 ? label : $"{path} › {label}";

    private static string KindOf(FieldSpec spec) => spec switch
    {
        BoolSpec => "bool",
        IntegerSpec => "integer",
        FloatSpec => "float",
        TextSpec => "text",
        EnumSpec => "enum",
        FlagsSpec => "flags",
        DateTimeSpec => "datetime",
        _ => spec.GetType().Name,
    };
}
