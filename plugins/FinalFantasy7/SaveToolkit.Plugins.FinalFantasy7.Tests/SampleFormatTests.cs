using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class SampleFormatTests
{
    [Theory]
    [InlineData("PPSA01911", 24)]
    [InlineData("PPSA08664", 275)]
    [InlineData("PPSA18936", 275)]
    public void EverySampleIncludingBackupsAndGenerations_WalksAndRoundTrips(string title, int gameRecords)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var plugin = new FinalFantasy7Plugin();
        var files = SampleSaves.Load(title).AllDiskFiles
            .Where(f => !f.IsSystemFile && schema.TryGetRole(f.FileName, out _)).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.True(schema.TryGetRole(file.FileName, out var common));
            Assert.True(plugin.Probe(file).IsMatch, file.RelativePath);
            var bytes = file.ReadAllBytes();
            var records = LinkedRecords.Parse(bytes, schema, common);
            Assert.Equal(common ? 2 : gameRecords, records.Count);
            Assert.Equal(bytes.Length - 16, records[^1].Offset);
            Assert.True(records[^1].IsTail);
            Assert.Equal(bytes.Length, records.Sum(r => r.Length));
            for (var i = 1; i < records.Count; i++)
            {
                Assert.Equal(records[i].Offset, (int)records[i - 1].Next);
            }

            using var document = plugin.Open(file);
            Assert.Equal(!common, document.CanEdit);
            Assert.True(bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span), file.RelativePath);
        }
    }

    [Theory]
    [InlineData("PPSA01911", "ff7remake000", 44269, 117739)]
    [InlineData("PPSA01911", "ff7remake001", 831, 6786)]
    [InlineData("PPSA01911", "ff7remake010", 54419, 120384)]
    [InlineData("PPSA01911", "ff7remakeplus000", 17469, 13160)]
    [InlineData("PPSA01911", "ff7remakeplus001", 4984, 471)]
    [InlineData("PPSA08664", "ff7rebirth000", 77530, 155133)]
    [InlineData("PPSA08664", "ff7rebirth001", 2000, 1035)]
    [InlineData("PPSA08664", "ff7rebirth010", 77530, 154609)]
    [InlineData("PPSA18936", "ff7rebirth000", 2000, 4064)]
    [InlineData("PPSA18936", "ff7rebirth001", 2000, 4080)]
    public void KnownSlots_HaveResearchedGilAndPlayTime(string title, string name, int gil, int seconds)
    {
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == name && !f.Directory.IsBackup);
        using var document = new FinalFantasy7Plugin().Open(file);
        _ = document.BuildView();
        Assert.Equal(gil, document.GetValue("game/gil").AsInt32());
        Assert.Equal(seconds, document.GetValue("game/playtime").AsInt32());
    }

    [Theory]
    [InlineData("PPSA01911", 10004, 1200, 3)]
    [InlineData("PPSA08664", 10020, 300, 2)]
    [InlineData("PPSA18936", 10020, 10000, 2)]
    public void KnownMateria_UsesVariantSpecificOffsets(string title, int id, int ap, int level)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == schema.Prefix + "000");
        using var document = new FinalFantasy7Plugin().Open(file);
        _ = document.ExpandNode("materia/0");
        Assert.Equal(id.ToString(), document.GetValue("materia/0/id").AsString());
        Assert.Equal(ap, document.GetValue("materia/0/ap").AsInt32());
        Assert.Equal(level, document.GetValue("materia/0/level").AsInt32());
    }

    [Fact]
    public void RemakeBackupTwins_AgreeOnFramingAndCompleteBytes()
    {
        var schema = Ff7Schema.ForTitle("PPSA01911")!;
        var files = SampleSaves.Load("PPSA01911").AllDiskFiles
            .Where(f => !f.IsSystemFile && schema.TryGetRole(f.FileName, out _)).ToArray();
        var pairs = 0;
        foreach (var primary in files.Where(f => !f.Directory.IsBackup))
        {
            var backup = files.SingleOrDefault(f => f.Directory.IsBackup && f.FileName == primary.FileName &&
                f.Directory.Name == primary.Directory.Name && f.Directory.Generation == primary.Directory.Generation);
            if (backup is null)
            {
                continue;
            }

            var bytes = primary.ReadAllBytes();
            Assert.True(bytes.AsSpan().SequenceEqual(backup.ReadAllBytes()));
            Assert.True(schema.TryGetRole(primary.FileName, out var common));
            Assert.Equal(LinkedRecords.Parse(bytes, schema, common), LinkedRecords.Parse(backup.ReadAllBytes(), schema, common));
            pairs++;
        }

        Assert.True(pairs > 0, "No matching Remake primary/backup twins were tested.");
    }

    [Fact]
    public void DemoSnapshotDiffs_DoNotChangeRecordMetadata()
    {
        const string title = "PPSA18936";
        var schema = Ff7Schema.ForTitle(title)!;
        var files = SampleSaves.Load(title).AllDiskFiles;
        var first = files.First(f => f.FileName == schema.Prefix + "000");
        var second = files.First(f => f.FileName == schema.Prefix + "001");
        var a = first.ReadAllBytes();
        var b = second.ReadAllBytes();
        var records = LinkedRecords.Parse(a, schema, common: false);
        var changed = 0;
        foreach (var record in records)
        {
            var metadataLength = record.IsTail ? 16 : 40;
            Assert.True(a.AsSpan(record.Offset, metadataLength).SequenceEqual(b.AsSpan(record.Offset, metadataLength)),
                $"Record {record.Index} envelope/content header changed between demo snapshots.");
            for (var offset = record.Offset + metadataLength; offset < record.Offset + record.Length; offset++)
            {
                changed += a[offset] == b[offset] ? 0 : 1;
            }
        }

        Assert.True(changed > 0, "The two demo snapshots must contain a gameplay change.");
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void Gameplay_RejectsCorruptPointersTagsLengthsAndTails(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == schema.Prefix + "000");
        var original = file.ReadAllBytes();
        var records = LinkedRecords.Parse(original, schema, common: false);
        (int Offset, uint Value)[] mutations =
        [
            (4, 0), (4, uint.MaxValue), (4, 16), (4, (uint)original.Length), (8, uint.MaxValue), (12, uint.MaxValue),
            (16, 0), (28, 0), (32, 999), (36, uint.MaxValue),
            (records[1].Offset + 4, (uint)records[1].Offset), // self-cycle
            (records[2].Offset + 4, (uint)records[1].Offset), // backward cycle
            (records[2].Offset + 4, (uint)records[2].Offset + 16), // overlap
            (records[^1].Offset, 0), (records[^1].Offset + 4, uint.MaxValue),
        ];
        foreach (var (offset, value) in mutations)
        {
            var corrupt = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(offset), value);
            var input = MemorySaveSet.SingleFile(file.FileName, corrupt, title);
            Assert.Throws<SaveFormatException>(() => new FinalFantasy7Plugin().Open(input));
        }

        var truncated = MemorySaveSet.SingleFile(file.FileName, original[..^16], title);
        Assert.Throws<SaveFormatException>(() => new FinalFantasy7Plugin().Open(truncated));
        var empty = records.First(r => r.IsEmpty);
        var badEmpty = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badEmpty.AsSpan(empty.Offset + 36), 1);
        Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(badEmpty, schema, common: false));
    }
}
