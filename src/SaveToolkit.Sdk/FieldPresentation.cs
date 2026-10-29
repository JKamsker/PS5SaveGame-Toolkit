using SaveToolkit.Sdk.Views;
using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk;

/// <summary>
/// Complete canonical field annotations for presentation without loading its view. An empty instance
/// explicitly declares no annotations; null FieldMetadata.Presentation means annotations are unspecified.
/// Does not grant write permission or replace the canonical spec or binding kind.
/// </summary>
public sealed record FieldPresentation
{
    /// <summary>Canonical evidence label. Null retains the canonical field's absence of a label.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Confidence>))]
    public Confidence? Confidence { get; init; }

    /// <summary>Canonical caution that must accompany every editor, including unloaded projections.</summary>
    public string? Warning { get; init; }

    /// <summary>Canonical explanation or attribution, before any contextual reference description.</summary>
    public string? Description { get; init; }
}
