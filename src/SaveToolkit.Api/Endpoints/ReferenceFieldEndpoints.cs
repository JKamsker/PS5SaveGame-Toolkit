using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Api.Endpoints;

/// <summary>F2 catalog queries enriched with a published field's document-specific policy.</summary>
internal static class ReferenceFieldEndpoints
{
    public static void MapReferenceFieldApi(this RouteGroupBuilder doc)
    {
        doc.MapGet("/reference-fields/candidates", (DocumentRef d, [FromQuery] string valueId,
            [FromQuery] string? q, [FromQuery] string? category, [FromQuery] int? offset,
            [FromQuery] int? limit, [FromQuery] long? revision, [FromQuery] string? actionId,
            [FromQuery] ReferenceSortKey? sortBy, [FromQuery] bool? descending) =>
        {
            ReferenceSortBinding.Validate(sortBy);
            return d.Value.SearchReferences(valueId, new ReferenceQuery
            {
                Text = q,
                Category = string.IsNullOrEmpty(category) ? null : category,
                SortBy = sortBy,
                Descending = descending ?? false,
                Offset = offset ?? 0,
                Limit = limit ?? ReferenceQuery.DefaultLimit,
            }, revision, actionId);
        });

        doc.MapGet("/reference-fields/record", (DocumentRef d, [FromQuery] string valueId,
            [FromQuery] string rawId, [FromQuery] long? revision, [FromQuery] string? actionId) =>
            d.Value.FindReference(valueId, rawId, revision, actionId));
    }
}

internal static class ReferenceSortBinding
{
    public static void Validate(ReferenceSortKey? key)
    {
        if (key is { } value && !Enum.IsDefined(value))
        {
            throw new BadHttpRequestException("Unknown reference sort key.");
        }
    }
}
