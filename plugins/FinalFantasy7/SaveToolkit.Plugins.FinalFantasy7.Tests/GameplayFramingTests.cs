using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class GameplayFramingTests
{
    private static readonly string[] Titles = ["PPSA01911", "PPSA08664", "PPSA18936"];

    [Theory]
    [InlineData("PPSA01911", 24)]
    [InlineData("PPSA08664", 275)]
    [InlineData("PPSA18936", 275)]
    public void GeneratedGameplayWalksEveryRecordFamilyAndPreservesBodies(string title, int count)
    {
        var fixture = SyntheticGameplay.Create(title);
        var schema = Ff7Schema.ForTitle(title)!;
        var records = LinkedRecords.Parse(fixture.Bytes, schema, common: false);
        Assert.Equal(count, records.Count);
        Assert.Equal(fixture.Bytes.Length, records.Sum(record => record.Length));
        for (var index = 0; index < records.Count; index++)
        {
            var expected = fixture.Records[index];
            var actual = records[index];
            Assert.Equal(index, actual.Index);
            Assert.Equal(expected.Offset, actual.Offset);
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Tag, actual.Tag);
            Assert.Equal(expected.Version, actual.Version);
            Assert.Equal(expected.Empty, actual.IsEmpty);
            Assert.Equal(index == count - 1, actual.IsTail);
            Assert.Equal(index == count - 1 ? 0U : (uint)(expected.Offset + expected.Length), actual.Next);
        }

        var plugin = new FinalFantasy7Plugin();
        var file = MemorySaveSet.SingleFile(schema.Prefix + "000", fixture.Bytes, title);
        Assert.True(plugin.Probe(file).IsMatch);
        using var document = plugin.Open(file);
        Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(fixture.Bytes, file.ReadAllBytes());
    }

    public static IEnumerable<object[]> Corruptions()
    {
        foreach (var title in Titles)
        {
            // Each case is independently reported, even when another mutation fails.
            (int Record, int Offset, uint Value)[] commonCases =
            [
                (0, 4, 0), (0, 4, uint.MaxValue), (0, 4, 16), (0, 8, uint.MaxValue),
                (0, 12, 2), (0, 16, 0), (0, 32, 99), (0, 36, uint.MaxValue),
                (1, 0, 0x00010004), (1, 0, 0x00020001), (1, 0, 0x01010001),
                (1, 4, 0), (1, 4, 16), (1, 8, uint.MaxValue), (1, 12, 2),
                (1, 20, 0), (1, 28, 0), (1, 32, 99), (1, 36, 0),
                (2, 16, 1), (2, 32, 1), (2, 36, 1),
                (-1, 0, 0), (-1, 4, 16), (-1, 8, uint.MaxValue), (-1, 12, 99),
            ];
            foreach (var (record, offset, value) in commonCases)
                yield return [title, record, offset, value];

            // The first-header gate cannot catch corruption in these later families.
            var laterRecords = title == "PPSA01911" ? new[] { 19, 20, 22 } : [81, 82, 162, 170, 178, 226, 273];
            foreach (var record in laterRecords)
            {
                yield return [title, record, 0, 0x00010005U];
                yield return [title, record, 24, 0U];
                yield return [title, record, 32, uint.MaxValue];
                yield return [title, record, 36, uint.MaxValue];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void GeneratedGameplayRejectsCorruptionWithoutPrivateSamples(string title, int record, int relativeOffset, uint value)
    {
        var fixture = SyntheticGameplay.Create(title);
        var index = record < 0 ? fixture.Records.Count - 1 : record;
        var offset = fixture.Records[index].Offset + relativeOffset;
        Assert.NotEqual(value, BinaryPrimitives.ReadUInt32LittleEndian(fixture.Bytes.AsSpan(offset)));
        BinaryPrimitives.WriteUInt32LittleEndian(fixture.Bytes.AsSpan(offset), value);
        var file = MemorySaveSet.SingleFile(Ff7Schema.ForTitle(title)!.Prefix + "000", fixture.Bytes, title);
        Assert.Throws<SaveFormatException>(() => new FinalFantasy7Plugin().Open(file));
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void GameplayRejectsSelfLinksOverlapsAndWrongFileLengths(string title)
    {
        var fixture = SyntheticGameplay.Create(title);
        var schema = Ff7Schema.ForTitle(title)!;
        var offset = fixture.Records[1].Offset;
        foreach (var next in new[] { (uint)offset, (uint)(offset + 16), (uint)fixture.Bytes.Length })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.Bytes.AsSpan(offset + 4), next);
            Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(fixture.Bytes, schema, common: false));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(fixture.Bytes.AsSpan(offset + 4), (uint)fixture.Records[2].Offset);
        foreach (var length in new[] { 0, 39, fixture.Bytes.Length - 16, fixture.Bytes.Length - 1, fixture.Bytes.Length + 1 })
        {
            var bytes = new byte[length];
            fixture.Bytes.AsSpan(0, Math.Min(length, fixture.Bytes.Length)).CopyTo(bytes);
            var file = MemorySaveSet.SingleFile(schema.Prefix + "000", bytes, title);
            Assert.False(new FinalFantasy7Plugin().Probe(file).IsMatch);
            Assert.Throws<SaveFormatException>(() => new FinalFantasy7Plugin().Open(file));
        }
    }

    [Theory]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void MlsdAndNvsdUpperVersionBytesAreRetained(string title)
    {
        var fixture = SyntheticGameplay.Create(title);
        foreach (var record in fixture.Records.Where(record => record.Tag is "MLSD" or "NVSD"))
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.Bytes.AsSpan(record.Offset + 32), record.Version | 0xA5B6C700);
        var schema = Ff7Schema.ForTitle(title)!;
        var records = LinkedRecords.Parse(fixture.Bytes, schema, common: false);
        Assert.Equal(fixture.Records.Count(record => record.Tag is "MLSD" or "NVSD"),
            records.Count(record => (record.Version & 0xFFFFFF00) == 0xA5B6C700));
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(schema.Prefix + "000", fixture.Bytes, title));
        Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
