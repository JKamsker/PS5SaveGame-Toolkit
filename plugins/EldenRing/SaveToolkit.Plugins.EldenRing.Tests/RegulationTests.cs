using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class RegulationTests
{
    [Fact]
    public void DfltInflation_MatchesDeclaredOutputExactly()
    {
        var payload = Encoding.ASCII.GetBytes("BND4 synthetic payload");
        Assert.Equal(payload, RegulationDecoder.Inflate(Dcx(payload)));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("codec")]
    [InlineData("small")]
    [InlineData("large")]
    [InlineData("budget")]
    [InlineData("truncated")]
    [InlineData("zlib")]
    public void DcxRejectsUnsupportedHeadersLyingSizesAndTruncation(string mutation)
    {
        var dcx = Dcx(Encoding.ASCII.GetBytes("BND4 synthetic payload"));
        switch (mutation)
        {
            case "header": dcx[0] = 0; break;
            case "codec": dcx[0x28] = (byte)'Z'; break;
            case "small": BinaryPrimitives.WriteUInt32BigEndian(dcx.AsSpan(0x1C), 1); break;
            case "large": BinaryPrimitives.WriteUInt32BigEndian(dcx.AsSpan(0x1C), 1000); break;
            case "budget": BinaryPrimitives.WriteUInt32BigEndian(dcx.AsSpan(0x1C), 64 * 1024 * 1024 + 1); break;
            case "truncated": dcx = dcx[..^2]; break;
            case "zlib": dcx[76] = 0; break;
        }
        Assert.Throws<SaveFormatException>(() => RegulationDecoder.Inflate(dcx));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DcxRejectsMissingZlibTrailerEvenWhenAllOutputWasProduced(int missingBytes)
    {
        var dcx = Dcx("BND4 synthetic payload"u8.ToArray());
        dcx = dcx[..^missingBytes];
        // Repair the enclosing length: this exercises zlib completion, not DCX bounds.
        BinaryPrimitives.WriteUInt32BigEndian(dcx.AsSpan(0x20), (uint)(dcx.Length - RegulationDecoder.DcxHeaderSize));
        var error = Assert.Throws<SaveFormatException>(() => RegulationDecoder.Inflate(dcx));
        Assert.Contains("zlib", error.Message);
    }

    [Fact]
    public void DcxRejectsAnUnfinishedDeflateBlockAfterProducingTheDeclaredOutput()
    {
        var dcx = Dcx("x"u8.ToArray());
        // Independent zlib stored-block vector for "x", with BFINAL deliberately cleared.
        // Output and Adler-32 remain correct, but another DEFLATE block is required.
        byte[] compressed = [0x78, 0x01, 0x00, 0x01, 0x00, 0xFE, 0xFF, 0x78, 0x00, 0x79, 0x00, 0x79];
        dcx = [.. dcx.AsSpan(0, RegulationDecoder.DcxHeaderSize), .. compressed];
        BinaryPrimitives.WriteUInt32BigEndian(dcx.AsSpan(0x20), (uint)compressed.Length);
        Assert.Throws<SaveFormatException>(() => RegulationDecoder.Inflate(dcx));
    }

    [Fact]
    public void BndAndParamDirectories_AcceptDuplicateNumericRowsAndIgnoreStaleLegacyStringWord()
    {
        var archive = RegulationArchiveReader.Read(Binder());
        var table = Assert.Single(archive.Tables);
        Assert.Equal("test.param", table.Name);
        Assert.Equal(2, table.Rows);
        Assert.Equal(3, table.DataVersion);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("format")]
    [InlineData("payload")]
    [InlineData("name")]
    [InlineData("compression")]
    [InlineData("param-rows")]
    [InlineData("param-data")]
    [InlineData("param-name")]
    [InlineData("param-schema")]
    public void BndAndParamDirectories_RejectMalformedBoundsAndSchemas(string mutation)
    {
        var bytes = Binder();
        switch (mutation)
        {
            case "count": Write32(bytes, 12, uint.MaxValue); break;
            case "format": bytes[0x31] = 0; break;
            case "payload": Write32(bytes, 64 + 24, uint.MaxValue); break;
            case "name": Write32(bytes, 64 + 32, uint.MaxValue); break;
            case "compression": bytes[64] = 0x80; break;
            case "param-rows": BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(160 + 10), ushort.MaxValue); break;
            case "param-data": Write64(bytes, 160 + 64 + 8, ulong.MaxValue); break;
            case "param-name": Write64(bytes, 160 + 64 + 16, ulong.MaxValue); break;
            case "param-schema": bytes[160 + 0x2D] = 0; break;
        }
        Assert.Throws<SaveFormatException>(() => RegulationArchiveReader.Read(bytes));
    }

    [Fact]
    public void UnsupportedRegulation_LeavesOtherViewsAndExportAvailable()
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        var message = Assert.IsType<TextNode>(Assert.Single(document.ExpandNode("regulation/metadata")));
        Assert.Equal(Tone.Warning, message.Tone);
        Assert.Contains("preserved", message.Text);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        Assert.Throws<SaveFormatException>(() => RegulationDecoder.Decode(new byte[17]));
    }

    private static byte[] Dcx(byte[] payload)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) zlib.Write(payload);
        var header = Convert.FromHexString("44435800000110000000001800000024000000440000004C4443530000000000000000004443500044464C540000002009000000000000000F00000000000000000101004443410000000008");
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x1C), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x20), (uint)compressed.Length);
        return [.. header, .. compressed.ToArray()];
    }

    private static byte[] Binder()
    {
        var bytes = new byte[320];
        "BND4"u8.CopyTo(bytes);
        bytes[10] = 1;
        Write32(bytes, 12, 1);
        Write64(bytes, 0x10, 64);
        "11001000"u8.CopyTo(bytes.AsSpan(0x18));
        Write64(bytes, 0x20, 36);
        Write64(bytes, 0x28, 160);
        bytes[0x30] = 1; bytes[0x31] = 0x74; bytes[0x32] = 4;
        Write64(bytes, 0x38, 144);
        bytes[64] = 0x40;
        Write32(bytes, 68, uint.MaxValue);
        Write64(bytes, 72, 160); Write64(bytes, 80, 160);
        Write32(bytes, 88, 160); Write32(bytes, 96, 100);
        Encoding.Unicode.GetBytes("test.param\0").CopyTo(bytes, 100);
        const int param = 160;
        Write32(bytes, param, uint.MaxValue); // stale legacy word; offset flag 0x80 selects the type pointer.
        Write64(bytes, param + 16, 144);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(param + 8), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(param + 10), 2);
        bytes[param + 0x2D] = 0x85; bytes[param + 0x2E] = 7;
        Write32(bytes, param + 64, 100);
        Write64(bytes, param + 72, 112);
        Write32(bytes, param + 88, 100); // deliberate duplicate row id; physical rows remain distinct.
        Write64(bytes, param + 96, 128);
        "TEST_PARAM\0"u8.CopyTo(bytes.AsSpan(param + 144));
        return bytes;
    }

    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Write64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
}
