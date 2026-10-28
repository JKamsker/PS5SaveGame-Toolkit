using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class InventoryTests
{
    [Fact]
    public void EveryPrimaryInventoryHasBoundedRecordsRatherThanMarkerCounts()
    {
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles.Where(f => f.FileName == "i29-s.save" && !f.Directory.IsBackup).ToArray();
        Assert.Equal(36, files.Length);
        var observedTypes = new HashSet<uint>();
        foreach (var file in files)
        {
            var container = Dat1Container.Parse(file.ReadAllBytes());
            var section = container.Sections.Single(s => s.Tag == InventoryReader.SectionTag);
            var blocks = InventoryReader.Read(container, section);
            Assert.Equal(27, blocks.Count);
            Assert.Equal(section.Size, (uint)blocks.Sum(b => b.Size));
            Assert.Equal(section.Offset, (uint)blocks[0].Offset);
            Assert.Equal(section.End, (ulong)(blocks[^1].Offset + blocks[^1].Size));
            Assert.True(blocks.Sum(b => b.Records.Count) > 27);
            foreach (var block in blocks)
            {
                Assert.Equal(block.Records.Count, block.Records.Select(r => r.Key).Distinct().Count());
                foreach (var record in block.Records)
                {
                    observedTypes.Add(record.Type);
                    Assert.NotNull(record.GuidBytes);
                    Assert.NotNull(record.ConfigBytes);
                    Assert.NotNull(record.Count);
                    Assert.InRange(record.Offset + record.Size, block.Offset, block.Offset + block.Size);
                    if (record.Xp is null) continue;
                    Assert.InRange(record.CheckpointAmmo!.Value, 0, 10000);
                    Assert.InRange(record.LoadedAmmo!.Value, 0, 10000);
                }
            }
        }
        Assert.Equal(5, observedTypes.Count);
    }

    [Fact]
    public void MarkerInsideUnknownRecordPayloadDoesNotCreateAnotherBlock()
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, InventoryReader.Magic);
        var bytes = Fixture(payload);
        var blocks = Parse(bytes);
        var record = Assert.Single(Assert.Single(blocks).Records);
        Assert.Null(record.GuidBytes);
        Assert.Equal(payload.Length, record.Size);
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("block-size")]
    [InlineData("record-size")]
    [InlineData("count")]
    [InlineData("trailing-header")]
    public void InvalidFramingIsRejectedBeforeAnyPayloadInterpretation(string corruption)
    {
        var bytes = Fixture(new byte[16]);
        var start = 32;
        switch (corruption)
        {
            case "magic": bytes[start + 8] ^= 1; break;
            case "block-size": Write(start + 4, uint.MaxValue); break;
            case "record-size": Write(start + 20, uint.MaxValue); break;
            case "count": Write(start + 12, 4097); break;
            case "trailing-header": Write(start + 4, 8); Write(start + 12, 0); break;
        }
        Assert.Throws<SaveFormatException>(() => Parse(bytes));
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    [Theory]
    [InlineData(0x37475015u)]
    [InlineData(0x4BF3942Cu)]
    public void ExternalSuffixIsValidatedSeparatelyFromDeclaredPayload(uint tag)
    {
        var bytes = Fixture(new byte[16], tag, externalSuffix: true);
        Assert.Equal(4, Assert.Single(Parse(bytes)).OpaqueBytes);
        bytes[^1] = 1;
        Assert.Throws<SaveFormatException>(() => Parse(bytes));
    }

    [Fact]
    public void UnsupportedWeaponStateRemainsOpaqueWithoutDiscardingItsRecord()
    {
        var payload = new byte[119];
        payload[0] = 0xFF;
        payload[1] = 0x3F;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(28), 0x7FC00000);
        var bytes = Fixture(payload, recordType: 0xA89E9C3C);
        var record = Assert.Single(Assert.Single(Parse(bytes)).Records);
        Assert.NotNull(record.GuidBytes);
        Assert.Null(record.CheckpointAmmo);
        Assert.Null(record.Xp);
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    internal static IReadOnlyList<InventoryBlock> Parse(byte[] bytes)
    {
        var container = Dat1Container.Parse(bytes);
        return InventoryReader.Read(container, Assert.Single(container.Sections));
    }

    private static byte[] Fixture(byte[] payload, uint tag = 0x12345678, bool externalSuffix = false, uint recordType = 0x87654321)
    {
        const int sectionStart = 32;
        var bytes = new byte[sectionStart + 24 + payload.Length + (externalSuffix ? 4 : 0)];
        Write(0, 0x44415431); Write(4, 8); Write(8, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
        Write(16, InventoryReader.SectionTag); Write(20, sectionStart); Write(24, (uint)(bytes.Length - sectionStart));
        Write(sectionStart, tag); Write(sectionStart + 4, (uint)(16 + payload.Length));
        Write(sectionStart + 8, InventoryReader.Magic); Write(sectionStart + 12, 1);
        Write(sectionStart + 16, recordType); Write(sectionStart + 20, (uint)payload.Length);
        payload.CopyTo(bytes, sectionStart + 24);
        return bytes;
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
