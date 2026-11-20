using System.Security.Cryptography;
using SaveToolkit.Sdk.Crypto;

namespace SaveToolkit.Core.Tests.Crypto;

/// <summary>
/// The managed AES is what runs in the browser; these tests pin it to FIPS-197, NIST SP 800-38A and the
/// platform AES, and check that the platform-backed path behaves the same.
/// </summary>
public sealed class PortableAesTests
{
    private const string Sp80038APlaintext = "6BC1BEE22E409F96E93D7E117393172AAE2D8A571E03AC9C9EB76FAC45AF8E51"
        + "30C81C46A35CE411E5FBC1191A0A52EFF69F2445DF4F9B17AD2B417BE66C3710";
    private const string Sp80038AIv = "000102030405060708090A0B0C0D0E0F";
    private const string Key128 = "2B7E151628AED2A6ABF7158809CF4F3C";
    private const string Key192 = "8E73B0F7DA0E6452C810F32B809079E562F8EAD2522C6B7B";
    private const string Key256 = "603DEB1015CA71BE2B73AEF0857D77811F352C073B6108D72D9810A30914DFF4";

    public static TheoryData<bool> Implementations => new() { true, false };

    // FIPS-197 appendix C.1-C.3 (plaintext 00112233445566778899AABBCCDDEEFF).
    public static TheoryData<string, string, bool> Fips197 => WithImplementations(
        ("000102030405060708090A0B0C0D0E0F", "69C4E0D86A7B0430D8CDB78070B4C55A"),
        ("000102030405060708090A0B0C0D0E0F1011121314151617", "DDA97CA4864CDFE06EAF70A0EC0D7191"),
        ("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F", "8EA2B7CA516745BFEAFC49904B496089"));

    // NIST SP 800-38A F.1.1, F.1.3, F.1.5.
    public static TheoryData<string, string, bool> Sp80038AEcb => WithImplementations(
        (Key128, "3AD77BB40D7A3660A89ECAF32466EF97F5D3D58503B9699DE785895A96FDBAAF43B1CD7F598ECE23881B00E3ED0306887B0C785E27E8AD3F8223207104725DD4"),
        (Key192, "BD334F1D6E45F25FF712A214571FA5CC974104846D0AD3AD7734ECB3ECEE4EEFEF7AFD2270E2E60ADCE0BA2FACE6444E9A4B41BA738D6C72FB16691603C18E0E"),
        (Key256, "F3EED1BDB5D2A03C064B5A7E3DB181F8591CCB10D410ED26DC5BA74A31362870B6ED21B99CA6F4F9F153E7B1BEAFED1D23304B7A39F9F3FF067D8D8F9E24ECC7"));

    // NIST SP 800-38A F.2.1, F.2.3, F.2.5.
    public static TheoryData<string, string, bool> Sp80038ACbc => WithImplementations(
        (Key128, "7649ABAC8119B246CEE98E9B12E9197D5086CB9B507219EE95DB113A917678B273BED6B8E3C1743B7116E69E222295163FF1CAA1681FAC09120ECA307586E1A7"),
        (Key192, "4F021DB243BC633D7178183A9FA071E8B4D9ADA9AD7DEDF4E5E738763F69145A571B242012FB7AE07FA9BAAC3DF102E008B0E27988598881D920A9E64F5615CD"),
        (Key256, "F58C4C04D6E5F1BA779EABFB5F7BFBD69CFC4E967EDB808D679F777BC6702C7D39F23369A9D9BACFA530E26304231461B2EB05E2C39BE9FCDA6C19078C6A9D1B"));

