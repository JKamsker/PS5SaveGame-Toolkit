using Microsoft.AspNetCore.Mvc;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.LocalApi.Protocol;
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
            ReferenceOperations.Candidates(d.Value, valueId, q, category, offset, limit, revision, actionId, sortBy, descending));

        doc.MapGet("/reference-fields/record", (DocumentRef d, [FromQuery] string valueId,
            [FromQuery] string rawId, [FromQuery] long? revision, [FromQuery] string? actionId) =>
            d.Value.FindReference(valueId, rawId, revision, actionId));

        doc.MapPost("/reference-fields/records", (DocumentRef d, ReferenceRecordsRequest request) =>
            ReferenceOperations.Records(d.Value, request))
            .WithApiErrors(Status413PayloadTooLarge, Status415UnsupportedMediaType);
    }
}
