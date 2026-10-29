using System.Text.Json.Serialization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

/// <summary>How an action-owned consequence is restored by the host.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionEffectKind>))]
public enum ActionEffectKind
{
    /// <summary>An existing logical observation with no independent setter.</summary>
    Observation,
    /// <summary>An action-owned structural consequence restored through one whole-action memento.</summary>
    StructuralMemento,
}

/// <summary>
/// Read-only metadata for a consequence that is not a parsed canonical field. The enclosing
/// <see cref="PlannedChange"/> owns identity, labels and typed before/after values.
/// </summary>
public sealed record ActionEffectDescriptor(ActionEffectKind Kind, FieldSpec Spec)
{
    /// <summary>Optional presentation evidence for the action-owned row.</summary>
    public FieldPresentation? Presentation { get; init; }
    /// <summary>Optional document-relative navigation path for the action-owned row.</summary>
    public IReadOnlyList<string>? Path { get; init; }
    /// <summary>Optional bounded provenance supplied by the provider.</summary>
    public string? Source { get; init; }

    internal ActionEffectDescriptor Snapshot()
    {
        if (!Enum.IsDefined(Kind)) throw new InvalidOperationException("Unknown action effect kind.");
        ArgumentNullException.ThrowIfNull(Spec);
        return this with
        {
            Spec = FieldSpecMetadata.Snapshot(Spec),
            Presentation = Presentation is null ? null : Presentation with { },
            Path = Path is null ? null : Array.AsReadOnly(Path.ToArray()),
        };
    }
}
