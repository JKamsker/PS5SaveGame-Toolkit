using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.References;
using static Microsoft.AspNetCore.Http.StatusCodes;

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

        doc.MapPost("/reference-fields/records", (DocumentRef d, ReferenceRecordsRequest request) =>
        {
            long revision = d.Value.State.Revision;
            if (request.Revision is { } expected && expected != revision) throw new StaleViewException();
            var results = new List<ReferenceRecordResult>(request.Pairs.Count);
            foreach (var pair in request.Pairs)
            {
                try
                {
                    // Always resolve the published field, even when another pair has the same key.
                    // The document HTTP gate holds across the whole batch; the captured revision
                    // also rejects interleaving mutations by non-HTTP Core callers.
                    var candidate = d.Value.FindReference(pair.FieldId, pair.RecordKey, revision, pair.ActionId);
                    results.Add(new(pair.FieldId, pair.RecordKey, pair.ActionId, StatusCodes.Status200OK, candidate));
                }
                catch (KeyNotFoundException error)
                {
                    results.Add(new(pair.FieldId, pair.RecordKey, pair.ActionId, StatusCodes.Status404NotFound, Error: error.Message));
                }
            }
            return new ReferenceRecordsResponse(revision, results);
        }).WithApiErrors(Status413PayloadTooLarge, Status415UnsupportedMediaType);
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
