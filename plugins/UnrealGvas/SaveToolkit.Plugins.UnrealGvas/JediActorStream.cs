using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas;

internal sealed record JediActorStream(
    bool IsValid,
    int DeclaredLength,
    int DecodedLength,
    IReadOnlyList<GvasNode> Roots,
    string? Error)
{
    public IReadOnlyList<GvasNode> ObservedNames { get; init; } = [];
    public bool CompleteTagParse { get; init; }
}

internal static class JediActorStreamReader
{
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
            // Stop at the declared bound, including a one-byte overrun probe; never inflate to an unbounded buffer.
            var buffer = new byte[8192];
            while (true)
            {
                var read = zlib.Read(buffer, 0, (int)Math.Min(buffer.Length, declaredLength - output.Length + 1));
                if (read == 0) break;
                if (output.Length + read > declaredLength)
                    return Invalid(declaredLength, "Decoded actor stream exceeds its declared length.");
                output.Write(buffer, 0, read);
            }
            var plain = output.ToArray();
            GvasEnvelope.ValidateZlib(compressed, plain);
            if (plain.Length != declaredLength)
                return Invalid(declaredLength, $"Decoded {plain.Length:N0} bytes; the save declares {declaredLength:N0}.");
            if (plain.Length < sizeof(int))
                return Invalid(declaredLength, "The decoded actor stream has no property name prefix.");
            IReadOnlyList<GvasNode> roots;
            var complete = true;
            try
            {
                // The first Int32 is the FString length of ActorRecords, not an outer array count.
                roots = GvasFile.ParseHeaderless(plain, header).Properties;
            }
            catch (Exception ex) when (ex is SaveFormatException or ArgumentException or OverflowException)
            {
                complete = false;
                roots = JediActorScanner.Scan(plain);
            }
            return roots.Count == 0
                ? Invalid(declaredLength, "The decoded actor stream contains no bounded property tags.")
                : new JediActorStream(true, declaredLength, plain.Length, roots, null)
                { ObservedNames = JediActorScanner.ScanKnownNames(plain), CompleteTagParse = complete };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SaveFormatException or OverflowException or ArgumentException)
        {
            return Invalid(declaredLength, ex.Message);
        }
    }

    private static JediActorStream Invalid(int declaredLength, string error) =>
        new(false, declaredLength, 0, [], error);
}
