using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SampleCorruptionTests
{
    [Fact]
    public void SamplePackageRejectsCorruptListCountSizesAndPhysicalTruncation()
    {
        var original = SaveBytes();
        var listOffset = (int)BinaryData.U64(original, LspkLayout.FileListOffset);
        foreach (var field in new[] { listOffset, listOffset + sizeof(uint), LspkLayout.FileListSize })
        {
            var bytes = original.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field), uint.MaxValue);
            Assert.Throws<SaveFormatException>(() => Open("sample.lsv", bytes));
        }
        foreach (var length in new[] { LspkLayout.Header16Size - 1, listOffset - 1, original.Length - 1 })
            Assert.Throws<SaveFormatException>(() => Open("sample.lsv", original[..length]));
        using var document = Open("sample.lsv", original);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("meta.lsf")]
    [InlineData("Globals.lsf")]
    public void SampleResourcesRejectCorruptSectionMetadataAndTruncation(string name)
    {
        var package = LspkPackage.Read(SaveBytes(), out var payloads);
        Assert.Contains(package.Entries, entry => entry.Name == name);
        var original = payloads[name];
        foreach (var field in new[]
        {
            LsfLayout.MetadataOffset + LsfLayout.NodesPair * LsfLayout.SizePairLength,
            LsfLayout.MetadataOffset + LsfLayout.AttributesPair * LsfLayout.SizePairLength,
            LsfLayout.MetadataOffset + LsfLayout.ValuesPair * LsfLayout.SizePairLength,
            LsfLayout.MetadataOffset + LsfLayout.ValuesPair * LsfLayout.SizePairLength + sizeof(uint),
        })
        {
            var bytes = original.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field), uint.MaxValue);
            Assert.Throws<SaveFormatException>(() => Open(name, bytes));
        }
        Assert.Throws<SaveFormatException>(() => Open(name, original[..^1]));
        using var document = Open(name, original);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SampleNewAgeRejectsCorruptDirectoryCountAndDescriptorBeforeAccess()
    {
        _ = LspkPackage.Read(SaveBytes(), out var payloads);
        var resource = LsfReader.Read(payloads["Globals.lsf"]);
        var original = Assert.Single(resource.Attributes, attribute => attribute.Name == "NewAge" &&
            attribute.Type == LarianAttributeType.ScratchBuffer).Data.ToArray();
        var count = original.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(count.AsSpan(36), LsmfReader.MaxComponents + 1);
        Assert.Contains("budget", Assert.Throws<SaveFormatException>(() => LsmfReader.Read(count)).Message, StringComparison.Ordinal);
        var descriptor = original.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(descriptor.AsSpan(LsmfFixture.Descriptor(descriptor) + sizeof(ulong)), ulong.MaxValue);
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(descriptor));
        var directoryEnd = LsmfReader.ArenaBase + (int)BinaryData.U64(original, 16) + (int)BinaryData.U64(original, 24);
        Assert.Throws<SaveFormatException>(() => LsmfReader.Read(original.AsMemory(0, directoryEnd - 1)));
        Assert.Equal(original, LsmfReader.Read(original).Bytes.ToArray());
    }

    private static byte[] SaveBytes() => SampleSaves.Load("PPSA18463").AllDiskFiles.First(file =>
        file.FileName == "AutoSave_0.lsv" && !file.Directory.IsBackup).ReadAllBytes();

    private static ISaveDocument Open(string name, byte[] bytes) =>
        new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile(name, bytes, "PPSA18463"));
}
