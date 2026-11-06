using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.LocalApi.Endpoints;
using SaveToolkit.LocalApi.Http;
using SaveToolkit.LocalApi.Protocol;
using SaveToolkit.LocalApi.Routing;

namespace SaveToolkit.LocalApi;

/// <summary>
/// Serves the toolkit's <c>/api</c> contract in-process, with the same routes, status codes, problem
/// bodies, ETags and downloads as the ASP.NET host, but no network or ASP.NET dependency. Requests
/// never block: on single-threaded WebAssembly, concurrent calls interleave only at awaits.
/// </summary>
public sealed class LocalApiHost : IDisposable
{
    private readonly LocalServices _services;
    private readonly RouteTable _routes = new();
    private readonly LocalApiOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public LocalApiHost(LocalApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        var catalog = options.Catalog ?? PluginCatalog.Discover();
        var packs = new ReferencePackStore(catalog.Plugins, new ReferencePackOptions
        {
            StorageRoot = options.ResolvedPackRoot,
            PreferredLocales = options.Locales.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        });
        var presets = new PresetStore(new PresetStoreOptions { StorageRoot = options.ResolvedPresetRoot });
        var workspaces = new WorkspaceStore(catalog, new WorkspaceStoreOptions
        {
            DataRoot = options.ResolvedWorkspaceRoot,
            IdleTimeout = options.WorkspaceIdleTimeout,
        }, packs);
        _services = new LocalServices(catalog, workspaces, packs, presets, new PresetTransferService(presets), options.Version);

        WorkspaceRoutes.Map(_routes);
        DocumentRoutes.Map(_routes);
        PresetRoutes.Map(_routes);
        ReferenceRoutes.Map(_routes);
        if (options.SweepInterval is { } interval) _ = SweepLoopAsync(interval, _lifetime.Token);
    }

    /// <summary>Handles one request; domain failures become problem responses, never exceptions.</summary>
    public async Task<LocalResponse> SendAsync(LocalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (path, query) = RequestPath.Split(request.PathAndQuery);
        using var http = new LocalHttpContext(request, path, QueryValues.Parse(query), cancellationToken);
        var selection = _routes.Select(http.Method, http.Path, http.RouteValues);
        if (selection.Endpoint is not { } endpoint) return NoEndpoint(http, selection);
        if (endpoint.AcceptsJson && !AcceptsContentType(http.ContentType))
            return StatusCodePage(http, 415, []);

        try
        {
            var result = PreBindingRejection(http, endpoint)
                ?? await endpoint.Handler(new EndpointContext(http, _services));
            // Rendering is part of result execution: a serialization failure is still a problem response.
            return ResponseWriter.Write(result, http.ResponseHeaders);
        }
        catch (Exception exception)
        {
            return HandleException(http, exception);
        }
    }

    /// <summary>Drops clean, unleased workspaces idle longer than the configured timeout; returns the count.</summary>
    public int SweepIdleWorkspaces() => _services.Workspaces.Sweep();

    /// <summary>
    /// Does one bounded piece of deferred startup work (validating one embedded reference pack nothing
    /// has needed yet) and returns false once none is left. Responses are the same whether or not this
    /// runs; calling it between requests only moves the cost away from the first document open.
    /// </summary>
    public bool RunIdleWork()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _services.Packs.ValidateNext();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _services.Workspaces.Dispose();
    }

    /// <summary>Status-code pages: unknown <c>/api</c> paths get a 404 problem, wrong methods a 405 with Allow.</summary>
    private static LocalResponse NoEndpoint(LocalHttpContext http, RouteSelection selection)
    {
        var status = selection.AllowedMethods.Count > 0 ? 405 : 404;
        // Unmatched requests never reach an endpoint, so no response headers exist yet.
        var headers = new List<KeyValuePair<string, string>>();
        if (status == 405) headers.Add(new("Allow", string.Join(", ", selection.AllowedMethods)));
        return StatusCodePage(http, status, headers);
    }

    private static LocalResponse StatusCodePage(LocalHttpContext http, int status, List<KeyValuePair<string, string>> headers) =>
        IsApiPath(http.Path) ? ResponseWriter.Write(EndpointFilters.Problem(status, null), headers) : new LocalResponse(status, headers, []);

    /// <summary>
    /// Routing's accepts policy: a missing Content-Type reaches binding; otherwise it must be a subset of
    /// <c>application/json</c> (for example <c>application/problem+json</c>).
    /// </summary>
    private static bool AcceptsContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return true;
        return MediaType.Parse(contentType) is { } media
            && string.Equals(media.Type, "application", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(media.SubType, "json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(media.Suffix, "json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsApiPath(string path) =>
        path.Equals("/api", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bounded preset envelopes are rejected before any binding (PresetRequestBodyLimitMiddleware).</summary>
    private static LocalResult? PreBindingRejection(LocalHttpContext http, LocalEndpoint endpoint)
    {
        if (endpoint.PresetBodyLimit is { } limit && http.Body.Length > limit)
            return new JsonResult(413, ProblemFields.Create(413, ReasonPhrases.Get(413), PresetRequestBodyLimitsRejection(limit)),
                JsonResult.Utf8Json);
        return null;
    }

    private static ErrorDto PresetRequestBodyLimitsRejection(int limit) =>
        SaveToolkit.Api.Infrastructure.PresetRequestBodyLimits.Rejection(limit);

    /// <summary>The exception handler: shared classification, a problem body and no-cache headers.</summary>
    private LocalResponse HandleException(LocalHttpContext http, Exception exception)
    {
        // The exception handler clears the response (including any ETag) and disables caching.
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Cache-Control", "no-cache,no-store"), new("Pragma", "no-cache"), new("Expires", "-1"),
        };
        if (exception is OperationCanceledException)
        {
            if (http.Aborted.IsCancellationRequested) return new LocalResponse(ErrorClassifier.ClientClosedRequest, headers, []);
            return ResponseWriter.Write(ExceptionProblem(ErrorClassifier.ClientClosedRequest, new ErrorDto("Request cancelled.")), headers);
        }

        var error = ErrorClassifier.Classify(exception);
        if (error.RetryAfter is { } retryAfter) headers.Add(new("Retry-After", retryAfter));
        if (error.Log) _options.OnError?.Invoke($"Unhandled error for {http.Path}", exception);
        return ResponseWriter.Write(ExceptionProblem(error.Status, error.Body), headers);
    }

    private static JsonResult ExceptionProblem(int status, ErrorDto body) =>
        new(status, ProblemFields.Create(status, ReasonPhrases.Get(status), body), JsonResult.Utf8Json);

    private async Task SweepLoopAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval, _options.TimeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { _services.Workspaces.Sweep(); }
                catch (AggregateException ex) { _options.OnError?.Invoke("Idle workspace cleanup reported plugin disposal failures.", ex); }
            }
        }
        catch (OperationCanceledException)
        {
            // Host disposed.
        }
        catch (Exception exception)
        {
            // Like a faulted hosted service: report it and stop sweeping (requests keep working).
            _options.OnError?.Invoke("Idle workspace sweeping stopped after an unexpected failure.", exception);
        }
    }
}
