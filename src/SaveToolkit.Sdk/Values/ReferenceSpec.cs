using System.Text.Json.Serialization;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Sdk.Values;

/// <summary>
/// A catalog-backed choice. The field value is the exact raw id as text, including unknown current
/// ids. Catalog metadata is resolved on demand rather than embedding every option in the view.
/// </summary>
public sealed record ReferenceSpec(string CatalogId) : FieldSpec
{
    /// <summary>Allow new ids absent from the pinned catalog. Known candidates still obey eligibility.</summary>
    public bool AllowCustom { get; init; }

    /// <summary>
    /// Optional document-specific eligibility, evaluated by the host under the document lock.
    /// Keep it cheap, deterministic and free of I/O. The host publishes its result with each candidate
    /// and checks it again at commit. It is executable plugin policy, never supplied by the browser.
    /// </summary>
    [JsonIgnore]
    public Func<ReferenceRecord, ReferenceEligibility>? Assess { get; init; }
}
