using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ParserPrecedenceTests
{
    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void MultipleFaultsKeepKindPointerEnvelopeContentAndChainOrder(string title)
    {
        var fixture = SyntheticGameplay.Create(title);
        var schema = Ff7Schema.ForTitle(title)!;
        var offset = fixture.Records[1].Offset;
        var original = fixture.Bytes.AsSpan(offset, 40).ToArray();
        Write(0, 0x01020005);
        Write(4, 0);
        Write(8, uint.MaxValue);
        Write(12, 2);
        Write(20, 0);
        Write(32, 99);
        Write(36, 0);
        AssertReason("unexpected record kind or premature tail");
        original.AsSpan(0, 4).CopyTo(fixture.Bytes.AsSpan(offset));
        AssertReason("next record pointer is outside the supported record boundary");
        original.AsSpan(4, 4).CopyTo(fixture.Bytes.AsSpan(offset + 4));
        AssertReason("invalid record envelope");
        original.AsSpan(8, 8).CopyTo(fixture.Bytes.AsSpan(offset + 8));
        AssertReason("invalid repeated tag, version or content length");
        original.CopyTo(fixture.Bytes, offset);
        Write(0, 0x00010005, fixture.Records[3].Offset);
        Assert.Equal($"FFVII record at 0x{fixture.Records[3].Offset:X}: unexpected record kind or premature tail.",
            Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(fixture.Bytes, schema, false)).Message);

        void Write(int relative, uint value, int? start = null) =>
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.Bytes.AsSpan((start ?? offset) + relative), value);
        void AssertReason(string reason) => Assert.Equal($"FFVII record at 0x{offset:X}: {reason}.",
            Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(fixture.Bytes, schema, false)).Message);
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void CommonHeaderGatePrecedesContentAndTerminalValidation(string title)
    {
        var bytes = ContainerTests.Common(title);
        var schema = Ff7Schema.ForTitle(title)!;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 99);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x414), uint.MaxValue);
        Assert.Equal("FFVII record at 0x0: invalid repeated tag, version or content length.", Reject());
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0);
        Assert.Equal("FFVII file header or size does not match the selected PS5 schema.", Reject());
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x410);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 1);
        Assert.Equal("FFVII record at 0x410: invalid terminal record.", Reject());

        string Reject() => Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(bytes, schema, true)).Message;
    }
}
