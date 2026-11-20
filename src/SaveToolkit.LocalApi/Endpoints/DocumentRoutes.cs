using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Sessions;
using SaveToolkit.LocalApi.Http;
using SaveToolkit.LocalApi.Protocol;
using SaveToolkit.LocalApi.Routing;
using static SaveToolkit.LocalApi.Endpoints.EndpointFilters;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>Document view, editing, history and binary routes (ASP.NET: WorkspaceEndpoints, ActionReviewEndpoints).</summary>
internal static class DocumentRoutes
{
    public const string Document = "/api/workspaces/{workspaceId}/documents/{documentId}";

    public static void Map(RouteTable routes)
    {
        routes.Map("POST", Document + "/actions/plan", c => Json<PlanActionRequest>(c, "PlanActionRequest", (d, r) =>
        {
            var preview = d.Value.PlanAction(r.ActionId, r.Parameters ?? [], r.Scope, r.Revision, c.Aborted);
            return preview.Stale ? ApiOutcome.Conflict(preview)
                : preview.Plan.Errors.Count > 0 ? ApiOutcome.Unprocessable(preview) : ApiOutcome.Ok(preview);
        }), json: true);
        routes.Map("POST", Document + "/actions/apply", c => Json<ApplyActionPlanRequest>(c, "ApplyActionPlanRequest",
            (d, r) => ApiOutcome.ForEdit(d.Value.ApplyActionPlan(r.PlanId, r.Revision, c.Aborted))), json: true);
        routes.Map("GET", Document + "/changes", c => Get(c, d => d.Value.GetChanges(c.Aborted)));
        routes.Map("POST", Document + "/changes/revert", c => Json<RevertChangeRequest>(c, "RevertChangeRequest",
            (d, r) => ApiOutcome.ForEdit(d.Value.RevertChange(r.ValueId, r.Revision, c.Aborted))), json: true);

        routes.Map("GET", Document, c => Get(c, d => d.Value.ToDto(d.Workspace)));
        routes.Map("DELETE", Document, c => Get(c, d => d.Workspace.CloseClean(d.Value.Id) ? new EmptyResult(204) : new EmptyResult(404)));
        routes.Map("GET", Document + "/nodes", c =>
        {
            var d = c.Document();
            var id = c.QueryString("id");
            return c.RunAsync([], d, () => Sync(() => d.Value.Expand(id)));
        });
        // Opaque IDs may contain slashes, which stay %2F in route parameters; the query form avoids that.
        routes.Map("GET", Document + "/collections", c =>
        {
            var d = c.Document();
            var collectionId = c.QueryString("collectionId");
            return c.RunAsync([], d, () => Sync(() => QueryCollection(c, d, collectionId)));
        });
        routes.Map("GET", Document + "/collections/{collectionId}", c =>
        {
            var d = c.Document();
            var collectionId = c.Route("collectionId");
            return c.RunAsync([], d, () => Sync(() => QueryCollection(c, d, collectionId)));
        });
        routes.Map("POST", Document + "/edits", c => Json<EditRequest>(c, "EditRequest", (d, r) =>
            ApiOutcome.ForEdit(d.Value.ApplyEdits([.. r.Edits.Select(e => new IncomingEdit(e.ValueId, e.Value))], r.Revision, c.Aborted))), json: true);
        routes.Map("POST", Document + "/actions", c => Json<ActionRequest>(c, "ActionRequest", (d, r) =>
            ApiOutcome.ForEdit(d.Value.InvokeAction(r.ActionId, r.Parameters ?? [], r.Revision, c.Aborted))), json: true);
        routes.Map("POST", Document + "/undo", c => History(c, d => d.Value.Undo()), json: true);
        routes.Map("POST", Document + "/redo", c => History(c, d => d.Value.Redo()), json: true);
        routes.Map("GET", Document + "/assets", c =>
        {
            var d = c.Document();
            var id = c.QueryString("id");
            return c.RunAsync([], d, () => Sync(() =>
                d.Value.GetAsset(id) is { } asset ? new BytesResult(200, asset.Content, asset.ContentType) : new EmptyResult(404)));
        });
        routes.Map("GET", Document + "/blobs", c =>
        {
            var d = c.Document();
            var id = c.QueryString("id");
            var offset = c.QueryLong("offset");
            var count = c.QueryInt("count");
            return c.RunAsync([], d, () => Sync(() => offset < 0
                ? new ApiOutcome(400, new ErrorDto("Offset cannot be negative."))
                : new BytesResult(200, d.Value.ReadBlob(id, offset, Math.Clamp(count, 0, ApiLimits.MaxBlobPage)), "application/octet-stream")));
        });
        routes.Map("GET", Document + "/download", c =>
        {
            var d = c.Document();
            return c.RunAsync([], d, async () =>
            {
                var session = d.Value;
                var own = DocumentDownloader.Capture(d.Workspace, session, c.Aborted);
                var content = own is null ? await WorkspaceRoutes.ReadAllAsync(session.File.OpenRead(), c.Aborted) : own.Content;
                return new BytesResult(200, content, "application/octet-stream", session.File.FileName);
            });
        });
    }

    /// <summary>A document route whose only input is the bound document.</summary>
    internal static ValueTask<LocalResult> Get(EndpointContext c, Func<DocumentRef, object?> handler)
    {
        var d = c.Document();
        return c.RunAsync([], d, () => Sync(() => handler(d)));
    }

    /// <summary>A document route with a required, inferred JSON body.</summary>
    internal static ValueTask<LocalResult> Json<T>(EndpointContext c, string type, Func<DocumentRef, T, object?> handler) where T : class
    {
        var d = c.Document();
        var request = c.Body<T>(type, "request")!;
        return c.RunAsync([request], d, () => Sync(() => handler(d, request)));
    }

    private static ValueTask<LocalResult> History(EndpointContext c, Func<DocumentRef, SessionEditResult> operation)
    {
        var d = c.Document();
        var request = c.Body<HistoryRequest>("HistoryRequest", "request", optional: true);
        return c.RunAsync([request], d, () => Sync(() => ApiOutcome.ForEdit(operation(d))));
    }

    private static object QueryCollection(EndpointContext c, DocumentRef document, string collectionId)
    {
        var (query, revision) = CollectionQueryBinder.Bind(c.Http.Query);
        try
        {
            return document.Value.QueryCollection(collectionId, query, revision, page =>
                CollectionPageSerializer.TrySerialize(QueryablePageDto.From(page)) is { } content
                    ? new BytesResult(200, content, JsonResult.PlainJson)
                    : throw new ApiStatusException(CollectionPageSerializer.LimitMessage, 413), c.Aborted);
        }
        catch (ArgumentException ex)
        {
            return new ApiOutcome(400, new ErrorDto(ex.Message));
        }
    }
}
