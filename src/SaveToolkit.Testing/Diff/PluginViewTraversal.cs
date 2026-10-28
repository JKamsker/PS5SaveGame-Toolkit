using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Testing.Diff;

/// <summary>Per-decode visitor; queues lazy sections and collections without retaining document state.</summary>
internal sealed class PluginViewTraversal(ISaveDocument document, SemanticTreeBuilder builder, int lazyBudget)
{
    private readonly Queue<PendingNode> _pending = new();
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly QueryableViewTraversal _queries = new(document, builder);

    public void Walk()
    {
        if (!Visit(document.BuildView(), "", 0, null)) return;

        var remaining = lazyBudget;
        while (_pending.TryDequeue(out var pending))
        {
            if (pending.Node is QueryableTableNode table)
            {
                if (!_queries.Visit(table, pending.Label, pending.Depth, Visit)) return;
                continue;
            }

            var section = (SectionNode)pending.Node;
            if (!_expanded.Add(section.LazyId!)) continue;
            if (remaining-- <= 0)
            {
                builder.MarkTruncated($"Lazy section '{section.LazyId}' not expanded (budget {lazyBudget}).");
                continue;
            }

            foreach (var child in document.ExpandNode(section.LazyId!))
            {
                if (!Visit(child, pending.Label, pending.Depth + 1, null)) return;
            }
        }
    }

    private bool Visit(ViewNode node, string label, int depth, string? cellPath)
    {
        if (depth > builder.Limits.MaxDepth)
        {
            builder.MarkTruncated($"View depth limit {builder.Limits.MaxDepth} reached at '{label}'.");
            return true;
        }

        // Rich read-only cells commonly put the saved scalar in the section title and provenance
        // in its children (Cyberpunk inspection tables). Keep that scalar under the cell identity.
        if (cellPath is not null && node is SectionNode cellSection &&
            !builder.Add(cellPath, "text", cellSection.Title, label)) return false;

        switch (node)
        {
            case FieldNode field:
                // A field may appear in eager, lazy and queried views; keep the original F1 path once.
                return builder.Contains(field.ValueId) || builder.Add(field.ValueId, KindOf(field.Spec), field.Value.ToString(), Join(label, field.Label));
            case TextNode text when cellPath is not null:
                return builder.Add(cellPath, "text", text.Text, label);
            case SectionNode { LazyId: not null } section:
                _pending.Enqueue(new(section, Join(label, section.Title), depth));
                return true;
            case SectionNode section:
                return section.Children.All(c => Visit(c, Join(label, section.Title), depth + 1, null));
            case TabsNode tabs:
                return tabs.Tabs.All(t => t.Children.All(c => Visit(c, Join(label, t.Title), depth + 1, null)));
            case TableNode table:
                return table.Rows.All(r => r.Cells.Values.All(c => Visit(c, Join(label, table.Title ?? "table"), depth + 1, null)));
            case QueryableTableNode table:
                _pending.Enqueue(new(table, Join(label, table.Title), depth));
                return true;
            default:
                // Action parameters and presentational nodes do not describe saved values.
                return true;
        }
    }

    internal static string Join(string path, string label) => path.Length == 0 ? label : $"{path} › {label}";

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

    private sealed record PendingNode(ViewNode Node, string Label, int Depth);
}
