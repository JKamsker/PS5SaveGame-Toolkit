using System.Text.Json.Serialization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>A current F8 page to load before locating the stable row and change's value ID.</summary>
public sealed record SemanticCollectionTarget(string CollectionId, string RowKey, QueryableCollectionQuery Query);

public sealed record SemanticChange(string ValueId, string Label, IReadOnlyList<string> Path,
    FieldValue OriginalValue, FieldValue CurrentValue, bool OriginalExists, bool CurrentExists, bool CanRevert)
{
    /// <summary>Present only for a field reached through a current queryable row; use the log's revision.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SemanticCollectionTarget? Target { get; init; }
}

public sealed record SemanticChangeLog(IReadOnlyList<SemanticChange> Changes, bool Complete,
    IReadOnlyList<string> Warnings, long Revision);
