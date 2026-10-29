namespace SaveToolkit.Api.Infrastructure;

/// <summary>Separates malformed HTTP multipart envelopes (400) from invalid imported file contents (422).</summary>
internal static class MultipartUpload
{
    internal static async Task<IFormCollection> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try { return await request.ReadFormAsync(cancellationToken); }
        catch (Exception exception) when (exception is not BadHttpRequestException
            && exception is InvalidDataException or IOException)
        {
            throw new BadHttpRequestException(
                "Could not read the upload. It may be incomplete or malformed; select the files and try again.",
                StatusCodes.Status400BadRequest, exception);
        }
    }
}
