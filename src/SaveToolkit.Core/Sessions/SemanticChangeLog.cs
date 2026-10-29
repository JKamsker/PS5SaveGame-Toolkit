using System.Text.Json.Serialization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>A current F8 page to load before locating the stable row and change's value ID.</summary>
public sealed record SemanticCollectionTarget(string CollectionId, string RowKey, QueryableCollectionQuery Query);

/// <summary>Presentation-only identity for an explicitly published projection of a canonical field.</summary>
public sealed record SemanticProjection(string ValueId, string Label, IReadOnlyList<string> Path,
    FieldBindingKind? BindingKind = null, FieldPresentation? Presentation = null);

/// <summary>Server-owned navigation state. A missing target remains reviewable and is never fabricated.</summary>
public sealed record SemanticNavigationTarget(IReadOnlyList<string> Path, SemanticCollectionTarget? Collection = null,
    bool Available = true, string? UnavailableReason = null);

/// <summary>Optional group provenance published by the host for a reviewed member.</summary>
public sealed record SemanticMemberContext(string Role, string RelativePath, string? GroupTitle = null,
    string? Provenance = null);

/// <summary>Bounded evidence for the portion of a document that was examined.</summary>
public sealed record SemanticReviewCoverage(long ExaminedCount, long OmittedCount, long BudgetBytes, long UsedBytes,
    IReadOnlyList<string> OmittedReasons)
{
    public static SemanticReviewCoverage Empty { get; } = new(0, 0, 0, 0, []);
}

public sealed record SemanticChange(string ValueId, string Label, IReadOnlyList<string> Path,
    FieldValue OriginalValue, FieldValue CurrentValue, bool OriginalExists, bool CurrentExists, bool CanRevert)
{
    /// <summary>Present only for a field reached through a current queryable row; use the log's revision.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SemanticCollectionTarget? Target { get; init; }

    /// <summary>Effective canonical identity, emitted only when the server has explicit metadata.</summary>
    [JsonPropertyName("canonicalValueId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CanonicalValueId { get; init; }

    /// <summary>Compatibility name for callers that distinguish effective identity from raw identity.</summary>
    [JsonIgnore]
    public string? EffectiveCanonicalValueId => CanonicalValueId;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FieldBindingKind? BindingKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FieldPresentation? Presentation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SemanticProjection>? Aliases { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SemanticMemberContext? Member { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SemanticNavigationTarget? NavigationTarget { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanNavigate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RevertReason { get; init; }
}

public sealed record SemanticChangeLog(IReadOnlyList<SemanticChange> Changes, bool Complete,
    IReadOnlyList<string> Warnings, long Revision)
{
    /// <summary>Present for metadata-backed or incomplete reviews; null preserves the legacy wire.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SemanticReviewCoverage? Coverage { get; init; }
}