    // NIST SP 800-38A F.4.1, F.4.3, F.4.5 (OFB keystream built from ECB, as the Pragmata plugin does).
    public static TheoryData<string, string, bool> Sp80038AOfb => WithImplementations(
        (Key128, "3B3FD92EB72DAD20333449F8E83CFB4A7789508D16918F03F53C52DAC54ED8259740051E9C5FECF64344F7A82260EDCC304C6528F659C77866A510D9C1D6AE5E"),
        (Key192, "CDC80D6FDDF18CAB34C25909C99A4174FCC28B8D4C63837C09E81700C11004018D9A9AEAC0F6596F559C6D4DAF59A5F26D9F200857CA6C3E9CAC524BD9ACC92A"),
        (Key256, "DC7E84BFDA79164B7ECD8486985D38604FEBDC6740D20B3AC88F6AD82A4FB08D71AB47A086E86EEDF39D1C5BBA97C4080126141D67F37BE8538F5A8BE740E484"));

    [Theory]
    [MemberData(nameof(Fips197))]
    public void SingleBlockMatchesFips197AppendixC(string key, string expected, bool managed)
    {
        using var aes = Create(key, managed);
        var block = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        aes.EncryptEcb(block, block);
        Assert.Equal(expected, Convert.ToHexString(block));
        aes.DecryptEcb(block, block);
        Assert.Equal("00112233445566778899AABBCCDDEEFF", Convert.ToHexString(block));
    }

    [Theory]
    [MemberData(nameof(Sp80038AEcb))]
    public void EcbMatchesSp80038A(string key, string expected, bool managed)
    {
        using var aes = Create(key, managed);
        var output = new byte[64];
        aes.EncryptEcb(Convert.FromHexString(Sp80038APlaintext), output);
        Assert.Equal(expected, Convert.ToHexString(output));
        aes.DecryptEcb(Convert.FromHexString(expected), output);
        Assert.Equal(Sp80038APlaintext, Convert.ToHexString(output));
    }

    [Theory]
    [MemberData(nameof(Sp80038ACbc))]
    public void CbcMatchesSp80038A(string key, string expected, bool managed)
    {
        using var aes = Create(key, managed);
        var iv = Convert.FromHexString(Sp80038AIv);
        var output = new byte[64];
        aes.EncryptCbc(iv, Convert.FromHexString(Sp80038APlaintext), output);
        Assert.Equal(expected, Convert.ToHexString(output));
        aes.DecryptCbc(iv, Convert.FromHexString(expected), output);
        Assert.Equal(Sp80038APlaintext, Convert.ToHexString(output));
    }

