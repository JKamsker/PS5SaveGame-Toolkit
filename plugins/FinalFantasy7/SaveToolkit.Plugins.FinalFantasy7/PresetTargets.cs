using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

// Plugin-private semantic data. Host storage, envelopes and JSON transport belong to SDK-07.
internal enum PresetScopeKind { Document, AllEligible, AllCopies, Selected }
internal enum PresetIdentityDomain { ItemType, MateriaInstance, CharacterIndex, MateriaType }
internal enum PresetTargetStatus { Matched, Missing, Ambiguous, Unresolved }
internal enum PresetDetailStatus { Unique, MissingDetail, DuplicateDetail, DuplicateInventory, InvalidOwnedCount, TypeMismatch }

internal sealed record PresetTarget(PresetIdentityDomain Domain, uint Id, uint? TypeId = null,
    bool AllMatches = false)
{
    public string? Source { get; init; }
}

internal sealed record PresetCandidate(string RowKey, int? Slot, uint TypeId, uint? InstanceId,
    PresetDetailStatus DetailStatus, string Source, int? OwnerIndex = null)
{
    public IReadOnlyList<int> OwnedInventorySlots { get; init; } = [];
    public uint? DetailTypeId { get; init; }
    public bool Resolved => DetailStatus == PresetDetailStatus.Unique;
    public string Reason => DetailStatus switch
    {
        PresetDetailStatus.Unique => "unique",
        PresetDetailStatus.MissingDetail => "missing-detail",
        PresetDetailStatus.DuplicateDetail => "duplicate-detail",
        PresetDetailStatus.DuplicateInventory => "duplicate-inventory",
        PresetDetailStatus.InvalidOwnedCount => "invalid-owned-count",
        PresetDetailStatus.TypeMismatch => "detail-type-mismatch",
        _ => throw new InvalidOperationException("Unknown owned-detail status."),
    };
}

internal sealed record PresetTargetResolution(PresetTarget Target, PresetTargetStatus Status,
    string Reason, IReadOnlyList<PresetCandidate> Candidates, IReadOnlyList<string> RowKeys);

internal sealed record PresetResolution(IReadOnlyList<PresetTargetResolution> Targets, ActionScope Scope)
{
    public bool NeedsChoice => Targets.Any(target => target.Status == PresetTargetStatus.Ambiguous);
}

internal sealed record Ff7ActionPreset(string ActionId, IReadOnlyDictionary<string, FieldValue> Parameters,
    PresetScopeKind ScopeKind, IReadOnlyList<PresetTarget> Targets, string TitleId, Ff7Variant Variant);
