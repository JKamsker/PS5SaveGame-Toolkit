using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ProbeTests
{
    [Theory]
    [InlineData("SAVESERVICE-LINE-0-0", "data000.bin", false, "Gameplay save, slot 0")]
    [InlineData("SAVESERVICE-LINE-0-0Slot", "data000.bin", false, "Gameplay save, slot 0")]
    [InlineData("SAVESERVICE-LINE-0-3Slot", "data003Slot.bin", false, "Gameplay save, slot 3")]
    [InlineData("SAVESERVICE-LINE-0-10Slot", "data010Slot.bin", true, "Gameplay save, slot 10")]
    [InlineData("saveservice-line-0-3slot", "DATA003SLOT.BIN", true, "Gameplay save, slot 3")]
    [InlineData("SAVESERVICE-LINE-0-003Slot", "data003Slot.bin", false, "Gameplay save, slot 3")]
    [InlineData("SAVESERVICE-LINE-0-3Slot", "data000.bin", false, "Gameplay save, slot 3")]
    [InlineData("SAVESERVICE-LINE-0--1", "data00-1.bin", false, "System data")]
    [InlineData("SAVESERVICE-LINE-0-3Slot", "DATA00-1.BIN", true, "System data")]
    [InlineData("Renamed", "data003Slot.bin", false, "Gameplay save")]
    [InlineData("SAVESERVICE-LINE-0--1", "data000.bin", false, "Gameplay save")]
    [InlineData("SAVESERVICE-LINE-0-3", "data000.bin", false, "Gameplay save")]
    [InlineData("SAVESERVICE-LINE-0-Slot", "data000.bin", false, "Gameplay save")]
    [InlineData("SAVESERVICE-LINE-0-+3Slot", "data000.bin", false, "Gameplay save")]
    [InlineData("SAVESERVICE-LINE-0- 3Slot", "data000.bin", false, "Gameplay save")]
    [InlineData("SAVESERVICE-LINE-0-3Slot-extra", "data000.bin", false, "Gameplay save")]
    [InlineData("SAVESERVICE-LINE-0-999999999999Slot", "data000.bin", false, "Gameplay save")]
    public void LabelsUseOnlyDirectoryNameAndHeader(string directory, string fileName, bool backup, string expected)
    {
        var set = new MemorySaveSet().With("PPSA02530", directory,
            new Dictionary<string, byte[]> { [fileName] = Header() }, backup);
        var file = new HeaderOnlyFile(set.Directories.Single().Files.Single());

        var result = new PragmataPlugin().Probe(file);

        Assert.Equal(ProbeScore.Title, result.Score);
        Assert.Equal(expected, result.Label);
    }

    [Fact]
    public void SampleLabelsAgreeWithParsedSlotsIncludingBackupsAndSnapshots()
    {
        var plugin = new PragmataPlugin();
        var files = SampleSaves.Load("PPSA02530").AllDiskFiles.Where(f => plugin.Probe(f).IsMatch).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
            var expected = document.Layout.Header is null ? "System data" :
                $"Gameplay save, slot {document.Read(document.Layout.Detail.Find(KnownHashes.Slot)!)}";
            Assert.Equal(expected, plugin.Probe(file).Label);
        }
    }

    private static byte[] Header()
    {
        var header = new byte[DsssContainer.HeaderSize];
        "DSSS"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 0x10);
        return header;
    }

    private sealed class HeaderOnlyFile(ISaveFile inner) : ISaveFile
    {
        public string Id => inner.Id;
        public ISaveDirectory Directory => inner.Directory;
        public string RelativePath => inner.RelativePath;
        public string PathInDirectory => inner.PathInDirectory;
        public string FileName => inner.FileName;
        public long Length => inner.Length;
        public Stream OpenRead() => throw new InvalidOperationException("Probe must only read the header.");
        public byte[] ReadAllBytes() => throw new InvalidOperationException("Probe must only read the header.");
        public byte[] ReadHeader(int count)
        {
            Assert.Equal(DsssContainer.HeaderSize, count);
            return inner.ReadHeader(count);
        }
    }
}
