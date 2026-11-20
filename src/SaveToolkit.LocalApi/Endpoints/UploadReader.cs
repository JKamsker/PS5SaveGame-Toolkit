using System.Runtime.InteropServices;
using SaveToolkit.LocalApi.Http;
using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>Separates malformed multipart envelopes (400) from invalid imported file contents (422).</summary>
internal static class UploadReader
{
    public const string MalformedMessage =
        "Could not read the upload. It may be incomplete or malformed; select the files and try again.";

    /// <param name="bodyLengthLimit">The request (and multipart section) size limit, enforced when the body is read.</param>
    public static FormData Read(EndpointContext context, MediaType contentType, long bodyLengthLimit)
    {
        // Kestrel's MaxRequestBodySize: a transport 413, raised only once the endpoint reads the body.
        if (context.Http.Body.Length > bodyLengthLimit)
            throw new ApiStatusException($"Request body too large. The max request body size is {bodyLengthLimit} bytes.", 413);
        try
        {
            return MultipartForm.Read(contentType, context.Http.Body, bodyLengthLimit, ApiLimits.MaxFormValues, context.Aborted);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new ApiStatusException(MalformedMessage, 400, exception);
        }
    }

    /// <summary>A read-only stream over an uploaded part without copying the request body.</summary>
    public static Stream OpenRead(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment) && segment.Array is not null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);
}
