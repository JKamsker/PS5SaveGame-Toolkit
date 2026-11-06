using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>An exception mapped to its status, problem body and optional Retry-After seconds.</summary>
/// <param name="Log">Whether the host logs the exception (server-side failures from the general mapping).</param>
internal sealed record ApiError(int Status, ErrorDto Body, string? RetryAfter = null, bool Log = false);

/// <summary>Maps domain exceptions to problems with the legacy error fields; never leaks stack traces.</summary>
internal static class ErrorClassifier
{
    public const int ClientClosedRequest = 499;
    public const string UnexpectedMessage = "Unexpected server error. See the server log for details.";

    /// <summary>
    /// Classifies a non-cancellation exception. Hosts handle cancellation first, then their own
    /// transport exceptions; everything else is mapped here identically for every host.
    /// </summary>
    public static ApiError Classify(Exception exception)
    {
        if (exception is StaleViewException stale)
            return new ApiError(409, new ErrorDto(stale.Message, StaleViewException.Code));

        if (exception is PresetTransferException transfer)
            return new ApiError(transfer.StatusCode, new ErrorDto(transfer.Message, transfer.Code),
                transfer.StatusCode == 503 ? "600" : null);

        var (status, message) = exception switch
        {
            FieldReferenceResponseLimitException e => (413, e.Message),
            PresetStorageException e => (503, e.Message),
            KeyNotFoundException e => (404, e.Message),
            SaveFormatException e => (422, e.Message),
            ImportException e => (422, e.Message),
            ExportException e => (422, e.Message),
            ReferencePackException e => (422, e.Message),
            InvalidDataException e => (422, $"Invalid data: {e.Message}"),
            InvalidOperationException e when e.Message.Contains("configured size limit", StringComparison.Ordinal) => (422, e.Message),
            ArgumentException e => (400, e.Message),
            InvalidOperationException e => (409, e.Message),
            ApiStatusException e => (e.StatusCode, e.Message),
            _ => (500, UnexpectedMessage),
        };

        return General(exception, status, message);
    }

    /// <summary>The general mapping tail, shared with host-specific transport exceptions.</summary>
    public static ApiError General(Exception exception, int status, string message)
    {
        var code = exception is PresetStorageException ? "presetStorageUnavailable" : null;
        return new ApiError(status, new ErrorDto(message, code), Log: status >= 500);
    }
}
