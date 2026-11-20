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
