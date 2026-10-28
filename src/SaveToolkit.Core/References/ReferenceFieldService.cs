using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.References;

/// <summary>Shared catalog resolution and policy for picker queries and the edit trust boundary.</summary>
public sealed class ReferenceFieldService(ReferenceContext context)
{
    public FieldValueCoercer.Result Validate(FieldValue raw, ReferenceSpec spec)
    {
        if (raw.Kind != FieldValueKind.Text)
        {
            return new(FieldValue.Null, "Expected a raw reference id as text.");
        }

        var candidate = Find(spec, raw.AsString());
        return candidate.Eligibility.Selectable
            ? new(raw, null)
            : new(FieldValue.Null, string.Join(" ", candidate.Eligibility.Reasons));
    }

    public ReferenceCandidate Find(ReferenceSpec spec, string rawId)
    {
        var record = context.CatalogForHost(spec.CatalogId)?.Find(rawId);
        if (record is null)
        {
            return new ReferenceCandidate(rawId)
            {
                Eligibility = new ReferenceEligibility
                {
                    Eligible = spec.AllowCustom,
                    Reasons = ["Id is absent from the pinned catalog."],
                },
            };
        }

        var eligibility = spec.Assess?.Invoke(record) ?? new ReferenceEligibility();
        if (!eligibility.Selectable && eligibility.Reasons.Count == 0)
        {
            eligibility = eligibility with { Reasons = ["This candidate is unavailable or ineligible for this field."] };
        }

        return new ReferenceCandidate(rawId)
        {
            Label = record.Name,
            Description = record.Description,
            Category = record.Category,
            AssetId = record.AssetId,
            Source = record.Source,
            Dataset = record.Dataset,
            AssetDataset = record.AssetId is { } asset ? context.AssetProvenance(asset) : null,
            Eligibility = eligibility,
        };
    }

    public ReferenceCandidatePage Search(ReferenceSpec spec, ReferenceQuery query, long revision)
    {
        query = query with { Offset = Math.Max(0, query.Offset), Limit = Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit) };
        var catalog = context.CatalogForHost(spec.CatalogId);
        var page = catalog?.Search(query);
        return new ReferenceCandidatePage(spec.CatalogId,
            page is null ? [] : [.. page.Items.Select(r => Find(spec, r.Key))],
            page?.Total ?? 0, query.Offset, query.Limit, catalog?.Categories ?? [], revision)
        {
            SortKeys = catalog?.SortKeys ?? [],
        };
    }
}
