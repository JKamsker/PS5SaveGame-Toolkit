using SaveToolkit.Api.Contracts;

namespace SaveToolkit.Api.Endpoints;

internal static class DocumentGroupEndpoints
{
    public static void MapDocumentGroups(this RouteGroupBuilder workspace)
    {
        workspace.MapGet("/files/{fileId}/group-proposals", (WorkspaceRef w, string fileId, string pluginId) =>
            w.Value.ProposeGroups(fileId, pluginId));
        workspace.MapPost("/document-groups", (WorkspaceRef w, OpenGroupRequest request) =>
            w.Value.OpenGroup(request.ProposalToken, request.ReleaseCleanDocumentId).Session.ToDto(w.Value));
    }
}

public sealed record OpenGroupRequest(string ProposalToken, string? ReleaseCleanDocumentId = null);
