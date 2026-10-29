using System.Security.Cryptography;

namespace SaveToolkit.Core;

/// <summary>Cancellation-aware acquisition for the synchronous, reentrant document/ownership gates.</summary>
internal static class OperationCancellation
{
    private static readonly TimeSpan GatePollInterval = TimeSpan.FromMilliseconds(25);
    private const int BufferSize = 81920;

    public static string HashSha256(Stream stream, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static T Locked<T>(Lock gate, Func<T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (!gate.TryEnter(GatePollInterval)) cancellationToken.ThrowIfCancellationRequested();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return operation();
        }
        finally { gate.Exit(); }
    }
}
