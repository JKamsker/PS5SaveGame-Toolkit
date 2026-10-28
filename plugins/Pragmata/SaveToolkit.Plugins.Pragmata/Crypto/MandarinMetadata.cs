using System.Buffers.Binary;
using System.Numerics;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Crypto;

internal static class MandarinMetadata
{
    // Public format constant from the reference toolkit; not a user credential.
    private static readonly BigInteger Modulus = new(
        Convert.FromHexString("f33b6fb972a0b72515e45c391829e182ad8a9bdc0a64d3444d79c810ab863717"), isUnsigned: true);

    public static void Validate(ReadOnlySpan<byte> metadata, ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv)
    {
        const int PairSize = 128;
        const int IntegerSize = PairSize / 2;
        Span<byte> recovered = stackalloc byte[32];
        var lowWord = new BigInteger(ulong.MaxValue);
        for (var i = 0; i < 4; i++)
        {
            var pair = metadata.Slice(i * PairSize, PairSize);
            var x = new BigInteger(pair[..IntegerSize], isUnsigned: true);
            var ciphertext = new BigInteger(pair[IntegerSize..], isUnsigned: true);
            var divisor = BigInteger.ModPow(x, ~DsssContainer.Ps5Key, Modulus);
            if (divisor.IsZero)
                throw new SaveFormatException("Invalid Mandarin key/IV metadata.");
            var word = (ulong)((ciphertext / divisor) & lowWord);
            BinaryPrimitives.WriteUInt64LittleEndian(recovered[(i * sizeof(ulong))..], word);
        }

        if (!recovered[..16].SequenceEqual(key) || !recovered[16..].SequenceEqual(iv))
            throw new SaveFormatException("Mandarin embedded key/IV metadata mismatch.");
    }
}
