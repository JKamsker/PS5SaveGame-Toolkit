namespace SaveToolkit.LocalApi.Http;

/// <summary>An executed endpoint result before it is rendered to a <see cref="LocalResponse"/>.</summary>
internal abstract record LocalResult(int Status);

/// <summary>A JSON value serialized as <see cref="object"/> (runtime type), like minimal API results.</summary>
internal sealed record JsonResult(int Status, object? Value, string ContentType) : LocalResult(Status)
{
    /// <summary>Problem envelopes are serialized with the toolkit options rather than the HTTP options.</summary>
    public bool IsProblem => Protocol.ProblemFields.IsProblem(Value);

    public const string Utf8Json = "application/json; charset=utf-8";
    public const string PlainJson = "application/json";
}

/// <summary>Raw bytes; a download name adds an attachment Content-Disposition.</summary>
internal sealed record BytesResult(int Status, ReadOnlyMemory<byte> Content, string ContentType, string? DownloadName = null)
    : LocalResult(Status);

/// <summary>A status without a body (204, or an error that is later normalized to a problem).</summary>
internal sealed record EmptyResult(int Status) : LocalResult(Status);
