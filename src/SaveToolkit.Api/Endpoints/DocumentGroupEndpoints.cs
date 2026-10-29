using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class DocumentGroupEndpoints
{
    public static void MapDocumentGroups(this RouteGroupBuilder workspace)
    {
        workspace.MapGet("/files/{fileId}/group-proposals", (WorkspaceRef w, string fileId, string pluginId) =>
            w.Value.ProposeGroups(fileId, pluginId)).WithApiErrors(Status400BadRequest, Status409Conflict, Status422UnprocessableEntity);
        workspace.MapPost("/document-groups", (WorkspaceRef w, OpenGroupRequest request) =>
            w.Value.OpenGroup(request.ProposalToken, request.ReleaseCleanDocumentId).Session.ToDto(w.Value))
            .WithApiErrors(Status400BadRequest, Status409Conflict, Status415UnsupportedMediaType, Status422UnprocessableEntity);
    }
}

public sealed record OpenGroupRequest(string ProposalToken, string? ReleaseCleanDocumentId = null);
