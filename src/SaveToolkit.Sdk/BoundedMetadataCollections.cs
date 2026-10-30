namespace SaveToolkit.Sdk;

/// <summary>Actual traversal bounds for provider-owned metadata collections, including dishonest Count implementations.</summary>
public static class BoundedMetadataCollections
{
    /// <summary>Visits every admitted element exactly once, checking cancellation and actual cardinality.</summary>
    public static void Visit<T>(IReadOnlyList<T> source, int maximum, Action<T> visit,
        CancellationToken cancellationToken = default)
    {
        foreach (var item in Enumerate(source, maximum, cancellationToken)) visit(item);
    }

    /// <summary>Enumerates a finite collection and rejects discrepancies between Count and traversal.</summary>
    public static IEnumerable<T> Enumerate<T>(IReadOnlyList<T> source, int maximum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count < 0 || source.Count > maximum)
            throw new InvalidOperationException("Metadata collection exceeds its bounded entry limit.");
        var count = 0;
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > maximum || count > source.Count)
                throw new InvalidOperationException("Metadata collection has an inconsistent bounded entry count.");
            yield return item;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (count != source.Count)
            throw new InvalidOperationException("Metadata collection has an inconsistent bounded entry count.");
    }

    /// <summary>Copies a finite collection without trusting its Count or indexed projection.</summary>
    public static T[] Copy<T>(IReadOnlyList<T> source, int maximum, CancellationToken cancellationToken = default)
    {
        var result = new List<T>();
        Visit(source, maximum, result.Add, cancellationToken);
        return result.ToArray();
    }

    /// <summary>Admits and detaches an action-effect navigation path before invoking any codec.</summary>
    public static IReadOnlyList<string> Path(IReadOnlyList<string> source, CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        Visit(source, ActionPlanSnapshots.MaxEffectPathParts, part =>
        {
            if (part is null || part.Length > ActionPlanSnapshots.MaxEffectPathPartLength)
                throw new InvalidOperationException("Action effect navigation path exceeds its bounded limit.");
            result.Add(part);
        }, cancellationToken);
        return result.AsReadOnly();
    }
}
