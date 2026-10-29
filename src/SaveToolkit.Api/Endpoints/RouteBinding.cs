using SaveToolkit.Core.Sessions;

namespace SaveToolkit.Api.Endpoints;

/// <summary>Resolves <c>{workspaceId}</c> from the route; unknown ids become 404 via the error handler.</summary>
internal sealed record WorkspaceRef(Workspace Value)
{
    private static readonly object LeaseKey = new();

    public static ValueTask<WorkspaceRef?> BindAsync(HttpContext context) =>
        ValueTask.FromResult<WorkspaceRef?>(new WorkspaceRef(Resolve(context)));

    internal static Workspace Resolve(HttpContext context)
    {
        if (context.Items[LeaseKey] is WorkspaceLease existing) return existing.Value;
        var id = context.GetRouteValue("workspaceId") as string ?? throw new KeyNotFoundException("Workspace not found.");
        var store = context.RequestServices.GetRequiredService<WorkspaceStore>();
        return Retain(context, store, id);
    }

    internal static Workspace Retain(HttpContext context, WorkspaceStore store, string id)
    {
        var lease = store.Acquire(id) ?? throw new KeyNotFoundException("Workspace not found. It may have expired; import the save again.");
        context.Items[LeaseKey] = lease;
        context.Response.RegisterForDispose(lease);
        return lease.Value;
    }
}

/// <summary>Resolves <c>{workspaceId}/documents/{documentId}</c>.</summary>
internal sealed record DocumentRef(Workspace Workspace, DocumentSession Value)
{
    public static ValueTask<DocumentRef?> BindAsync(HttpContext context)
    {
        var workspace = WorkspaceRef.Resolve(context);
        var id = context.GetRouteValue("documentId") as string ?? throw new KeyNotFoundException("Document not found.");
        var session = workspace.FindDocument(id) ?? throw new KeyNotFoundException("Document not found. Open the file again.");
        return ValueTask.FromResult<DocumentRef?>(new DocumentRef(workspace, session));
    }
}
