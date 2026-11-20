using SaveToolkit.Core.Sessions;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>
/// Transport-neutral handler result: a status plus an optional JSON value. Statuses of 400 and above
/// are rendered as problem envelopes that retain the value's fields (see <see cref="ProblemFields"/>).
/// </summary>
internal sealed record ApiOutcome(int Status, object? Value = null)
{
    public static ApiOutcome Ok(object? value) => new(200, value);

    public static ApiOutcome Conflict(object? value) => new(409, value);

    public static ApiOutcome Unprocessable(object? value) => new(422, value);

    /// <summary>Shared status mapping for edits, actions, history and individual change reverts.</summary>
    public static ApiOutcome ForEdit(SessionEditResult result) =>
        result.Ok ? Ok(result) : result.Stale ? Conflict(result) : Unprocessable(result);
}
