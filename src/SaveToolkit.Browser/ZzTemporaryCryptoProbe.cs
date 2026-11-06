#define AFTER_FIX
// TEMPORARY crypto probe (removed after verification). Not part of the change.
#pragma warning disable
// Throwaway probe: lives only in the scratchpad, never in the repository.
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;

namespace SaveToolkit.Browser.TemporaryCryptoProbe;

public static partial class Probe
{
    private delegate byte[] SpanToBytes(ReadOnlySpan<byte> source);
    private delegate byte[] Digest(string algorithm, ReadOnlySpan<byte> source);
    private delegate void Ofb(ReadOnlySpan<byte> input, Span<byte> output, byte[] key, byte[] iv);
    private delegate void AriseTransform(ReadOnlySpan<byte> source, Span<byte> destination, bool encrypt);

    private static readonly byte[] Data = Enumerable.Range(0, 4096 + 37).Select(i => (byte)(i * 31 + 7)).ToArray();
    private static readonly byte[] Key16 = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");


    [JSExport]
    public static string Run()
    {
        var output = new StringBuilder();
        output.AppendLine($"IsBrowser={OperatingSystem.IsBrowser()} Framework={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Check(output, "platform MD5.HashData", () => MD5.HashData(Data));
        Check(output, "platform IncrementalHash(MD5)", () => { using var h = IncrementalHash.CreateHash(HashAlgorithmName.MD5); h.AppendData(Data); return h.GetHashAndReset(); });
        Check(output, "platform HMACMD5.HashData", () => HMACMD5.HashData(Key16, Data));
        Check(output, "platform SHA1.HashData", () => SHA1.HashData(Data));
        Check(output, "platform SHA256.HashData", () => SHA256.HashData(Data));
        Check(output, "platform SHA384.HashData", () => SHA384.HashData(Data));
        Check(output, "platform SHA512.HashData", () => SHA512.HashData(Data));
        Check(output, "platform IncrementalHash(SHA256)", () => { using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); h.AppendData(Data); return h.GetHashAndReset(); });
        Check(output, "platform HMACSHA256.HashData", () => HMACSHA256.HashData(Key16, Data));
        Check(output, "platform RandomNumberGenerator", () => RandomNumberGenerator.GetBytes(16).Length == 16 ? [1] : [0]);
        Check(output, "platform Aes.Create ECB", () => { using var aes = Aes.Create(); aes.Key = Key16; return aes.EncryptEcb(Data.AsSpan(0, 4096), PaddingMode.None); });
        Check(output, "platform Aes.Create CBC", () => { using var aes = Aes.Create(); aes.Key = Key16; return aes.DecryptCbc(Data.AsSpan(0, 4096), Key16, PaddingMode.None); });
#if AFTER_FIX
        Check(output, "sdk PortableMd5.HashData", () => SaveToolkit.Sdk.Crypto.PortableMd5.HashData(Data));
        Check(output, "sdk PortableMd5 abc (expect 900150983CD24FB0D6963F7D28E17F72)", () => SaveToolkit.Sdk.Crypto.PortableMd5.HashData("abc"u8));
        Check(output, "sdk IncrementalMd5", () => { using var h = SaveToolkit.Sdk.Crypto.IncrementalMd5.Create(); h.AppendData(Data.AsSpan(0, 100)); h.AppendData(Data.AsSpan(100)); return h.GetHashAndReset(); });
        Check(output, "sdk PortableAes ECB", () => { using var aes = SaveToolkit.Sdk.Crypto.PortableAes.Create(Key16); var o = new byte[4096]; aes.EncryptEcb(Data.AsSpan(0, 4096), o); return o; });
        Check(output, "sdk PortableAes CBC", () => { using var aes = SaveToolkit.Sdk.Crypto.PortableAes.Create(Key16); var o = new byte[4096]; aes.DecryptCbc(Key16, Data.AsSpan(0, 4096), o); return o; });
        Check(output, "sdk FIPS-197 C.1 (expect 69C4E0D86A7B0430D8CDB78070B4C55A)", () =>
        {
            using var aes = SaveToolkit.Sdk.Crypto.PortableAes.Create(Key16);
            var o = new byte[16];
            aes.EncryptEcb(Convert.FromHexString("00112233445566778899AABBCCDDEEFF"), o);
            return o;
        });
        Check(output, "sdk PortableAes.IsManaged", () => { using var aes = SaveToolkit.Sdk.Crypto.PortableAes.Create(Key16); return [aes.IsManaged ? (byte)1 : (byte)0]; });
#endif
        PluginChecks(output);
        return output.ToString();
    }

