using Microsoft.AspNetCore.Diagnostics;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Maps domain exceptions to <c>{ "error": "..." }</c> responses; never leaks stack traces.</summary>
internal sealed partial class ToolkitExceptionHandler(ILogger<ToolkitExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is StaleViewException stale)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ErrorDto(stale.Message, StaleViewException.Code), cancellationToken);
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

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ErrorDto(message), cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error for {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string path);
}

/// <summary>Periodically drops idle workspaces and their blobs.</summary>
internal sealed class WorkspaceSweeper(WorkspaceSessions sessions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            sessions.Store.Sweep();
        }
    }
}

/// <summary>DI holder so the sweeper does not need to know how the store was built.</summary>
internal sealed record WorkspaceSessions(Core.Sessions.WorkspaceStore Store);
