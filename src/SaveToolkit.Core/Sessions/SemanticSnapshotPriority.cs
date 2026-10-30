namespace SaveToolkit.Core.Sessions;

/// <summary>Reclaims optional evidence on a staged snapshot, retaining honest partial coverage.</summary>
internal static class SemanticSnapshotPriority
{
    public static bool MakeRoom(SemanticSnapshot snapshot, long requiredBytes, long maxBytes,
        Func<SemanticField, bool> optional, CancellationToken cancellationToken)
    {
        if (requiredBytes <= maxBytes - snapshot.Bytes) return true;
        var candidates = snapshot.Fields.Values.Where(optional)
            .Select(field => (field.ValueId, Cost: SemanticSnapshot.Estimate(field)))
            .OrderByDescending(field => field.Cost).ToArray();
        var required = requiredBytes - (maxBytes - snapshot.Bytes);
        long available = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            available += candidate.Cost;
            if (available >= required) break;
        }
        if (available < required) return false;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Fields.Remove(candidate.ValueId);
            snapshot.Bytes -= candidate.Cost;
            snapshot.OmittedCount++;
            snapshot.Complete = false;
            if (requiredBytes <= maxBytes - snapshot.Bytes) break;
        }
        const string warning = "Optional legacy evidence was omitted to retain explicitly affected fields and charged member context within the review budget.";
        if (!snapshot.QueryWarnings.Contains(warning, StringComparer.Ordinal))
            snapshot.QueryWarnings = [.. snapshot.QueryWarnings, warning];
        return true;
    }
}
