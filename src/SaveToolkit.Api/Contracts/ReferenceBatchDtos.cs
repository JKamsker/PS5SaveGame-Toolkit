using SaveToolkit.Sdk.References;

namespace SaveToolkit.Api.Contracts;

/// <summary>Field identity includes the owning action for action parameters.</summary>
public sealed record ReferenceRecordPair(string FieldId, string RecordKey, string? ActionId = null);

public sealed record ReferenceRecordsRequest(IReadOnlyList<ReferenceRecordPair> Pairs, long? Revision = null)
{
    public const int MaxPairs = 256;
}

/// <summary>One ordered result per input, including duplicates and missing fields.</summary>
public sealed record ReferenceRecordResult(string FieldId, string RecordKey, string? ActionId,
    int Status, ReferenceCandidate? Candidate = null, string? Error = null);

public sealed record ReferenceRecordsResponse(long Revision, IReadOnlyList<ReferenceRecordResult> Results);
