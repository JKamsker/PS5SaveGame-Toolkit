using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Core.Sessions;
using SaveToolkit.LocalApi.Http;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>
/// Parameter binding for one request, reproducing minimal-API order and diagnostics: route binders
/// (workspace/document) first, then the JSON body, then query/route values in declaration order.
/// </summary>
internal sealed class EndpointContext(LocalHttpContext http, LocalServices services)
{
    private static readonly object LeaseKey = new();

    public LocalHttpContext Http { get; } = http;

    public LocalServices Services { get; } = services;

    public CancellationToken Aborted => Http.Aborted;

    public WorkspaceRef Workspace() => new(ResolveWorkspace());

    public DocumentRef Document()
    {
        var workspace = ResolveWorkspace();
        var id = Http.RouteValues.GetValueOrDefault("documentId") ?? throw new KeyNotFoundException("Document not found.");
        var session = workspace.FindDocument(id) ?? throw new KeyNotFoundException("Document not found. Open the file again.");
        return new DocumentRef(workspace, session);
    }

    /// <summary>Acquires a workspace lease for the rest of the request (like <c>WorkspaceRef.Retain</c>).</summary>
    public Workspace Retain(string id)
    {
        var lease = Services.Workspaces.Acquire(id) ?? throw new KeyNotFoundException("Workspace not found. It may have expired; import the save again.");
        Http.Items[LeaseKey] = lease;
        Http.RegisterForDispose(lease);
        return lease.Value;
    }

    public string Route(string name) => Http.RouteValues[name];

    /// <summary>
    /// Reads a JSON body like minimal APIs: an empty body is "absent" (no media-type check), otherwise
    /// the content type must be JSON (415), its charset known (409) and the payload must deserialize (400).
    /// </summary>
    /// <param name="type">The parameter type as minimal APIs display it, e.g. <c>EditRequest</c>.</param>
    /// <param name="optional">A nullable parameter accepts an absent or <c>null</c> body.</param>
    /// <param name="implicitBody">Whether the body source was inferred (no <c>[FromBody]</c>).</param>
    public T? Body<T>(string type, string name, bool optional = false, bool implicitBody = true) where T : class
    {
        T? value = null;
        if (!Http.Body.IsEmpty)
        {
            if (MediaType.Parse(Http.ContentType) is not { IsJson: true } mediaType)
                throw new Protocol.ApiStatusException($"Expected a supported JSON media type but got \"{Http.ContentType}\".", 415);
            // The charset is honoured like ReadFromJsonAsync; an unknown one escapes binding as a 409.
            var json = Charsets.JsonUtf8(mediaType, Http.Body);
            try
            {
                var span = json.Span;
                if (span.StartsWith(Utf8Bom)) span = span[Utf8Bom.Length..];
                value = JsonSerializer.Deserialize<T>(span, ToolkitJson.Options);
            }
            catch (JsonException exception)
            {
                throw new Protocol.ApiStatusException($"Failed to read parameter \"{type} {name}\" from the request body as JSON.", 400, exception);
            }
        }

        if (value is null && !optional)
        {
            throw new Protocol.ApiStatusException(implicitBody
                ? $"Implicit body inferred for parameter \"{name}\" but no body was provided. Did you mean to use a Service instead?"
                : $"Required parameter \"{type} {name}\" was not provided from body.");
        }

        return value;
    }

    public string QueryString(string name) =>
        Raw(name) ?? throw Missing("string", name);

    public string? OptionalQueryString(string name) => Raw(name);

    public long QueryLong(string name) => Raw(name) is { } raw
        ? long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw Failed("long", name, raw)
        : throw Missing("long", name);

    public int QueryInt(string name) => Raw(name) is { } raw
        ? int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw Failed("int", name, raw)
        : throw Missing("int", name);

    public int? OptionalQueryInt(string name) => Optional<int>(name, "Nullable<int>",
        raw => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null);

    public long? OptionalQueryLong(string name) => Optional<long>(name, "Nullable<long>",
        raw => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null);

    public bool? OptionalQueryBool(string name) => Optional<bool>(name, "Nullable<bool>",
        raw => bool.TryParse(raw, out var value) ? value : null);

    public TEnum? OptionalQueryEnum<TEnum>(string name) where TEnum : struct, Enum => Optional<TEnum>(name, $"Nullable<{typeof(TEnum).Name}>",
        raw => Enum.TryParse<TEnum>(raw, out var value) ? value : null);

    private T? Optional<T>(string name, string type, Func<string, T?> parse) where T : struct
    {
        // An empty value is still a value: minimal APIs report it as a binding failure.
        var raw = Raw(name);
        if (raw is null) return null;
        return parse(raw) ?? throw Failed(type, name, raw);
    }

    private string? Raw(string name) =>
        Http.Query.TryGetValues(name, out var values) ? Protocol.QueryValueExtensions.Join(values) : null;

    private static Protocol.ApiStatusException Missing(string type, string name) =>
        new($"Required parameter \"{type} {name}\" was not provided from query string.");

    private static Protocol.ApiStatusException Failed(string type, string name, string raw) =>
        new($"Failed to bind parameter \"{type} {name}\" from \"{raw}\".");

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private Workspace ResolveWorkspace()
    {
        if (Http.Items.TryGetValue(LeaseKey, out var existing) && existing is WorkspaceLease lease) return lease.Value;
        var id = Http.RouteValues.GetValueOrDefault("workspaceId") ?? throw new KeyNotFoundException("Workspace not found.");
        return Retain(id);
    }
}
