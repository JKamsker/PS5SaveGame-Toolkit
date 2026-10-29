using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.Views;

/// <summary>
/// Another presentation of a canonical field, never a new binding or catalog reference.
/// The host resolves the target through loaded fields or targeted IFieldMetadataDocument discovery.
/// Available targets are sent as ordinary FieldNodes; unresolved targets remain unavailable references.
/// </summary>
/// <param name="TargetValueId">Direct canonical value ID. References cannot target other presentation nodes.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FieldReferenceNode(
    [property: StringLength(FieldReferenceNode.MaxTargetIdLength, MinimumLength = 1)] string TargetValueId) : ViewNode, IJsonOnDeserialized
{
    /// <summary>Maximum target ID length in characters.</summary>
    public const int MaxTargetIdLength = 4096;

    /// <summary>Maximum length of either optional presentation string in characters.</summary>
    public const int MaxPresentationLength = 4096;

    /// <summary>Optional contextual label. Canonical spec, confidence and warning are retained.</summary>
    [StringLength(MaxPresentationLength, MinimumLength = 1)]
    public string? Label { get; init; }

    /// <summary>Optional contextual explanation, appended to any canonical explanation.</summary>
    [StringLength(MaxPresentationLength)]
    public string? Description { get; init; }

    /// <summary>Reject malformed identities and oversized presentation data without resolving a target.</summary>
    /// <exception cref="InvalidOperationException">The reference exceeds its protocol bounds.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TargetValueId) || TargetValueId.Length > MaxTargetIdLength
            || Label is { Length: > MaxPresentationLength } || Label is not null && string.IsNullOrWhiteSpace(Label)
            || Description is { Length: > MaxPresentationLength })
            throw new InvalidOperationException("Invalid field reference identity or presentation bounds.");
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        try { Validate(); }
        catch (InvalidOperationException exception) { throw new JsonException(exception.Message, exception); }
    }
}
