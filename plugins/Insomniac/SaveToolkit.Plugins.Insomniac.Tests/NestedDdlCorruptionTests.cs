using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class NestedDdlCorruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldNamesRequireNonemptyTerminatedTextInsideTheNameTable(bool emptyName)
    {
        var fixture = DdlTestFixture.Root();
        if (emptyName) fixture.Bytes[fixture.NameOffset(0)] = 0;
        else fixture.Bytes.AsSpan(fixture.NameOffset(0), fixture.SectionOffset - fixture.NameOffset(0)).Fill((byte)'x');
        Assert.Contains("Invalid field name", Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes)).Message);
    }

    [Fact]
    public void PackedNestedArraysPreserveEveryElementAndFollowingSibling()
    {
        var fixture = DdlCompoundFixture.NestedArrays();
        var fields = NestedDdlTests.Read(fixture.Bytes);
        var children = fields["Children"].Objects;
        Assert.Equal(2, children.Count);
        Assert.Equal(1, children[0].Offset % 4);
        Assert.Equal(["é🎮", "tail"], children[0].Fields["Strings"].Values.Select(v => v.AsString()));
        Assert.Equal([1L, 2L], children[0].Fields["Numbers"].Values.Select(v => v.AsInt64()));
        Assert.Equal("nested", Assert.Single(Assert.Single(children[1].Fields["Nested"].Objects).Fields["Strings"].Values).AsString());
        Assert.Equal(0x12345678L, Assert.Single(fields["Tail"].Values).AsInt64());
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", fixture.Bytes));
        Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("child-count", "Invalid object header")]
    [InlineData("child-table-size", "Invalid object header")]
    [InlineData("child-type", "Unsupported field count or array kind")]
    [InlineData("child-array-kind", "Unsupported field count or array kind")]
    [InlineData("child-name", "outside the string table")]
    [InlineData("child-array-count", "Field exceeds the object bounds")]
    [InlineData("child-string-length", "String is too long")]
    [InlineData("child-string-bounds", "Field exceeds the object bounds")]
    [InlineData("child-string-utf8", "Invalid DDL UTF-8")]
    [InlineData("child-string-terminator", "String terminator is missing")]
    [InlineData("grandchild-size", "Invalid object header")]
    [InlineData("grandchild-header", "Invalid object header")]
    public void NestedTypeTablesAndArraysRejectCorruptionInsideValidOuterFraming(string corruption, string message)
    {
        var fixture = DdlCompoundFixture.NestedArrays();
        var children = NestedDdlTests.Read(fixture.Bytes)["Children"].Objects;
        var child = children[0];
        var text = child.Fields["Strings"].Offset;
        var grandchild = Assert.Single(children[1].Fields["Nested"].Objects);
        switch (corruption)
        {
            case "child-count": fixture.Write(child.Offset + 8, uint.MaxValue); break;
            case "child-table-size": fixture.Write(child.Offset + 12, 23); break;
            case "child-type": fixture.Bytes[child.Offset + 23] = 14; break;
            case "child-array-kind": fixture.Bytes[child.Offset + 20] |= 4; break;
            case "child-name": fixture.Write(child.Offset + 32, (uint)fixture.SectionOffset); break;
            case "child-array-count": fixture.Write(child.Offset + 28, 0x010FFFF0); break;
            case "child-string-length": fixture.Write(text, uint.MaxValue); break;
            case "child-string-bounds": fixture.Write(text, 1000); break;
            case "child-string-utf8": fixture.Bytes[text + 16] = 0xFF; break;
            case "child-string-terminator": fixture.Bytes[text + 16 + 6] = 1; break;
            case "grandchild-size": fixture.Write(grandchild.Offset + 12, uint.MaxValue); break;
            case "grandchild-header": fixture.Write(grandchild.Offset, 1); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        Assert.Contains(message, Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes)).Message);
        Assert.Contains(message, Assert.Throws<SaveFormatException>(() =>
            new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", fixture.Bytes))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildCannotConsumeTheNextArrayElementOrParentSibling(bool shrinkChild)
    {
        var fixture = DdlCompoundFixture.NestedArrays();
        var fields = NestedDdlTests.Read(fixture.Bytes);
        var child = fields["Children"].Objects[0];
        if (shrinkChild)
            fixture.Write(child.Offset + 12, (uint)(child.Fields["Strings"].Offset - child.Offset - 16 + 15));
        else
            fixture.Write(child.Offset + 12, (uint)(fixture.Bytes.Length - child.Offset - 16));
        Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes));
    }
}
