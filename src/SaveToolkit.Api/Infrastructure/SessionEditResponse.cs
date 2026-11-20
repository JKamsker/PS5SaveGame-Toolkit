using SaveToolkit.Core.Sessions;
using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Shared status mapping for edits, actions, history and individual change reverts.</summary>
internal static class SessionEditResponse
{
    internal static IResult Create(SessionEditResult result) => ApiOutcome.ForEdit(result).ToResult();
}

/// <summary>Renders transport-neutral outcomes; failures are normalized to problems by <see cref="ApiContractFilter"/>.</summary>
internal static class ApiOutcomeResults
{
    internal static IResult ToResult(this ApiOutcome outcome) => outcome.Status switch
    {
        StatusCodes.Status200OK => Results.Ok(outcome.Value),
        StatusCodes.Status204NoContent => Results.NoContent(),
        StatusCodes.Status404NotFound when outcome.Value is null => Results.NotFound(),
        StatusCodes.Status409Conflict => Results.Conflict(outcome.Value),
        StatusCodes.Status422UnprocessableEntity => Results.UnprocessableEntity(outcome.Value),
        _ => Results.Json(outcome.Value, statusCode: outcome.Status),
    };
}
