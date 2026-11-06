using SaveToolkit.Sdk.Crypto;

namespace SaveToolkit.Plugins.Pragmata.Crypto;

internal static class AesOfb
{
    private const int BlockSize = 16;

    public static void Transform(ReadOnlySpan<byte> input, Span<byte> output, byte[] key, byte[] iv)
    {
        if (output.Length != input.Length || key.Length != BlockSize || iv.Length != BlockSize)
            throw new ArgumentException("Invalid AES-128-OFB buffer, key or IV length.");

        // PortableAes also works on browser-wasm, where Aes.Create is unsupported.
        using var aes = PortableAes.Create(key);
        var feedback = iv.ToArray();
        var stream = new byte[BlockSize];
        for (var offset = 0; offset < input.Length; offset += BlockSize)
        {
            aes.EncryptEcb(feedback, stream);
            stream.CopyTo(feedback, 0);
            var used = Math.Min(BlockSize, input.Length - offset);
            for (var i = 0; i < used; i++)
                output[offset + i] = (byte)(input[offset + i] ^ stream[i]);
        }
    }
}
