using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class RegulationCorruptionTests
{
    private const int ParamOffset = 160;
    private const int ParamTypeOffset = 144;

    [Fact]
    public void EveryTruncatedPrefixOfTheSyntheticBinderIsRejected()
    {
        var bytes = RegulationTests.Binder();
        for (var length = 0; length < bytes.Length; length++)
        {
            var prefixLength = length;
            Assert.Throws<SaveFormatException>(() => RegulationArchiveReader.Read(bytes.AsSpan(0, prefixLength)));
        }
        Assert.Single(RegulationArchiveReader.Read(bytes).Tables);
    }

    [Fact]
    public void EveryTruncatedDcxPrefixIsRejectedEvenWithTheCompressedLengthRepaired()
    {
        var bytes = RegulationTests.Dcx("BND4 synthetic payload"u8.ToArray());
        for (var length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes[..length];
            if (length >= RegulationDecoder.DcxHeaderSize)
                BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(0x20), (uint)(length - RegulationDecoder.DcxHeaderSize));
            Assert.Throws<SaveFormatException>(() => RegulationDecoder.Inflate(prefix));
        }
        Assert.Equal("BND4 synthetic payload"u8.ToArray(), RegulationDecoder.Inflate(bytes));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IndependentStoredBlockVectorRequiresEveryAdlerChecksumByte(int checksumByte)
    {
        var header = RegulationTests.Dcx("x"u8.ToArray()).AsSpan(0, RegulationDecoder.DcxHeaderSize).ToArray();
        byte[] compressed = [0x78, 0x01, 0x01, 0x01, 0x00, 0xFE, 0xFF, 0x78, 0x00, 0x79, 0x00, 0x79];
        if (checksumByte >= 0) compressed[compressed.Length - sizeof(uint) + checksumByte] ^= 1;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x20), (uint)compressed.Length);
        byte[] dcx = [.. header, .. compressed];
        if (checksumByte >= 0)
            Assert.Throws<SaveFormatException>(() => RegulationDecoder.Inflate(dcx));
        else
            Assert.Equal("x"u8.ToArray(), RegulationDecoder.Inflate(dcx));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(47)]
    [InlineData(49)]
    [InlineData(EldenRingLayout.RegulationSize + 16)]
    public void InvalidAesFramingFailsWithTheDocumentedException(int length) =>
        Assert.Throws<SaveFormatException>(() => RegulationDecoder.Decode(new byte[length]));

    [Theory]
    [InlineData("metadata-end")]
    [InlineData("hash-offset")]
    [InlineData("zero-payload")]
    [InlineData("huge-payload")]
    [InlineData("payload-in-metadata")]
    [InlineData("unterminated-name")]
    [InlineData("invalid-utf16")]
    [InlineData("type-in-directory")]
    [InlineData("type-outside-table")]
    [InlineData("unterminated-type")]
    [InlineData("row-in-directory")]
    [InlineData("row-in-strings")]
    [InlineData("name-before-strings")]
    [InlineData("name-at-end")]
    public void BinderAndParamBoundaryCorruptionUsesTheDocumentedRejection(string mutation)
    {
        var bytes = RegulationTests.Binder();
        switch (mutation)
        {
            case "metadata-end": Write64(bytes, 0x28, ulong.MaxValue); break;
            case "hash-offset": Write64(bytes, 0x38, ulong.MaxValue); break;
            case "zero-payload": Write64(bytes, 72, 0); Write64(bytes, 80, 0); break;
            case "huge-payload": Write64(bytes, 72, ulong.MaxValue); Write64(bytes, 80, ulong.MaxValue); break;
            case "payload-in-metadata": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(88), 159); break;
            case "unterminated-name": bytes.AsSpan(100, 60).Fill(0x41); break;
            case "invalid-utf16": bytes[100] = 0; bytes[101] = 0xD8; bytes[102] = 0; bytes[103] = 0; break;
            case "type-in-directory": Write64(bytes, ParamOffset + 16, 111); break;
            case "type-outside-table": Write64(bytes, ParamOffset + 16, ulong.MaxValue); break;
            case "unterminated-type": bytes.AsSpan(ParamOffset + ParamTypeOffset).Fill(0x41); break;
            case "row-in-directory": Write64(bytes, ParamOffset + 72, 111); break;
            case "row-in-strings": Write64(bytes, ParamOffset + 72, ParamTypeOffset); break;
            case "name-before-strings": Write64(bytes, ParamOffset + 80, ParamTypeOffset - 1); break;
            case "name-at-end": Write64(bytes, ParamOffset + 80, (ulong)(bytes.Length - ParamOffset)); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.Throws<SaveFormatException>(() => RegulationArchiveReader.Read(bytes));
    }

    private static void Write64(byte[] bytes, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
}
