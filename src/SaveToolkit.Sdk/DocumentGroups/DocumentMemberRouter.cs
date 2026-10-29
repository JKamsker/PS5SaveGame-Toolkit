namespace SaveToolkit.Sdk;

/// <summary>
/// Resolves group identities and partitions edit batches without executing mutations. Ownership
/// policies must identify exactly one member; overlapping policies are rejected rather than guessed.
/// Recreate this non-owning router whenever the group replaces its member adapters after a restore.
/// </summary>
/// <remarks>The copied member list is fixed. Concurrent resolution is safe only when ownership callbacks are safe;
/// document dispatch and group transactions still require external synchronization.</remarks>
public sealed class DocumentMemberRouter
{
    private readonly DocumentMemberAdapter[] _members;

    /// <summary>Copy the member list and reject duplicate roles using ordinal comparison.</summary>
    /// <exception cref="ArgumentNullException">The member list is null.</exception>
    /// <exception cref="ArgumentException">The list contains null members or duplicate roles.</exception>
    public DocumentMemberRouter(IReadOnlyList<DocumentMemberAdapter> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        _members = members.ToArray();
        if (_members.Any(member => member is null))
            throw new ArgumentException("Group members must be non-null.", nameof(members));
        if (_members.Select(member => member.Role).Distinct(StringComparer.Ordinal).Count() != _members.Length)
            throw new ArgumentException("Group member roles must be unique.", nameof(members));
    }

    /// <summary>Find the unique owning adapter, or throw for unknown or ambiguous identities.</summary>
    /// <exception cref="ArgumentNullException">The identity is null.</exception>
    /// <exception cref="KeyNotFoundException">No member owns the identity.</exception>
    /// <exception cref="InvalidOperationException">Multiple members own the identity.</exception>
    public DocumentMemberAdapter Resolve(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        DocumentMemberAdapter? owner = null;
        foreach (var member in _members)
        {
            if (!member.Owns(id)) continue;
            if (owner is not null) throw new InvalidOperationException("The group identity matches multiple member policies.");
            owner = member;
        }
        return owner ?? throw new KeyNotFoundException("Unknown group member identity.");
    }

    /// <summary>
    /// Resolve every identity before returning any batches. Batches retain group-qualified IDs,
    /// first-seen member order and original per-member edit order, including duplicates. The group
    /// must wrap calls to each batch's Member.ApplyEdits in its existing whole-group transaction.
    /// </summary>
    public IReadOnlyList<DocumentMemberEdits> PartitionEdits(IReadOnlyList<ValueEdit> edits) => edits
        .GroupBy(edit => Resolve(edit.ValueId))
        .Select(group => new DocumentMemberEdits(group.Key, group.ToArray())).ToArray();

    /// <summary>
    /// Resolves metadata for a mixed group request after validating every owner. Member calls use
    /// the current adapters, so a restored candidate never retains setters or labels from a prior
    /// publication. Results are returned in request order and remain limited to requested IDs.
    /// </summary>
    public IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        ArgumentNullException.ThrowIfNull(valueIds);
        var groups = valueIds.Select((id, index) => (id, index, member: Resolve(id)))
            .GroupBy(item => item.member);
        var resolved = new Dictionary<string, FieldMetadata>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var requested = group.Select(item => item.id).ToHashSet(StringComparer.Ordinal);
            var fields = group.Key.ResolveFieldMetadata(group.Select(item => item.id).ToArray());
            if (fields.Count > requested.Count)
                throw new InvalidOperationException("A group member returned too many metadata descriptors.");
            var memberIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (!requested.Contains(field.ValueId) || !memberIds.Add(field.ValueId))
                    throw new InvalidOperationException("A group member returned foreign or duplicate metadata.");
                if (!resolved.TryAdd(field.ValueId, field))
                    throw new InvalidOperationException("Group members returned duplicate metadata.");
            }
        }
        return valueIds.Distinct(StringComparer.Ordinal)
            .Where(resolved.ContainsKey)
            .Select(id => resolved[id])
            .ToArray();
    }
}

/// <summary>One member's ordered edits, still expressed in the group namespace.</summary>
/// <param name="Member">Non-owning adapter to dispatch this batch through.</param>
/// <param name="Edits">Ordered group-qualified edits; callers must not mutate the list during dispatch.</param>
public sealed record DocumentMemberEdits(DocumentMemberAdapter Member, IReadOnlyList<ValueEdit> Edits);
