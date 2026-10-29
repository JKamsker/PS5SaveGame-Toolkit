using System.Text.Json;
using System.Text.Json.Serialization;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;

namespace SaveToolkit.Api.Endpoints;

internal static class ActionReviewEndpoints
{
    public static void MapActionReview(this RouteGroupBuilder doc)
    {
        doc.MapPost("/actions/plan", (DocumentRef d, PlanActionRequest request) =>
        {
            var preview = d.Value.PlanAction(request.ActionId, request.Parameters ?? [], request.Scope, request.Revision);
            return preview.Stale ? Results.Conflict(preview)
                : preview.Plan.Errors.Count > 0 ? Results.UnprocessableEntity(preview) : Results.Ok(preview);
        });
        doc.MapPost("/actions/apply", (DocumentRef d, ApplyActionPlanRequest request) =>
            Result(d.Value.ApplyActionPlan(request.PlanId, request.Revision)));
        doc.MapGet("/changes", (DocumentRef d) => d.Value.GetChanges());
        doc.MapPost("/changes/revert", (DocumentRef d, RevertChangeRequest request) =>
            Result(d.Value.RevertChange(request.ValueId, request.Revision)));
    }

    private static IResult Result(SessionEditResult result) => result.Ok ? Results.Ok(result)
        : result.Stale ? Results.Conflict(result) : Results.UnprocessableEntity(result);
}

/// <summary>
/// HTTP action-planning request. Omitting or sending null for Scope selects document scope; the
/// response contains the normalized scope used for planning.
/// </summary>
public sealed record PlanActionRequest(string ActionId, Dictionary<string, JsonElement>? Parameters,
    ActionScope? Scope = null, long? Revision = null);
public sealed record ApplyActionPlanRequest(string PlanId, [property: JsonRequired] long Revision);
public sealed record RevertChangeRequest(string ValueId, [property: JsonRequired] long Revision);
