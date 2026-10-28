using System.Security.Cryptography;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseCipherTests
{
    [Fact]
    public void Cipher_MatchesIndependentChainedVectors_InBothDirections()
    {
        // Independently calculated using Python/PyCryptodome, not the upstream converter.
        // Includes a partial feedback update (16 bytes), key evolution, and a multi-chunk block.
        string[] expected =
        [
            "C6F4BCD93FD05B3DAD2CC97ACE02DE7FCE541588B84D7FF98ABEF3B3301FA7CA090E6D9BD2A64A9F14A69458574367B8B327595D95425C79E8BA8706E28EA331",
            "DD9FD4DCCE0B741A47448BF342492D48",
            "AE1478A3F942AA9B898FE10D4968D7453F46DE21C372F750FA9DD9EBE7D82331227977DB4DCA8C4825D068251F998F833A8F0B4682B87D85CFA9D462D859AE55ABF876CC2F67A39274A21DE504A5EBAF",
        ];
        using var encryptor = new AriseCipher();
        using var decryptor = new AriseCipher();
        foreach (var hex in expected)
        {
            var ciphertext = Convert.FromHexString(hex);
            var plaintext = Enumerable.Range(0, ciphertext.Length).Select(i => (byte)i).ToArray();
            var encrypted = new byte[ciphertext.Length];
            encryptor.Transform(plaintext, encrypted, encrypt: true);
            Assert.Equal(ciphertext, encrypted);
            var decrypted = new byte[ciphertext.Length];
            decryptor.Transform(ciphertext, decrypted, encrypt: false);
            Assert.Equal(plaintext, decrypted);
        }
    }

    [Fact]
    public void Sha1_MatchesPublishedAbcVector()
    {
#pragma warning disable CA5350
        var digest = SHA1.HashData("abc"u8);
#pragma warning restore CA5350
        Assert.Equal("A9993E364706816ABA3E25717850C26C9CD0D89D", Convert.ToHexString(digest));
    }
}
