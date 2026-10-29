using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Sessions;

/// <summary>Reads immutable owned sources while preserving path-specific ownership diagnostics.</summary>
internal static class OwnedSourceReader
{
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
}