    [Theory]
    [MemberData(nameof(Sp80038AOfb))]
    public void OfbKeystreamFromEcbMatchesSp80038A(string key, string expected, bool managed)
    {
        using var aes = Create(key, managed);
        var plaintext = Convert.FromHexString(Sp80038APlaintext);
        var feedback = Convert.FromHexString(Sp80038AIv);
        var output = new byte[plaintext.Length];
        for (var offset = 0; offset < plaintext.Length; offset += 16)
        {
            aes.EncryptEcb(feedback, feedback);
            for (var i = 0; i < 16; i++)
            {
                output[offset + i] = (byte)(plaintext[offset + i] ^ feedback[i]);
            }
        }

        Assert.Equal(expected, Convert.ToHexString(output));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void MatchesPlatformAesForRandomKeysAndLengths(bool managed)
    {
        var random = new Random(managed ? 0xAE5 : 0x197);
        foreach (var keySize in new[] { 16, 24, 32 })
        {
            for (var blocks = 0; blocks <= 40; blocks++)
            {
                var key = Bytes(random, keySize);
                var iv = Bytes(random, 16);
                var data = Bytes(random, blocks * 16 + (blocks == 40 ? 16 * 300 : 0));
                using var reference = PlatformAes(key);
                using var aes = Create(key, managed);
                AssertSame(reference.EncryptEcb(data, PaddingMode.None), data, aes.EncryptEcb);
                AssertSame(reference.DecryptEcb(data, PaddingMode.None), data, aes.DecryptEcb);
                AssertSame(reference.EncryptCbc(data, iv, PaddingMode.None), data, (s, d) => aes.EncryptCbc(iv, s, d));
                AssertSame(reference.DecryptCbc(data, iv, PaddingMode.None), data, (s, d) => aes.DecryptCbc(iv, s, d));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void InstancesCanBeReusedAcrossManyCalls(bool managed)
    {
        var random = new Random(0x5EC7);
        var key = Bytes(random, 32);
        using var reference = PlatformAes(key);
        using var aes = Create(key, managed);
        for (var call = 0; call < 300; call++)
        {
            var data = Bytes(random, 16 * random.Next(0, 6));
            var output = new byte[data.Length];
            aes.EncryptEcb(data, output);
            Assert.Equal(reference.EncryptEcb(data, PaddingMode.None), output);
            aes.DecryptEcb(output, output);
            Assert.Equal(data, output);
        }
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void RejectsInvalidArguments(bool managed)
    {
        Assert.Throws<ArgumentException>(() => managed ? PortableAes.CreateManaged(new byte[15]) : PortableAes.Create(new byte[15]));
        Assert.Throws<ArgumentException>(() => managed ? PortableAes.CreateManaged(new byte[33]) : PortableAes.Create(new byte[33]));
        var aes = Create(Key128, managed);
        var buffer = new byte[64];
        Assert.Throws<ArgumentException>(() => aes.EncryptEcb(buffer.AsSpan(0, 17), new byte[32]));
        Assert.Throws<ArgumentException>(() => aes.DecryptEcb(buffer.AsSpan(0, 32), new byte[16]));
        Assert.Throws<ArgumentException>(() => aes.EncryptCbc(new byte[15], buffer.AsSpan(0, 16), new byte[16]));
        Assert.Throws<ArgumentException>(() => aes.DecryptCbc(new byte[16], buffer.AsSpan(0, 15), new byte[16]));
        Assert.Throws<ArgumentException>(() => aes.EncryptEcb(buffer.AsSpan(0, 32), buffer.AsSpan(16, 32)));
        aes.Dispose();
        aes.Dispose();
        Assert.Throws<ObjectDisposedException>(() => aes.EncryptEcb(buffer.AsSpan(0, 16), buffer.AsSpan(0, 16)));
    }

    [Fact]
    public void CreateUsesThePlatformOutsideTheBrowser()
    {
        using var portable = PortableAes.Create(Convert.FromHexString(Key128));
        using var managed = PortableAes.CreateManaged(Convert.FromHexString(Key128));
        Assert.Equal(OperatingSystem.IsBrowser(), PortableAes.UsesManagedImplementation);
        Assert.Equal(OperatingSystem.IsBrowser(), portable.IsManaged);
        Assert.True(managed.IsManaged);
    }

    private static void AssertSame(byte[] expected, byte[] data, SpanAction transform)
    {
        var output = new byte[data.Length + 16];
        output[^1] = 0xA5;
        transform(data, output);
        Assert.Equal(expected, output[..data.Length]);
        Assert.Equal(0xA5, output[^1]);

        var inPlace = data.ToArray();
        transform(inPlace, inPlace);
        Assert.Equal(expected, inPlace);
    }

    private delegate void SpanAction(ReadOnlySpan<byte> source, Span<byte> destination);

    private static PortableAes Create(string key, bool managed) =>
        managed ? PortableAes.CreateManaged(Convert.FromHexString(key)) : PortableAes.Create(Convert.FromHexString(key));

    private static PortableAes Create(byte[] key, bool managed) =>
        managed ? PortableAes.CreateManaged(key) : PortableAes.Create(key);

    private static Aes PlatformAes(byte[] key)
    {
        var aes = Aes.Create();
        aes.Key = key;
        return aes;
    }

    private static byte[] Bytes(Random random, int length)
    {
        var bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }

    private static TheoryData<string, string, bool> WithImplementations(params (string Key, string Expected)[] vectors)
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var (key, expected) in vectors)
        {
            data.Add(key, expected, true);
            data.Add(key, expected, false);
        }

        return data;
    }
}
