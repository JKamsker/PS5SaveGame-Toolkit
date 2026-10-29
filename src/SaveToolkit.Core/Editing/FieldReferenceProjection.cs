using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Editing;

/// <summary>Normalizes bounded presentations without publishing bindings or expanding hidden content.</summary>
internal sealed class FieldReferenceProjection(ISaveDocument document, ViewIndex index, CancellationToken cancellationToken)
{
    private const int MaxNodes = 100_000;
    private const int MaxDepth = 128;
    private const int MaxTargets = 10_000;
    private readonly Dictionary<string, FieldNode?> _targets = new(StringComparer.Ordinal);

    public ViewNode Resolve(ViewNode root)
    {
        Collect(root);
        if (_targets.Count == 0) return root;
        index.ResolveMetadata(_targets.Keys, cancellationToken);
        foreach (var id in _targets.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shown = index.ShownField(id);
            var metadata = index.ResolvedMetadata(id);
            // Metadata-aware documents can explicitly retire a binding. Never resurrect a cached view.
            if (metadata is null && (index.RequiresMetadata(id) || shown is null)) continue;
            if (shown is null && metadata?.Presentation is null) continue;
            try
            {
                var field = shown ?? new FieldNode(id, metadata!.Label, metadata.Spec, default);
                if (metadata?.Presentation is { } presentation)
                {
                    if (presentation.Confidence is { } confidence && !Enum.IsDefined(confidence))
                        throw new InvalidOperationException("Invalid canonical field confidence.");
                    if (shown is not null && (shown.Confidence != presentation.Confidence || shown.Warning != presentation.Warning
                        || shown.Description != presentation.Description))
                        throw new InvalidOperationException("Registered and rendered canonical field annotations conflict.");
                    field = field with { Confidence = presentation.Confidence, Warning = presentation.Warning, Description = presentation.Description };
                }
                field = field with { Spec = metadata?.Spec ?? field.Spec, Value = document.GetValue(id) };
                FieldReferenceWireBudget.CheckField(field, cancellationToken);
                _targets[id] = field;
            }
            catch (KeyNotFoundException)
            {
                // Legacy loaded-only fields may disappear before the next structural view is published.
                _targets[id] = null;
            }
        }
        var projected = Map(root);
        FieldReferenceWireBudget.Check(projected, cancellationToken);
        return projected;
    }

    private void Collect(ViewNode root)
    {
        var pending = new Stack<(ViewNode Node, int Depth)>();
        pending.Push((root, 0));
        var count = 0;
        var exceededBudget = false;
        while (pending.TryPop(out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Ordinary legacy trees retain their existing size policy, even after a reference page.
            // Only reference-bearing output opts into the projection traversal budget. Defer the
            // failure until a reference is seen so its position cannot bypass the bound.
            exceededBudget |= ++count > MaxNodes || entry.Depth > MaxDepth;
            if (entry.Node is FieldReferenceNode reference)
            {
                reference.Validate();
                _targets.TryAdd(reference.TargetValueId, null);
                if (_targets.Count > MaxTargets)
                    throw new InvalidOperationException("Too many distinct field-reference targets.");
            }
            if (exceededBudget && _targets.Count > 0)
                throw new InvalidOperationException("Field-reference projection traversal exceeds its node or depth budget.");
            // Lazy children are ignored just as they are by ViewIndex. Action parameters are local inputs.
            foreach (var child in Children(entry.Node)) pending.Push((child, entry.Depth + 1));
        }
    }

    private static IEnumerable<ViewNode> Children(ViewNode node) => node switch
    {
        SectionNode { LazyId: null } section => section.Children,
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
        TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
        _ => [],
    };

    private ViewNode Map(ViewNode node)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return node switch
        {
            FieldNode shown when _targets.TryGetValue(shown.ValueId, out var current) && current is not null => shown with
            {
                Spec = current.Spec, Value = current.Value,
            },
            FieldReferenceNode reference when _targets[reference.TargetValueId] is { } field => field with
            {
                Label = reference.Label ?? field.Label,
                Description = string.Join("\n", new[] { field.Description, reference.Description }.Where(text => !string.IsNullOrEmpty(text))),
            },
            SectionNode { LazyId: null } section => section with { Children = section.Children.Select(Map).ToArray() },
            TabsNode tabs => tabs with { Tabs = tabs.Tabs.Select(tab => tab with { Children = tab.Children.Select(Map).ToArray() }).ToArray() },
            TableNode table => table with
            {
                Rows = table.Rows.Select(row => row with
                {
                    Cells = row.Cells.ToDictionary(pair => pair.Key, pair => Map(pair.Value), StringComparer.Ordinal),
                }).ToArray(),
            },
            _ => node,
        };
    }
}
