using System.Security.Cryptography;

namespace SaveToolkit.Plugins.Pragmata.Crypto;

internal static class AesOfb
{
    private const int BlockSize = 16;

    public static void Transform(ReadOnlySpan<byte> input, Span<byte> output, byte[] key, byte[] iv)
    {
        if (output.Length != input.Length || key.Length != BlockSize || iv.Length != BlockSize)
            throw new ArgumentException("Invalid AES-128-OFB buffer, key or IV length.");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        var feedback = iv.ToArray();
        var stream = new byte[BlockSize];
        for (var offset = 0; offset < input.Length; offset += BlockSize)
        {
            encryptor.TransformBlock(feedback, 0, BlockSize, stream, 0);
            stream.CopyTo(feedback, 0);
            var used = Math.Min(BlockSize, input.Length - offset);
            for (var i = 0; i < used; i++)
                output[offset + i] = (byte)(input[offset + i] ^ stream[i]);
        }
    }
}
