using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Editing;

/// <summary>Rebinds remembered lazy sections after their parents become known to a fresh index.</summary>
internal static class LazyViewExpansion
{
    /// <summary>
    /// Expands each reachable id once. Unreachable ids are returned separately so the caller decides
    /// whether to retain them for a later structural undo. Plugin failures propagate to the caller.
    /// </summary>
    public static Result Expand(ISaveDocument document, ViewIndex index, IEnumerable<string> lazyIds)
    {
        var pending = lazyIds.ToHashSet(StringComparer.Ordinal);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var ready = pending.Where(index.KnowsLazy).ToArray();
            if (ready.Length == 0) break;
            foreach (var id in ready)
            {
                index.AddAll(document.ExpandNode(id));
                expanded.Add(id);
                pending.Remove(id);
            }
        }

        return new Result(expanded, pending);
    }

    internal sealed record Result(HashSet<string> Expanded, HashSet<string> Missing);
}
