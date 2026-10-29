using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DdlCorruptionTests
{
    [Theory]
    [InlineData("CareerBolts", 0x4A96B708u)]
    [InlineData("Playtime", 0x06DFCC00u)]
    [InlineData("Difficulty", 0x605D31D6u)]
    [InlineData("Checkpoint", RiftPlayerView.CampaignTag)]
    [InlineData("", 0xEDB88320u)]
    public void EngineNameHashMatchesPublishedSchemaAndSampleIdentifiers(string name, uint expected) =>
        Assert.Equal(expected, DdlReader.NameHash(System.Text.Encoding.UTF8.GetBytes(name)));

    [Theory]
    [InlineData("magic", "Invalid object header or bounds")]
    [InlineData("size", "Invalid object header or bounds")]
    [InlineData("field-count", "Invalid object header or bounds")]
    [InlineData("value-count", "Unsupported field count or array kind")]
    [InlineData("name-bounds", "Field name points outside the string table")]
    [InlineData("name-hash", "Field name does not match its identifier hash")]
    [InlineData("type", "Unsupported field count or array kind")]
    [InlineData("nan", "Non-finite number")]
    [InlineData("bool", "Boolean is neither zero nor one")]
    [InlineData("terminator", "String terminator is missing")]
    [InlineData("padding", "Unexpected bytes after the object fields")]
    public void CorruptRootDdlIsRejected(string corruption, string message)
    {
        var fixture = DdlTestFixture.Root();
        var bytes = fixture.Bytes;
        var start = fixture.SectionOffset;
        switch (corruption)
        {
            case "magic": bytes[start + 4] ^= 1; break;
            case "size": Write(start + 12, (uint)bytes.Length); break;
            case "field-count": Write(start + 8, uint.MaxValue); break;
            case "value-count": Write(start + 20, 0x0DFFFFF0); break;
            case "name-bounds": Write(fixture.NamePointerOffset(0), (uint)bytes.Length); break;
            case "name-hash": bytes[start + 16] ^= 1; break;
            case "type": Write(start + 20, 0xFE000010); break;
            case "nan": Write(fixture.ValueOffset(1), 0x7FC00000); break;
            case "bool": bytes[fixture.ValueOffset(2)] = 2; break;
            case "terminator":
                var text = fixture.ValueOffset(3);
                var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(text));
                bytes[text + 16 + length] = 1;
                break;
            case "padding": bytes[^1] = 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        var error = Assert.Throws<SaveFormatException>(() => new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", bytes)));
        Assert.Contains($"DDL section 0x{DdlTestFixture.SectionTag:X8}:", error.Message);
        Assert.Contains(message, error.Message);
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    [Fact]
    public void UnmodifiedSyntheticRootHasExpectedValuesAndRoundTripsExactly()
    {
        var fixture = DdlTestFixture.Root();
        var fields = NestedDdlTests.Read(fixture.Bytes);
        Assert.Equal(5, fields.Count);
        Assert.Equal(0x12345678L, Assert.Single(fields["CareerBolts"].Values).AsInt64());
        Assert.Equal(1.5, Assert.Single(fields["Playtime"].Values).AsDouble());
        Assert.True(Assert.Single(fields["IsChallengeMode"].Values).AsBool());
        Assert.Equal("abc", Assert.Single(fields["Version"].Values).AsString());
        Assert.Empty(fields["Empty"].Values);
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", fixture.Bytes));
        Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
