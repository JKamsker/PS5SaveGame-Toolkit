using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Sdk;

/// <summary>Optional plugin capability. Proposals never acquire ownership or change documents.</summary>
public interface IDocumentGroupPlugin
{
    /// <summary>
    /// Propose explicit alternatives containing the anchor and all required partners. Never select a
    /// different generation implicitly. Return Errors for missing, unsupported or ambiguous partners.
    /// </summary>
    IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor);

    /// <summary>
    /// Validate roles, formats, relationships and capacities against one immutable input set.
    /// Called before opening and against the complete candidate output before publication/export.
    /// Must not mutate inputs or do I/O outside the supplied save/reference interfaces.
    /// </summary>
    IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references);

    /// <summary>Open a fresh, independent group model using the pinned immutable original inputs.</summary>
    IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references);
}

public sealed record DocumentGroupMember(string Role, string RelativePath);

public sealed record DocumentGroupProposal(string Id, string Title, IReadOnlyList<DocumentGroupMember> Members)
{
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
}

public sealed record DocumentGroupFile(string Role, ISaveFile File);

/// <summary>Host-owned read-only selection. All members share a title and imported generation.</summary>
public sealed record DocumentGroupInput(IReadOnlyList<DocumentGroupFile> Members);

/// <summary>
/// One coordinated view/model for explicitly selected files. Field, row, action, collection and lazy
/// ids must be unique across the group (role prefixes work well). Member models may be composed here.
/// The host wraps this model in an ordinary document session for history, review and query handling.
/// </summary>
public interface IGroupedSaveDocument : ISaveDocument
{
    /// <summary>
    /// Capture the entire group, including unpersisted stable duplicate identities and allocation
    /// counters and any cursor state needed to rebind previously queried pages. Tokens must be
    /// immutable, reusable and accepted by a fresh OpenGroup instance's
    /// RestoreMemento. Report the complete retained size honestly. No live objects may be shared.
    /// </summary>
    DocumentMemento CaptureMemento();
}
