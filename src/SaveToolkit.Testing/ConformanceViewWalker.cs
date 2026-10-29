using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using Xunit;

namespace SaveToolkit.Testing;

/// <summary>Collects and checks fields from eager views, lazy sections and bounded F8 pages.</summary>
internal sealed class ConformanceViewWalker(ISaveDocument document, string path, int lazyBudget, int queryBudget,
    Action<ViewNode>? observe = null)
{
    private readonly ViewIndex _index = new(document);
    private readonly Dictionary<string, FieldNode> _fields = new(StringComparer.Ordinal);
    private readonly Queue<ViewNode> _pending = new();
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly ConformanceQueryPages _queries = new(document, queryBudget);

    public IReadOnlyList<FieldNode> Walk()
    {
        Accept(document.BuildView());
        var remaining = lazyBudget;
        while (_pending.TryDequeue(out var node))
        {
            if (node is QueryableTableNode table)
            {
                foreach (var page in _queries.Read(table)) Accept(page);
            }
            else if (node is SectionNode { LazyId: { } id } && _expanded.Add(id) && remaining-- > 0)
            {
                foreach (var child in document.ExpandNode(id)) Accept(child);
            }
        }

        foreach (var diagnostic in _index.SelectionDiagnostics)
            TestContext.Current.TestOutputHelper?.WriteLine("Selection binding warning: " + diagnostic);
        return _fields.Values.ToList();
    }

    private void Accept(ViewNode node)
    {
        _index.Add(node);
        _ = JsonSerializer.Serialize(node, ToolkitJson.Options);
        foreach (var child in Flatten(node))
        {
            observe?.Invoke(child);
            switch (child)
            {
                case FieldNode field:
                    // Several presentations may share an ID, but must agree on its contract and value.
                    if (_fields.TryGetValue(field.ValueId, out var earlier))
                    {
                        Assert.True(
                            JsonSerializer.Serialize(earlier.Spec, ToolkitJson.Options) == JsonSerializer.Serialize(field.Spec, ToolkitJson.Options),
                            $"{path}: value id '{field.ValueId}' appears with different specs.");
                        Assert.True(earlier.Value == field.Value,
                            $"{path}: value id '{field.ValueId}' appears with different values.");
                    }
                    else _fields.Add(field.ValueId, field);
                    break;
                case SectionNode { LazyId: not null }:
                case QueryableTableNode:
                    _pending.Enqueue(child);
                    break;
            }
        }
        // Check raw canonical publications above before normalizing presentations, so a reference
        // cannot conceal conflicting plugin snapshots. Resolve each canonical target only once.
        foreach (var reference in Flatten(node).OfType<FieldReferenceNode>().DistinctBy(reference => reference.TargetValueId))
        {
            if (_index.ResolveFieldReferences(document, reference) is not FieldNode field) continue;
            if (_fields.TryGetValue(field.ValueId, out var canonical)) Assert.Equal(canonical.Value, field.Value);
            else _fields.Add(field.ValueId, field);
        }
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        var children = node switch
        {
            SectionNode { LazyId: null } section => section.Children,
            TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
            TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
            _ => [],
        };
        foreach (var child in children.SelectMany(Flatten)) yield return child;
    }
}
