using System.Security.Cryptography;
using SaveToolkit.Sdk.Crypto;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Per-operation PS5 cipher state. Public format material, not user credentials.</summary>
internal sealed class AriseCipher : IDisposable
{
    private const int FeedbackSize = 64;
    private const byte ZeroKeyReplacement = 0x20;
    private readonly byte[] _work = new byte[FeedbackSize];
    private readonly byte[] _key = Convert.FromHexString(
        "39514C4A5048585A556E47324C4A414858433677636648596B42567A53487942" +
        "2D353374396350666E726950634A2D6B4B637252614362364272695553636400");
    private readonly byte[] _feedback = Convert.FromHexString(
        "4A45457854703539693651505F706732474B375A6A63573332624A2D586D5257" +
        "435859415843345963474D527552526A42685F44375473384D506B5A34473900");

    public void Transform(ReadOnlySpan<byte> source, Span<byte> destination, bool encrypt)
    {
        // The key evolves once per section; feedback evolves once per 64-byte chunk.
        // Keep one AES-256-ECB cipher for the section rather than recreating it for every chunk.
        // PortableAes also works on browser-wasm, where Aes.Create is unsupported.
        using var aes = PortableAes.Create(_key.AsSpan(0, 32));
        for (var offset = 0; offset < source.Length; offset += FeedbackSize)
        {
            var length = Math.Min(FeedbackSize, source.Length - offset);
            var input = source.Slice(offset, length);
            var output = destination.Slice(offset, length);
            var temporary = _work.AsSpan(0, length);
            input.CopyTo(temporary);
            if (encrypt)
            {
                Xor(temporary);
                aes.EncryptEcb(temporary, temporary);
                temporary.CopyTo(output);
                output.CopyTo(_feedback);
            }
            else
            {
                aes.DecryptEcb(temporary, temporary);
                Xor(temporary);
                input.CopyTo(_feedback);
                temporary.CopyTo(output);
            }
        }

        UpdateKey(encrypt ? source : destination);
    }

    private void Xor(Span<byte> block)
    {
        // PS5 swaps each adjacent pair of feedback bytes, including partial chunks.
        for (var i = 0; i < block.Length; i++)
        {
            block[i] ^= _feedback[i ^ 1];
        }
    }

    private void UpdateKey(ReadOnlySpan<byte> plaintext)
    {
        // SHA1 is required by the game's file format, not used as a security primitive here.
#pragma warning disable CA5350
        var digest = SHA1.HashData(plaintext);
#pragma warning restore CA5350
        for (var i = 0; i < _key.Length; i++)
        {
            var updated = (byte)(_key[i] ^ digest[i % digest.Length]);
            _key[i] = updated != 0 ? updated : _key[i] != 0 ? _key[i] : ZeroKeyReplacement;
        }
    }

    public void Dispose()
    {
        // Nothing to release: each Transform disposes its own cipher. Kept so callers can use 'using'.
    }
}
