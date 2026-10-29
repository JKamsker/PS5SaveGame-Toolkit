using Microsoft.AspNetCore.Diagnostics;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Maps domain exceptions to problems with the legacy error fields; never leaks stack traces.</summary>
internal sealed partial class ToolkitExceptionHandler(ILogger<ToolkitExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            // An aborted connection cannot receive JSON. A connected caller still gets the
            // shared error envelope, without exposing cancellation diagnostics or logging errors.
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            if (!context.RequestAborted.IsCancellationRequested)
                await ApiProblem.WriteAsync(context, StatusCodes.Status499ClientClosedRequest,
                    new ErrorDto("Request cancelled."), CancellationToken.None);
            return true;
        }

        if (exception is StaleViewException stale)
        {
            await ApiProblem.WriteAsync(context, StatusCodes.Status409Conflict,
                new ErrorDto(stale.Message, StaleViewException.Code), cancellationToken);
            return true;
        }

        var (status, message) = exception switch
        {
            KeyNotFoundException e => (StatusCodes.Status404NotFound, e.Message),
            SaveFormatException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
            ImportException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
            ExportException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
            ReferencePackException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
            InvalidDataException e => (StatusCodes.Status422UnprocessableEntity, $"Invalid data: {e.Message}"),
            InvalidOperationException e => (StatusCodes.Status409Conflict, e.Message),
            BadHttpRequestException e => (e.StatusCode, e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error. See the server log for details."),
        };

        if (status >= 500)
        {
            LogUnhandled(logger, exception, context.Request.Path);
        }

        await ApiProblem.WriteAsync(context, status, new ErrorDto(message), cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error for {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string path);
}

/// <summary>Periodically drops idle workspaces and their blobs.</summary>
internal sealed partial class WorkspaceSweeper(WorkspaceSessions sessions, ILogger<WorkspaceSweeper> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, timeProvider ?? TimeProvider.System);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { sessions.Store.Sweep(); }
            catch (AggregateException ex) { LogCleanupFailure(logger, ex); }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idle workspace cleanup reported plugin disposal failures; remaining sweeps will continue.")]
    private static partial void LogCleanupFailure(ILogger logger, AggregateException exception);
}

/// <summary>DI holder so the sweeper does not need to know how the store was built.</summary>
internal sealed record WorkspaceSessions(Core.Sessions.WorkspaceStore Store);
