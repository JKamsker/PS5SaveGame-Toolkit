using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing;

// Public game-format key/IV framing: ClayAmore 014107f0 util/regulation.rs (MIT).
// This is not a user credential. Only the sampled DCX/DFLT variant is supported.
internal static class RegulationDecoder
{
    internal const int MaximumDecodedSize = 64 * 1024 * 1024;
    internal const int DcxHeaderSize = 0x4C;
    private const int CipherBlockSize = 16;
    private const int OpaquePrefixSize = 16;
    private const string PublicFormatKey = "99BFFC366A6BC8C6F5827D093602D676C42892A01C207FB024D3AF4E493FEF99";
    private const string HeaderTemplate = "44435800000110000000001800000024000000440000004C444353000000000000000000"
        + "4443500044464C540000002009000000000000000F00000000000000000101004443410000000008";

    public static byte[] Decode(ReadOnlySpan<byte> allocation)
    {
        if (allocation.Length > EldenRingLayout.RegulationSize || allocation.Length < OpaquePrefixSize + CipherBlockSize * 2
            || (allocation.Length - OpaquePrefixSize - CipherBlockSize) % CipherBlockSize != 0)
            throw new SaveFormatException("Regulation allocation has invalid AES block framing.");
        using var aes = Aes.Create();
        aes.Key = Convert.FromHexString(PublicFormatKey);
        var plaintext = aes.DecryptCbc(allocation[(OpaquePrefixSize + CipherBlockSize)..],
            allocation.Slice(OpaquePrefixSize, CipherBlockSize), PaddingMode.None);
        return Inflate(plaintext);
    }

    internal static byte[] Inflate(ReadOnlySpan<byte> dcx)
    {
        var template = Convert.FromHexString(HeaderTemplate);
        if (dcx.Length < DcxHeaderSize || template.Length != DcxHeaderSize
            || !dcx[..0x1C].SequenceEqual(template.AsSpan(0, 0x1C))
            || !dcx.Slice(0x24, DcxHeaderSize - 0x24).SequenceEqual(template.AsSpan(0x24)))
            throw new SaveFormatException("Unsupported regulation DCX header; only sampled DFLT is decoded.");
        var decodedSize = BinaryPrimitives.ReadUInt32BigEndian(dcx[0x1C..]);
        var compressedSize = BinaryPrimitives.ReadUInt32BigEndian(dcx[0x20..]);
        if (decodedSize == 0 || decodedSize > MaximumDecodedSize || compressedSize == 0
            || compressedSize > dcx.Length - DcxHeaderSize)
            throw new SaveFormatException("Regulation DCX sizes exceed their bounded allocation.");
        try
        {
            using var input = new MemoryStream(dcx.Slice(DcxHeaderSize, (int)compressedSize).ToArray(), false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            var output = new byte[(int)decodedSize];
            zlib.ReadExactly(output);
            if (zlib.ReadByte() != -1)
                throw new SaveFormatException("Regulation decompression exceeds its declared size.");
            return output;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            throw new SaveFormatException("Invalid or truncated regulation zlib stream.", exception);
        }
    }
}
