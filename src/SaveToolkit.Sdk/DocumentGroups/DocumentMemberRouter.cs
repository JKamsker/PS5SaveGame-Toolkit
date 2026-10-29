namespace SaveToolkit.Sdk;

/// <summary>
/// Resolves group identities and partitions edit batches without executing mutations. Ownership
/// policies must identify exactly one member; overlapping policies are rejected rather than guessed.
/// Recreate this non-owning router whenever the group replaces its member adapters after a restore.
/// </summary>
public sealed class DocumentMemberRouter
{
    private readonly DocumentMemberAdapter[] _members;

    /// <summary>Copy the member list and reject duplicate roles using ordinal comparison.</summary>
    public DocumentMemberRouter(IReadOnlyList<DocumentMemberAdapter> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        _members = members.ToArray();
        if (_members.Select(member => member.Role).Distinct(StringComparer.Ordinal).Count() != _members.Length)
            throw new ArgumentException("Group member roles must be unique.", nameof(members));
    }

    /// <summary>Find the unique owning adapter, or throw for unknown or ambiguous identities.</summary>
    public DocumentMemberAdapter Resolve(string id)
    {
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
}

/// <summary>One member's ordered edits, still expressed in the group namespace.</summary>
public sealed record DocumentMemberEdits(DocumentMemberAdapter Member, IReadOnlyList<ValueEdit> Edits);
