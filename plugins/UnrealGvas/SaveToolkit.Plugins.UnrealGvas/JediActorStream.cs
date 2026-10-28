using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas;

internal sealed record JediActorStream(
    bool IsValid,
    int DeclaredLength,
    int DecodedLength,
    IReadOnlyList<GvasNode> Roots,
    string? Error);

internal static class JediActorStreamReader
{
    private const int CountPrefixLength = sizeof(int);
    private const int MaxDecodedLength = 128 * 1024 * 1024;

    public static JediActorStream Read(ReadOnlySpan<byte> compressed, int declaredLength, GvasHeader header)
    {
        if (declaredLength < 0 || declaredLength > MaxDecodedLength)
            return Invalid(declaredLength, "The declared actor stream length exceeds the safety limit.");

        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(Math.Min(declaredLength, 1 << 20));
            zlib.CopyTo(output);
            var plain = output.ToArray();
            if (plain.Length != declaredLength)
                return Invalid(declaredLength, $"Decoded {plain.Length:N0} bytes; the save declares {declaredLength:N0}.");
            if (plain.Length < CountPrefixLength)
                return Invalid(declaredLength, "The decoded actor stream has no array count prefix.");

            var count = BitConverter.ToInt32(plain, 0);
            if (count < 0 || count > GvasFile.MaxNodes)
                return Invalid(declaredLength, $"Actor array count {count:N0} is outside the node budget.");
            IReadOnlyList<GvasNode> roots;
            try
            {
                roots = GvasFile.ParseHeaderless(plain.AsSpan(CountPrefixLength), header).Properties;
            }
            catch (Exception)
            {
                roots = JediActorScanner.Scan(plain.AsSpan(CountPrefixLength));
            }
            return roots.Count == 0
                ? Invalid(declaredLength, "The decoded actor stream contains no bounded property tags.")
                : new JediActorStream(true, declaredLength, plain.Length, roots, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SaveFormatException or OverflowException or ArgumentException)
        {
            return Invalid(declaredLength, ex.Message);
        }
    }

    private static JediActorStream Invalid(int declaredLength, string error) =>
        new(false, declaredLength, 0, [], error);
}
