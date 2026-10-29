using System.Buffers;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Sessions;

/// <summary>Reads immutable owned sources while preserving path-specific ownership diagnostics.</summary>
internal static class OwnedSourceReader
{
    private const int BufferSize = 81920;
    private const long MaxArrayBytes = int.MaxValue;

    public static string Hash(ISaveFile file, string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = file.OpenRead();
            return OperationCancellation.HashSha256(stream, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Owned output '{relativePath}' could not be read. Close and reimport the dump.", error);
        }
    }

    public static byte[] ReadAllBytes(ISaveFile file, string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            if (file.Length < 0 || file.Length > MaxArrayBytes)
                throw new InvalidOperationException($"Owned output '{relativePath}' is too large to capture safely.");

            using var stream = file.OpenRead();
            using var content = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    if (content.Length > file.Length - read)
                        throw new InvalidOperationException(
                            $"Owned output '{relativePath}' changed while it was being captured. Reimport the dump.");
                    content.Write(buffer, 0, read);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return content.ToArray();
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Owned output '{relativePath}' could not be read. Close and reimport the dump.", error);
        }
    }
}
