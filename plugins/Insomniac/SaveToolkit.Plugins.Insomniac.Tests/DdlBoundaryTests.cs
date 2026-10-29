using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DdlBoundaryTests
{
    [Theory]
    [InlineData("UInt8", "00", "0")]
    [InlineData("UInt8", "FF", "255")]
    [InlineData("UInt16", "0000", "0")]
    [InlineData("UInt16", "FFFF", "65535")]
    [InlineData("UInt32", "00000000", "0")]
    [InlineData("UInt32", "FFFFFFFF", "4294967295")]
    [InlineData("UInt64", "0000000000000000", "0")]
    [InlineData("UInt64", "FFFFFFFFFFFFFFFF", "18446744073709551615")]
    [InlineData("Int8", "80", "-128")]
    [InlineData("Int8", "7F", "127")]
    [InlineData("Int16", "0080", "-32768")]
    [InlineData("Int16", "FF7F", "32767")]
    [InlineData("Int32", "00000080", "-2147483648")]
    [InlineData("Int32", "FFFFFF7F", "2147483647")]
    [InlineData("Int64", "0000000000000080", "-9223372036854775808")]
    [InlineData("Int64", "FFFFFFFFFFFFFF7F", "9223372036854775807")]
    [InlineData("Identifier", "FFFFFFFFFFFFFFFF", "18446744073709551615")]
    [InlineData("Asset", "0100000000002000", "9007199254740993")]
    public void IntegerWireWidthsAndSignednessRemainExact(string kind, string hex, string expected)
    {
        var fixture = Scalar(kind, Convert.FromHexString(hex));
        var field = NestedDdlTests.Read(fixture.Bytes)["Value"];
        Assert.Equal(Enum.Parse<DdlValueKind>(kind), field.Kind);
        Assert.Equal(fixture.ValuesOffset, field.Offset);
        Assert.Equal(Int128.Parse(expected, CultureInfo.InvariantCulture), Assert.Single(field.Values).AsInteger());
    }

    [Theory]
    [InlineData("Float", "0000C07F")]
    [InlineData("Float", "0000807F")]
    [InlineData("Float", "000080FF")]
    [InlineData("Double", "000000000000F87F")]
    [InlineData("Double", "000000000000F07F")]
    [InlineData("Double", "000000000000F0FF")]
    public void EveryNonFiniteNumberIsRejected(string kind, string hex) =>
        Reject(Scalar(kind, Convert.FromHexString(hex)), "Non-finite number");

    [Theory]
    [InlineData("Float", "0000C03F", 1.5)]
    [InlineData("Float", "0000C0BF", -1.5)]
    [InlineData("Double", "000000000000F83F", 1.5)]
    [InlineData("Double", "000000000000F8BF", -1.5)]
    public void FiniteNumbersRetainTheirStoredPrecision(string kind, string hex, double expected) =>
        Assert.Equal(expected, Assert.Single(NestedDdlTests.Read(Scalar(kind, Convert.FromHexString(hex)).Bytes)["Value"].Values).AsDouble());

    [Theory]
    [InlineData("UInt8", 1)]
    [InlineData("UInt16", 2)]
    [InlineData("UInt32", 4)]
    [InlineData("UInt64", 8)]
    [InlineData("Int8", 1)]
    [InlineData("Int16", 2)]
    [InlineData("Int32", 4)]
    [InlineData("Int64", 8)]
    [InlineData("Float", 4)]
    [InlineData("Double", 8)]
    [InlineData("Bool", 1)]
    [InlineData("Identifier", 8)]
    [InlineData("Asset", 8)]
    public void ScalarCannotReadPastItsObjectEvenWhenDat1FramingIsValid(string kind, int width)
    {
        var fixture = Scalar(kind, new byte[width]);
        var bytes = fixture.Bytes[..(fixture.ValuesOffset + width - 1)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)(bytes.Length - fixture.SectionOffset));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(fixture.SectionOffset + 12),
            (uint)(bytes.Length - fixture.SectionOffset - DdlTestFixture.ObjectHeaderSize));
        Assert.Contains("Field exceeds the object bounds", Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(bytes)).Message);
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Enum")]
    [InlineData("Bitfield")]
    [InlineData("File")]
    [InlineData("Json")]
    public void StringKindsRetainEmptyAndMultibyteText(string kind)
    {
        foreach (var value in new[] { "", "é漢🎮", new string('a', 65536) })
        {
            var fixture = Scalar(kind, DdlTestFixture.StringPayload(value));
            Assert.Equal(value, Assert.Single(NestedDdlTests.Read(fixture.Bytes)["Value"].Values).AsString());
            using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", fixture.Bytes));
            Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("header", "Invalid object header")]
    [InlineData("array-kind", "Unsupported field count or array kind")]
    [InlineData("name-before-table", "outside the string table")]
    [InlineData("name-at-data", "outside the string table")]
    [InlineData("name-utf8", "Invalid DDL UTF-8")]
    [InlineData("string-utf8", "Invalid DDL UTF-8")]
    [InlineData("string-length", "String is too long")]
    [InlineData("string-bounds", "Field exceeds the object bounds")]
    [InlineData("null-count", "Invalid empty field")]
    [InlineData("null-byte", "Invalid empty field")]
    public void MalformedNamesDescriptorsAndStringsHaveSpecificFormatErrors(string corruption, string message)
    {
        var fixture = DdlTestFixture.Root();
        switch (corruption)
        {
            case "header": fixture.Write(fixture.SectionOffset, 1); break;
            case "array-kind": fixture.Bytes[fixture.DescriptorOffset(0) + 4] |= 4; break;
            case "name-before-table": fixture.Write(fixture.NamePointerOffset(0), 0); break;
            case "name-at-data": fixture.Write(fixture.NamePointerOffset(0), (uint)fixture.SectionOffset); break;
            case "name-utf8": fixture.Bytes[fixture.NameOffset(0)] = 0xFF; break;
            case "string-utf8": fixture.Bytes[fixture.ValueOffset(3) + DdlTestFixture.ObjectHeaderSize] = 0xFF; break;
            case "string-length": fixture.Write(fixture.ValueOffset(3), 65537); break;
            case "string-bounds": fixture.Write(fixture.ValueOffset(3), 1000); break;
            case "null-count": fixture.Bytes[fixture.DescriptorOffset(4) + 4] = 0x10; break;
            case "null-byte": fixture.Bytes[fixture.ValueOffset(4)] = 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        Reject(fixture, message);
    }

    [Fact]
    public void DuplicateFieldNamesAreRejectedRatherThanOverwritingTheFirstValue() =>
        Reject(new(new("Same", DdlValueKind.UInt8, [1]), new("Same", DdlValueKind.UInt8, [2])), "Duplicate field name");

    [Fact]
    public void EmptyObjectIsAcceptedAtTheExactSectionBoundary()
    {
        var fixture = new DdlTestFixture();
        Assert.Empty(NestedDdlTests.Read(fixture.Bytes));
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", fixture.Bytes));
        Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ArraysReadEveryPackedValueAndTheFollowingFieldAtItsExactOffset()
    {
        var fixture = new DdlTestFixture(
            new("Numbers", DdlValueKind.UInt16, [0x34, 0x12, 0xCD, 0xAB], Count: 2),
            new("Flags", DdlValueKind.Bool, [0, 1], Count: 2),
            new("Tail", DdlValueKind.UInt32, [0x78, 0x56, 0x34, 0x12]));
        var fields = NestedDdlTests.Read(fixture.Bytes);
        Assert.Equal(3, fields.Count);
        Assert.Equal(2, fields["Numbers"].Count);
        Assert.Equal([0x1234L, 0xABCDL], fields["Numbers"].Values.Select(value => value.AsInt64()));
        Assert.Equal([false, true], fields["Flags"].Values.Select(value => value.AsBool()));
        Assert.Equal(fixture.ValueOffset(2), fields["Tail"].Offset);
        Assert.Equal(0x12345678L, Assert.Single(fields["Tail"].Values).AsInt64());
    }

    private static DdlTestFixture Scalar(string kind, byte[] payload) =>
        new(new DdlTestFixture.Field("Value", Enum.Parse<DdlValueKind>(kind), payload));

    private static void Reject(DdlTestFixture fixture, string message)
    {
        var error = Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes));
        Assert.Contains(message, error.Message);
        // The public plugin boundary must reject the same malformed input.
        var publicError = Assert.Throws<SaveFormatException>(() =>
            new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", fixture.Bytes)));
        Assert.Contains(message, publicError.Message);
    }
}
