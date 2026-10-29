using SaveToolkit.Api.Contracts;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Route metadata for bounded preset JSON envelopes, applied before ASP.NET binding.</summary>
internal sealed record PresetRequestBodyLimitMetadata(int MaxBytes);

internal static class PresetRequestBodyLimits
{
    public const int CreateBytes = 512 * 1024;
    public const int CaptureBytes = 256 * 1024;
    public const int PreviewBytes = 256 * 1024;
    public const int ApplyBytes = 64 * 1024;
}

/// <summary>Buffers only the bounded request envelope so oversized JSON never reaches model binding.</summary>
internal sealed class PresetRequestBodyLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<PresetRequestBodyLimitMetadata>();
        if (metadata is null || !context.Request.ContentLength.HasValue && !context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength is long length && length > metadata.MaxBytes)
        {
            await Reject(context, metadata.MaxBytes);
            return;
        }

        var bounded = new MemoryStream(Math.Min(metadata.MaxBytes, 64 * 1024));
        var buffer = new byte[Math.Min(32 * 1024, metadata.MaxBytes + 1)];
        var count = 0;
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
            if (read == 0) break;
            count = checked(count + read);
            if (count > metadata.MaxBytes)
            {
                await Reject(context, metadata.MaxBytes);
                return;
            }
            await bounded.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
        }

        bounded.Position = 0;
        context.Request.Body = bounded;
        context.Request.ContentLength = count;
        await next(context);
    }

    private static Task Reject(HttpContext context, int maxBytes) =>
        ApiProblem.WriteAsync(context, StatusCodes.Status413PayloadTooLarge,
            new ErrorDto($"Preset request body exceeds the {maxBytes} byte limit.", "presetPayloadTooLarge"),
            context.RequestAborted);
}
