using System.Runtime.CompilerServices;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Sessions;
using SaveToolkit.LocalApi.Http;
using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>
/// The endpoint filters of the ASP.NET host: request validation and problem normalization
/// (<c>ApiContractFilter</c>) around the document operation gate (<c>DocumentRevisionFilter</c>).
/// </summary>
internal static class EndpointFilters
{
    private static readonly ConditionalWeakTable<DocumentSession, SemaphoreSlim> Gates = new();

    /// <param name="arguments">Bound handler arguments in declaration order (validated like ApiContractFilter).</param>
    /// <param name="document">The bound document, when the route has one; enables the revision gate.</param>
    public static async ValueTask<LocalResult> RunAsync(this EndpointContext context, object?[] arguments, DocumentRef? document,
        Func<ValueTask<object?>> handler)
    {
        foreach (var argument in arguments)
        {
            if (ApiRequestValidation.Error(argument) is { } error)
                return Problem(400, new ErrorDto(error));
        }

        var result = document is null ? await handler() : await GateAsync(context, arguments, document, handler);
        return Normalize(result);
    }

    public static ValueTask<object?> Sync(Func<object?> handler) => ValueTask.FromResult(handler());

    public static JsonResult Problem(int status, object? body) =>
        new(status, ProblemFields.Create(status, ReasonPhrases.Get(status), body), JsonResult.PlainJson);

    private static async ValueTask<object?> GateAsync(EndpointContext context, object?[] arguments, DocumentRef reference,
        Func<ValueTask<object?>> handler)
    {
        var document = reference.Value;
        var gate = Gates.GetValue(document, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(context.Aborted);
        try
        {
            // Binding happens before this gate. A preceding close/replacement may have
            // disposed that exact session while this request was queued.
            if (!ReferenceEquals(reference.Workspace.FindDocument(document.Id), document))
                return Problem(404, new ErrorDto("Document not found. Open the file again."));
            var ifMatch = context.Http.HeaderValues("If-Match").ToList();
            if (ifMatch.Count > 0)
            {
                if (!EntityTags.TryParseStrictList(ifMatch, out var tags) || tags.Count == 0
                    || tags.Count > 1 && tags.Any(tag => tag.IsAny))
                    return Problem(400, new ErrorDto("If-Match must contain valid entity tags or *."));
                var currentTag = RevisionTag.Format(document.State.Revision);
                if (!tags.Any(tag => tag.IsAny || !tag.IsWeak && tag.Tag == currentTag))
                    return Problem(412, new ErrorDto(RevisionTag.StaleMessage, StaleViewException.Code));
            }

            if (arguments.OfType<HistoryRequest>().SingleOrDefault()?.Revision is { } expected)
            {
                var state = document.State;
                if (expected != state.Revision) return ApiOutcome.Conflict(RevisionTag.StaleHistory(state));
            }

            var result = await handler();
            if (RevisionTag.Of(ValueOf(result)) is { } current) context.Http.SetResponseHeader("ETag", RevisionTag.Format(current));
            return result;
        }
        finally { gate.Release(); }
    }

    private static object? ValueOf(object? result) => result switch
    {
        ApiOutcome outcome => outcome.Value,
        JsonResult json => json.Value,
        LocalResult => null,
        _ => result,
    };

    /// <summary>Error results become problem envelopes that keep their body fields (ApiProblem.Normalize).</summary>
    private static LocalResult Normalize(object? result) => result switch
    {
        JsonResult json when ProblemFields.IsProblem(json.Value) => json,
        JsonResult { Status: >= 400 } json => Problem(json.Status, json.Value),
        EmptyResult { Status: >= 400 } empty => Problem(empty.Status, null),
        LocalResult local => local,
        ApiOutcome { Status: >= 400 } outcome => Problem(outcome.Status, outcome.Value),
        ApiOutcome { Status: 204 } => new EmptyResult(204),
        ApiOutcome outcome => new JsonResult(outcome.Status, outcome.Value, JsonResult.Utf8Json),
        _ => new JsonResult(200, result, JsonResult.Utf8Json),
    };
}
