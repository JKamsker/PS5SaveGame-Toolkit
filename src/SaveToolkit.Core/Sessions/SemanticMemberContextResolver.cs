using SaveToolkit.Sdk;
using SaveToolkit.Core.Editing;

namespace SaveToolkit.Core.Sessions;

internal sealed record MemberContextResolution(
    IReadOnlyDictionary<string, SemanticMemberContext> Contexts, int OmittedCount, string? Diagnostic,
    long AcceptedBytes = 0);

internal static class SemanticMemberContextResolver
{
    public static MemberContextResolution Resolve(
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, SemanticMemberContext>>? resolver,
        IReadOnlyList<SemanticChange> changes, long usedBytes, long maxBytes,
        CancellationToken cancellationToken)
    {
        if (resolver is null) return new(new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal), 0, null);
        var ids = new List<string>(Math.Min(FieldMetadataLimits.MaxClosureIds, changes.Count));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var omitted = 0;
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.CanonicalValueId is not { } canonical || !seen.Add(canonical)) continue;
            if (ids.Count < FieldMetadataLimits.MaxClosureIds) ids.Add(canonical);
            else omitted++;
        }
        if (ids.Count == 0 && omitted == 0)
            return new(new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal), 0, null);

        var diagnostics = omitted == 0 ? null
            : $"Group member context is bounded: omitted at least {omitted} canonical changed IDs.";
        try
        {
            var contexts = resolver(ids);
            if (contexts.Count > ids.Count)
                return new(new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal),
                    omitted + contexts.Count - ids.Count, "Group member context returned more rows than requested.", 0);
            var requested = ids.ToHashSet(StringComparer.Ordinal);
            var mapped = new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal);
            var malformed = false;
            long acceptedBytes = 0;
            foreach (var (id, context) in contexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!requested.Contains(id) || string.IsNullOrWhiteSpace(context.Role)
                    || string.IsNullOrWhiteSpace(context.RelativePath)
                    || !SemanticContextBudget.TryEstimate(context, out _)
                    || !mapped.TryAdd(id, context))
                {
                    malformed = true;
                    continue;
                }
                var occurrences = changes.Where(change => change.CanonicalValueId == id)
                    .Sum(change => 1 + (change.Aliases?.Count ?? 0));
                if (!SemanticContextBudget.TryEstimate(context, out var contextBytes)
                    || contextBytes > (maxBytes - usedBytes - acceptedBytes) / Math.Max(1, occurrences))
                {
                    mapped.Remove(id);
                    omitted++;
                    malformed = true;
                    continue;
                }
                acceptedBytes += contextBytes * Math.Max(1, occurrences);
            }

            var missing = ids.Count - mapped.Count;
            omitted += missing;
            if (missing > 0 || malformed)
                diagnostics = diagnostics is null
                    ? "Group member context is incomplete: the resolver omitted or returned malformed canonical context."
                    : diagnostics + " The resolver also omitted or returned malformed canonical context.";
            return new(mapped, omitted, diagnostics, acceptedBytes);
        }
        catch (SemanticContextResolutionException ex)
        {
            var accepted = ex.Contexts.Where(pair => ids.Contains(pair.Key, StringComparer.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            return new(accepted, omitted + 1, ex.Message, accepted.Values.Sum(context =>
                SemanticContextBudget.TryEstimate(context, out var bytes) ? bytes : 0));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return new(new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal),
                omitted + ids.Count,
                diagnostics is null
                    ? $"Group member context resolution failed: {ex.Message}"
                    : diagnostics + $" Resolver failure: {ex.Message}");
        }
    }
}