    private static void PluginChecks(StringBuilder output)
    {
        var ragnarok = Type("GodOfWarRagnarok", "RagnarokAuthentication");
        var digest = ragnarok.GetMethod("Calculate", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Digest>();
        Check(output, "plugin GodOfWarRagnarok MD5", () => digest("MD5", Data));

        var lspk = Type("BaldursGate3", "LspkIntegrity").GetMethod("Compute", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(output, "plugin BaldursGate3 LspkIntegrity", () =>
            lspk.CreateDelegate<Func<IEnumerable<byte[]>, byte[]>>()([Data.AsSpan(0, 1000).ToArray(), Data.AsSpan(1000).ToArray()]));

        var ofb = Type("Pragmata", "Crypto.AesOfb").GetMethod("Transform", BindingFlags.Static | BindingFlags.Public)!.CreateDelegate<Ofb>();
        Check(output, "plugin Pragmata AesOfb", () =>
        {
            var o = new byte[Data.Length];
            ofb(Data, o, Key16, Key16);
            var back = new byte[o.Length];
            ofb(o, back, Key16, Key16);
            return back.AsSpan().SequenceEqual(Data) ? o : throw new InvalidOperationException("OFB round trip mismatch");
        });

        var ariseType = Type("TalesOfArise", "AriseCipher");
        Check(output, "plugin TalesOfArise AriseCipher", () =>
        {
            var input = Data.AsSpan(0, 4096 + 32).ToArray();
            var cipher = new byte[input.Length];
            var plain = new byte[input.Length];
            for (var section = 0; section < 3; section++)
            {
                using (var encryptor = (IDisposable)Activator.CreateInstance(ariseType, nonPublic: true)!)
                using (var decryptor = (IDisposable)Activator.CreateInstance(ariseType, nonPublic: true)!)
                {
                    Transform(encryptor)(input, cipher, true);
                    Transform(decryptor)(cipher, plain, false);
                }
            }
            return plain.AsSpan().SequenceEqual(input) ? cipher : throw new InvalidOperationException("Arise round trip mismatch");
            AriseTransform Transform(object target) =>
                ariseType.GetMethod("Transform", BindingFlags.Instance | BindingFlags.Public)!.CreateDelegate<AriseTransform>(target);
        });

        var decode = Type("EldenRing", "RegulationDecoder").GetMethod("Decode", BindingFlags.Static | BindingFlags.Public)!.CreateDelegate<SpanToBytes>();
        Check(output, "plugin EldenRing RegulationDecoder", () =>
        {
            var allocation = EldenAllocation(out var expected);
            var decoded = decode(allocation);
            return decoded.AsSpan().SequenceEqual(expected) ? decoded : throw new InvalidOperationException("Regulation round trip mismatch");
        });
    }

    private static byte[] EldenAllocation(out byte[] expected)
    {
        expected = Data;
        var header = Convert.FromHexString("44435800000110000000001800000024000000440000004C444353000000000000000000"
            + "4443500044464C540000002009000000000000000F00000000000000000101004443410000000008");
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(Data);
        var body = compressed.ToArray();
        var plain = new byte[(header.Length + body.Length + 15) / 16 * 16];
        header.CopyTo(plain, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(0x1C), (uint)Data.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(0x20), (uint)body.Length);
        body.CopyTo(plain, header.Length);
        var key = Convert.FromHexString("99BFFC366A6BC8C6F5827D093602D676C42892A01C207FB024D3AF4E493FEF99");
        var iv = Convert.FromHexString("F0E1D2C3B4A5968778695A4B3C2D1E0F");
        var allocation = new byte[32 + plain.Length];
        iv.CopyTo(allocation, 16);
#if AFTER_FIX
        using var aes = SaveToolkit.Sdk.Crypto.PortableAes.Create(key);
        aes.EncryptCbc(iv, plain, allocation.AsSpan(32));
#else
        _ = key;
        plain.CopyTo(allocation, 32);
#endif
        return allocation;
    }

    private static Type Type(string plugin, string name) =>
        Assembly.Load("SaveToolkit.Plugins." + plugin).GetType("SaveToolkit.Plugins." + plugin + "." + name, throwOnError: true)!;

    private static void Check(StringBuilder output, string name, Func<byte[]> action)
    {
        try
        {
            var result = action();
            output.AppendLine($"OK     {name}: sha256={Convert.ToHexString(SHA256.HashData(result))[..16]} head={Convert.ToHexString(result.AsSpan(0, Math.Min(16, result.Length)))}");
        }
        catch (Exception exception)
        {
            output.AppendLine($"THROWS {name}: {exception.GetType().Name}: {exception.Message.Split('\n')[0]}");
        }
    }
}
