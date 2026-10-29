using System.Security.Cryptography;

namespace SaveToolkit.Sdk.References;

internal static class PackFileVerifier
{
    public static byte[]? Read(IPackFileSource source, PackFileModel file, long maxBytes, bool normaliseText, PackByteBudget budget, PackProblems problems)
    {
        var bytes = source.Read(file.Path!, maxBytes);
        if (bytes is null)
        {
            problems.Add($"'{file.Path}' is listed but missing");
            return null;
        }

        if (!budget.TryTake(bytes.Length))
        {
            throw new EmbeddedReferencePackException($"Reference pack {source.Origin} is larger than {budget.Limit:N0} bytes in total.");
        }

        var hashed = normaliseText ? NormaliseLineEndings(bytes) : bytes;
        var actual = Convert.ToHexStringLower(SHA256.HashData(hashed));
        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"'{file.Path}' hash mismatch (manifest {file.Sha256!.ToLowerInvariant()}, file {actual})");
            return null;
        }

        return hashed;
    }

    /// <summary>CRLF → LF; lone CR is kept.</summary>
    internal static byte[] NormaliseLineEndings(byte[] bytes)
    {
        if (bytes.AsSpan().IndexOf("\r\n"u8) < 0)
        {
            return bytes;
        }

        var result = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == '\r' && i + 1 < bytes.Length && bytes[i + 1] == '\n')
            {
                continue;
            }

            result.Add(bytes[i]);
        }

        return [.. result];
    }

}

internal sealed class PackByteBudget(long limit)
{
    private long _used;
    public long Limit => limit;
    public bool TryTake(long bytes)
    {
        _used += bytes;
        return _used <= limit;
    }
}
