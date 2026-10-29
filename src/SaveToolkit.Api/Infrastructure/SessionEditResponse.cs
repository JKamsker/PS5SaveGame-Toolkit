using SaveToolkit.Core.Sessions;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Shared status mapping for edits, actions, history and individual change reverts.</summary>
internal static class SessionEditResponse
{
    internal static IResult Create(SessionEditResult result) =>
        result.Ok ? Results.Ok(result)
        : result.Stale ? Results.Conflict(result)
        : Results.UnprocessableEntity(result);
}
