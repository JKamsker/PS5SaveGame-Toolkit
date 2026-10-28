using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class LazyPages
{
    internal const int PageSize = 200;
    private readonly Dictionary<string, Func<IReadOnlyList<ViewNode>>> _sections = new(StringComparer.Ordinal);

    public SectionNode Section(string id, string title, int count, Func<int, int, IReadOnlyList<ViewNode>> leaf) =>
        Section(id, title, 0, count, leaf);

    public IReadOnlyList<ViewNode> Expand(string id) => _sections.TryGetValue(id, out var expand) ? expand() :
        throw new KeyNotFoundException($"Unknown Pragmata section '{id}'.");

    private SectionNode Section(string id, string title, int start, int count, Func<int, int, IReadOnlyList<ViewNode>> leaf)
    {
        var key = $"{id}/{start}/{count}";
        _sections[key] = () => Page(id, start, count, leaf);
        return SectionNode.Lazy(title, key, $"{count} entries");
    }

    private IReadOnlyList<ViewNode> Page(string id, int start, int count, Func<int, int, IReadOnlyList<ViewNode>> leaf)
    {
        if (count <= PageSize) return leaf(start, count);
        // Recursively group large arrays: both intermediate and leaf expansions are bounded.
        var groupSize = Math.Max(PageSize, (count + PageSize - 1) / PageSize);
        var result = new List<ViewNode>();
        for (var i = start; i < start + count; i += groupSize)
        {
            var used = Math.Min(groupSize, start + count - i);
            result.Add(Section(id, $"Entries {i}–{i + used - 1}", i, used, leaf));
        }
        return result;
    }
}
