using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class TruncationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryIncompleteNestedSaveRejectsWithAndWithoutRepairedOuterSizes(bool repairSizes)
    {
        var fixture = DdlCompoundFixture.NestedArrays();
        for (var length = 0; length < fixture.Bytes.Length; length++)
        {
            var bytes = fixture.Bytes[..length];
            if (repairSizes)
            {
                if (length >= 12) Write(bytes, 8, (uint)length);
                if (length >= 28) Write(bytes, 24, (uint)Math.Max(0, length - fixture.SectionOffset));
                if (length >= fixture.SectionOffset + 16)
                    Write(bytes, fixture.SectionOffset + 12, (uint)(length - fixture.SectionOffset - 16));
            }
            var file = MemorySaveSet.SingleFile("i29-userprefs.save", bytes, "PPSA01474");
            var error = Assert.Throws<SaveFormatException>(() => new InsomniacPlugin().Open(file));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            var sink = new RobustnessTestSink();
            Assert.Throws<SaveFormatException>(() => new Insomniac.RiftEvidenceDecoder().Decode(file, sink));
            Assert.Equal(0, sink.AddCalls);
        }
    }

    [Theory]
    [InlineData(0x1CEA6CEBu, 4)]
    [InlineData(DdlSections.ObjectivesTag, 8)]
    public void PrefixedSectionsRejectEveryTruncatedPrefixAndObjectHeader(uint tag, int prefix)
    {
        var fixture = new DdlTestFixture();
        for (var length = 0; length < prefix + 16; length++)
        {
            var bytes = new byte[fixture.SectionOffset + length];
            fixture.Bytes.AsSpan(0, fixture.SectionOffset).CopyTo(bytes);
            Write(bytes, 8, (uint)bytes.Length); Write(bytes, 16, tag); Write(bytes, 24, (uint)length);
            if (length > prefix)
                fixture.Bytes.AsSpan(fixture.SectionOffset, length - prefix).CopyTo(bytes.AsSpan(fixture.SectionOffset + prefix));
            var error = Assert.Throws<SaveFormatException>(() =>
                new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes)));
            Assert.Contains($"DDL section 0x{tag:X8}", error.Message);
        }
    }

    [Fact]
    public void RealSampleSectionTruncationsAndTypeTableCorruptionRejectWithoutChangingSource()
    {
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles.Where(file => !file.Directory.IsBackup &&
            file.FileName is "i29-s.save" or "i29-userprefs.save").ToArray();
        Assert.Equal(38, files.Length);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            var container = Dat1Container.Parse(original);
            var tag = file.FileName == "i29-s.save" ? RiftPlayerView.CampaignTag : DdlSections.PreferencesTag;
            var section = Assert.Single(container.Sections, section => section.Tag == tag);
            var start = checked((int)section.Offset);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(start + 8));
            foreach (var shortSize in new[] { 0, 15, checked(16 + (int)count * 12 - 1) })
            {
                var bytes = original.ToArray();
                Write(bytes, 16 + section.Index * 12 + 8, (uint)shortSize);
                Assert.Contains("DDL section", Assert.Throws<SaveFormatException>(() =>
                    new InsomniacPlugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA01474"))).Message);
            }
            var invalidType = original.ToArray();
            invalidType[start + 23] = 14; // Unassigned wire type, retaining count and array metadata.
            Assert.Contains("Unsupported field count or array kind", Assert.Throws<SaveFormatException>(() =>
                new InsomniacPlugin().Open(MemorySaveSet.SingleFile(file.FileName, invalidType, "PPSA01474"))).Message);
            Assert.Equal(original, file.ReadAllBytes());
        }
    }

    private static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
