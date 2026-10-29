namespace SaveToolkit.Codecs.Tests;

internal static class OodleTestStreams
{
    internal const int QuantumBytes = 256 * 1024;

    internal static byte[] Entropy(int type, byte[] payload, int outputSize)
    {
        uint sizes = ((uint)(outputSize - 1) << 18) | (uint)payload.Length;
        return [(byte)((type << 4) | ((outputSize - 1) >> 14)), (byte)(sizes >> 24),
            (byte)(sizes >> 16), (byte)(sizes >> 8), (byte)sizes, .. payload];
    }

    internal static byte[] Quantum(byte[] payload)
    {
        int size = payload.Length - 1;
        return [0x8C, 6, (byte)(size >> 16), (byte)(size >> 8), (byte)size, .. payload];
    }

    internal static void Reject(byte[] stream, int outputSize)
    {
        var error = Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(stream, outputSize));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Contains("Kraken", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
