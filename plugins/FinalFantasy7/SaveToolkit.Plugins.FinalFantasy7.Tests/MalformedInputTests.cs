using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class MalformedInputTests
{
    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void EveryCommonPrefixRejectsWithAFormatMessage(string title)
    {
        var bytes = ContainerTests.Common(title);
        var schema = Ff7Schema.ForTitle(title)!;
        var plugin = new FinalFantasy7Plugin();
        for (var length = 0; length < bytes.Length; length++)
        {
            var file = MemorySaveSet.SingleFile(schema.CommonName, bytes[..length], title);
            Assert.False(plugin.Probe(file).IsMatch);
            Assert.Contains("FFVII", Assert.Throws<SaveFormatException>(() => plugin.Open(file)).Message);
            Assert.Contains("FFVII", Assert.Throws<SaveFormatException>(() =>
                LinkedRecords.Parse(bytes.AsSpan(0, length), schema, common: true)).Message);
        }
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void GameplayTruncationAtEveryHeaderAndBodyBoundaryRejects(string title)
    {
        var fixture = SyntheticGameplay.Create(title);
        var schema = Ff7Schema.ForTitle(title)!;
        foreach (var record in fixture.Records)
        {
            // Every partial envelope/content header, the first body byte and the last byte.
            var headerLength = record.Kind == 4 ? SyntheticGameplay.EnvelopeLength : SyntheticGameplay.BodyOffset;
            var cuts = Enumerable.Range(record.Offset, headerLength + 1)
                .Append(record.Offset + headerLength + 1).Append(record.Offset + record.Length - 1)
                .Where(length => length < fixture.Bytes.Length).Distinct();
            foreach (var length in cuts)
            {
                var error = Assert.Throws<SaveFormatException>(() =>
                    LinkedRecords.Parse(fixture.Bytes.AsSpan(0, length), schema, common: false));
                Assert.Contains("FFVII", error.Message);
            }
        }
    }

    [Theory]
    [InlineData("PPSA01911", false)]
    [InlineData("PPSA08664", false)]
    [InlineData("PPSA18936", false)]
    [InlineData("PPSA01911", true)]
    [InlineData("PPSA08664", true)]
    [InlineData("PPSA18936", true)]
    public void UnsupportedFileLengthsRejectBeforeAnyRead(string title, bool common)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var identity = MemorySaveSet.SingleFile(common ? schema.CommonName : schema.Prefix + "000", [], title);
        foreach (var length in new long[] { 0, 39, schema.FileLength(common) - 1, schema.FileLength(common) + 1,
                     int.MaxValue, (long)int.MaxValue + 1, uint.MaxValue, long.MaxValue })
        {
            var file = new ObservedSaveFile(identity, length, ReadOnlyMemory<byte>.Empty) { RejectReads = true };
            var plugin = new FinalFantasy7Plugin();
            Assert.False(plugin.Probe(file).IsMatch);
            Assert.False(Assert.Single(plugin.Decoders).CanDecode(file));
            Assert.Contains("schema size", Assert.Throws<SaveFormatException>(() => plugin.Open(file)).Message);
            Assert.Equal(0, file.HeaderReads);
            Assert.Equal(0, file.BodyReads);
        }
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void ShortReadDespiteOriginalFileLengthRejectsBeforeBodyReaders(string title)
    {
        var bytes = ContainerTests.Common(title);
        var schema = Ff7Schema.ForTitle(title)!;
        var identity = MemorySaveSet.SingleFile(schema.CommonName, [], title);
        var plugin = new FinalFantasy7Plugin();
        foreach (var length in new[] { 0, 1, 15, 16, 31, 32, 35, 36, 39, 40, bytes.Length - 1 })
        {
            var file = new ObservedSaveFile(identity, bytes.Length, bytes.AsMemory(0, length));
            Assert.Equal(length >= LinkedRecords.ContentOffset, plugin.Probe(file).IsMatch);
            Assert.Equal(0, file.BodyReads);
            Assert.Contains("header or size", Assert.Throws<SaveFormatException>(() => plugin.Open(file)).Message);
            Assert.Equal(1, file.BodyReads);
        }
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void ExtremeMetadataInEveryRecordFamilyRejectsAtItsOffset(string title)
    {
        var fixture = SyntheticGameplay.Create(title);
        var schema = Ff7Schema.ForTitle(title)!;
        var representatives = fixture.Records.Where(record => record.Kind != 4).GroupBy(record => record.Kind)
            .Select(group => group.Last());
        foreach (var record in representatives)
        foreach (var relative in new[] { LinkedRecords.NextPointerOffset, LinkedRecords.AllocationLengthOffset,
                     LinkedRecords.DeclaredLengthOffset })
        foreach (var value in new uint[] { 0x7FFFFFFF, 0x80000000, uint.MaxValue })
        {
            var at = record.Offset + relative;
            var original = BinaryPrimitives.ReadUInt32LittleEndian(fixture.Bytes.AsSpan(at));
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.Bytes.AsSpan(at), value);
            var error = Assert.Throws<SaveFormatException>(() => LinkedRecords.Parse(fixture.Bytes, schema, common: false));
            Assert.Contains(record.Offset == 0 && relative == LinkedRecords.NextPointerOffset
                ? "header or size" : $"0x{record.Offset:X}", error.Message);
            BinaryPrimitives.WriteUInt32LittleEndian(fixture.Bytes.AsSpan(at), original);
        }
    }
}

/// <summary>Records reads without allocating the file length reported by a malformed input.</summary>
internal sealed class ObservedSaveFile(ISaveFile identity, long length, ReadOnlyMemory<byte> content) : ISaveFile
{
    public string Id => identity.Id;
    public ISaveDirectory Directory => identity.Directory;
    public string RelativePath => identity.RelativePath;
    public string PathInDirectory => identity.PathInDirectory;
    public string FileName => identity.FileName;
    public long Length => length;
    public bool RejectReads { get; init; }
    public int HeaderReads { get; private set; }
    public int BodyReads { get; private set; }

    public Stream OpenRead() => throw new InvalidOperationException("FFVII should use bounded header/body reads.");

    public byte[] ReadAllBytes()
    {
        BodyReads++;
        if (RejectReads) throw new InvalidOperationException("Unsupported file body must not be read.");
        return content.ToArray();
    }

    public byte[] ReadHeader(int count)
    {
        HeaderReads++;
        if (RejectReads) throw new InvalidOperationException("Unsupported file header must not be read.");
        return content[..Math.Min(count, content.Length)].ToArray();
    }
}
