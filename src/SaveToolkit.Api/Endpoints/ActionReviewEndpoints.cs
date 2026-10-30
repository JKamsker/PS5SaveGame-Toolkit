using System.Text.Json;
using System.Text.Json.Serialization;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class ActionReviewEndpoints
{
    public static void MapActionReview(this RouteGroupBuilder doc)
    {
        doc.MapPost("/actions/plan", (DocumentRef d, PlanActionRequest request, CancellationToken ct) =>
        {
            var preview = d.Value.PlanAction(request.ActionId, request.Parameters ?? [], request.Scope, request.Revision, ct);
            return preview.Stale ? Results.Conflict(preview)
                : preview.Plan.Errors.Count > 0 ? Results.UnprocessableEntity(preview) : Results.Ok(preview);
        }).Produces<ActionPreview>().WithApiErrors(Status415UnsupportedMediaType);
        doc.MapPost("/actions/apply", (DocumentRef d, ApplyActionPlanRequest request, CancellationToken ct) =>
            SessionEditResponse.Create(d.Value.ApplyActionPlan(request.PlanId, request.Revision, ct)))
            .Produces<SessionEditResult>().WithApiErrors(Status415UnsupportedMediaType);
        doc.MapGet("/changes", (DocumentRef d, CancellationToken ct) => d.Value.GetChanges(ct))
            .Produces<SemanticChangeLog>();
        doc.MapPost("/changes/revert", (DocumentRef d, RevertChangeRequest request, CancellationToken ct) =>
            SessionEditResponse.Create(d.Value.RevertChange(request.ValueId, request.Revision, ct)))
            .Produces<SessionEditResult>().WithApiErrors(Status415UnsupportedMediaType);
    }
}

/// <summary>
/// HTTP action-planning request. Omitting or sending null for Scope selects document scope; the
/// response contains the normalized scope used for planning.
/// </summary>
public sealed record PlanActionRequest(string ActionId, Dictionary<string, JsonElement>? Parameters,
    ActionScope? Scope = null, long? Revision = null);
public sealed record ApplyActionPlanRequest(string PlanId, [property: JsonRequired] long Revision);
public sealed record RevertChangeRequest(string ValueId, [property: JsonRequired] long Revision);
