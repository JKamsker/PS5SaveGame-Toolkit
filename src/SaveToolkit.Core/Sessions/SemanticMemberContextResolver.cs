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
        var requested = ids.ToHashSet(StringComparer.Ordinal);
        var occurrences = changes.GroupBy(change => change.CanonicalValueId,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key!, group => Math.Max(1,
                group.Sum(change => 1 + (change.Aliases?.Count ?? 0))), StringComparer.Ordinal);
        var mapped = new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal);
        var omittedIds = new HashSet<string>(StringComparer.Ordinal);
        var malformed = false;
        long acceptedBytes = 0;
        try
        {
            var contexts = resolver(ids);
            Admit(contexts);
        }
        catch (SemanticContextResolutionException ex)
        {
            malformed = true;
            diagnostics = diagnostics is null ? ex.Message : diagnostics + " " + ex.Message;
            Admit(ex.Contexts);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            malformed = true;
            diagnostics = diagnostics is null
                ? $"Group member context resolution failed: {ex.Message}"
                : diagnostics + $" Resolver failure: {ex.Message}";
        }

        foreach (var id in ids)
            if (!mapped.ContainsKey(id)) omittedIds.Add(id);
        omitted += omittedIds.Count;
        if (omitted > 0 || malformed)
            diagnostics = diagnostics is null
                ? "Group member context is incomplete: the resolver omitted or returned malformed canonical context."
                : diagnostics + " Group member context was retained only within the review budget.";
        return new(mapped, omitted, diagnostics, acceptedBytes);

        void Admit(IEnumerable<KeyValuePair<string, SemanticMemberContext>> values)
        {
            var entries = 0;
            foreach (var (id, context) in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > ids.Count)
                {
                    malformed = true;
                    break;
                }
                if (!requested.Contains(id) || !mapped.TryAdd(id, context))
                {
                    malformed = true;
                    if (requested.Contains(id)) omittedIds.Add(id);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(context.Role)
                    || string.IsNullOrWhiteSpace(context.RelativePath)
                    || context.Role.Length > SemanticContextBudget.MaxStringLength
                    || context.RelativePath.Length > SemanticContextBudget.MaxStringLength
                    || context.Provenance is { Length: > SemanticContextBudget.MaxStringLength }
                    || !SemanticContextBudget.TryEstimate(context, out var contextBytes))
                {
                    mapped.Remove(id);
                    omittedIds.Add(id);
                    malformed = true;
                    continue;
                }
                var multiplier = occurrences.GetValueOrDefault(id, 1);
                var remaining = maxBytes - usedBytes - acceptedBytes;
                if (contextBytes > remaining / multiplier)
                {
                    mapped.Remove(id);
                    omittedIds.Add(id);
                    malformed = true;
                    continue;
                }
                acceptedBytes += checked(contextBytes * multiplier);
            }
        }
    }
}
