using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportStorageMutationEffectivenessTests
{
    [Fact]
    public void TruncatedArchivesUseRemainingBytesBeforeAnyPotentiallyNegativeSeek()
    {
        using var stream = new RejectNegativeSeekStream();
        Assert.Throws<InvalidDataException>(() => SaveSetImporter.Import(
            [new ImportSource("dump.zip", () => stream)], TestZips.TempDir()));
        Assert.Equal(0, stream.Seeks);
    }

    [Fact]
    public void SystemFilesAreAddressableByIdButNeverAsEditableGameFiles()
    {
        var set = Import(("PPSA00001/sdimg_SAVE/data.bin", [1]),
            ("PPSA00001/sdimg_SAVE/sce_sys/icon0.png", [2]));
        var directory = Assert.Single(set.Directories);
        var system = Assert.Single(directory.SystemFiles);
        Assert.Equal(2, set.AllDiskFiles.Count());
        Assert.Same(system, set.FindById(system.Id));
        Assert.Null(set.FindFile(system.RelativePath));
        Assert.Equal(new byte[] { 2 }, system.ReadAllBytes());
    }

    [Fact]
    public void DirectoryLookupAndTwinSelectionPreserveDirectoryIdentity()
    {
        var set = Import(("PPSA00001/sdimg_SAVE/data.bin", [1]),
            ("PPSA00001/sdimg_sce_bu_SAVE/data.bin", [2]),
            ("PPSA00001/sdimg_OTHER/data.bin", [3]), ("loose.bin", [4]));
        var primary = set.Directories.Single(d => d.RawName == "sdimg_SAVE");
        var backup = set.Directories.Single(d => d.RawName == "sdimg_sce_bu_SAVE");
        var loose = set.Directories.Single(d => d.RawName == "loose");

        foreach (var directory in set.Directories) Assert.Same(directory, set.FindDirectory(directory.Id));
        Assert.Null(set.FindDirectory("missing"));
        Assert.Same(backup, set.Twin(primary));
        Assert.Same(primary, set.Twin(backup));
        Assert.Null(set.Twin(loose));
        Assert.False(loose.IsBackup);
    }

    [Fact]
    public void PrimaryTwinRequiresABackupWhenALooseDirectoryHasTheSameName()
    {
        var set = Import(("PPSA00001/sdimg_SAVE/data.bin", [1]),
            ("PPSA00001/sdimg_sce_bu_SAVE/data.bin", [1]), ("PPSA00001/SAVE/data.bin", [2]));
        var primary = set.Directories.Single(directory => directory.RawName == "sdimg_SAVE");
        var backup = set.Directories.Single(directory => directory.RawName == "sdimg_sce_bu_SAVE");
        Assert.Same(backup, set.Twin(primary));
    }

    [Fact(Skip = "#40 bug: backup twin lookup selects loose SAVE before sdimg_SAVE when both share a name.")]
    public void BackupTwinUsesTheSdimgPrimaryWhenALooseDirectoryHasTheSameName()
    {
        var set = Import(("PPSA00001/sdimg_SAVE/data.bin", [1]),
            ("PPSA00001/sdimg_sce_bu_SAVE/data.bin", [1]), ("PPSA00001/SAVE/data.bin", [2]));
        var primary = set.Directories.Single(directory => directory.RawName == "sdimg_SAVE");
        var backup = set.Directories.Single(directory => directory.RawName == "sdimg_sce_bu_SAVE");
        Assert.Same(primary, set.Twin(backup));
    }

    [Fact]
    public void OverlayStreamsAreReadOnlyAndHeadersAreBoundedByTheReplacement()
    {
        var set = Import(("data.bin", [1, 2, 3]));
        var original = Assert.Single(set.AllDiskFiles);
        var overlay = new OverlaySaveSet(set, new Dictionary<string, ReadOnlyMemory<byte>>
            { [original.RelativePath] = new byte[] { 9 } });
        var file = Assert.Single(Assert.Single(overlay.Directories).Files);

        Assert.Equal(new byte[] { 9 }, file.ReadHeader(100));
        Assert.Empty(file.ReadHeader(0));
        using var stream = file.OpenRead();
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.WriteByte(8));
        Assert.Equal(new byte[] { 9 }, file.ReadAllBytes());
        Assert.Equal(new byte[] { 1, 2, 3 }, original.ReadAllBytes());
    }

    [Fact]
    public void MaximumLengthPathSegmentsRemainImportableAndBareSaveFoldersAreRejected()
    {
        const int maximumSegmentLength = 255;
        var name = new string('x', maximumSegmentLength);
        Assert.Equal(name, Assert.Single(Import((name, [1])).AllDiskFiles).FileName);
        Assert.Null(SaveSetImporter.ParsePath("sdimg_SAVE"));
    }

    [Fact]
    public void MetadataTitleIdIsNormalizedWhenTheImportedPathHasNoTitle()
    {
        var set = Import(("save.dat", [1]), ("sce_sys/param.sfo", TitleMetadata("ppsa12345")));
        Assert.Equal("PPSA12345", Assert.Single(set.Directories).TitleId);
    }

    [Fact]
    public void AggregateDeclaredLengthsAreRejectedBeforeReadingTheNextEntry()
    {
        var root = StorageRoot();
        var archive = TestZips.Build(("first.bin", [1, 2]), ("next.bin", [3]));
        int central = archive.AsSpan().IndexOf("PK\x01\x02"u8);
        const int centralHeaderSize = 46;
        const int nameLengthOffset = 28;
        const int uncompressedLengthOffset = 24;
        int next = central + centralHeaderSize + BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(central + nameLengthOffset));
        // Without the aggregate precheck this entry reaches copying, then fails as truncated instead.
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(next + uncompressedLengthOffset), 2);
        Assert.Throws<ImportException>(() => SaveSetImporter.Import(
            [new ImportSource("dump.zip", () => new MemoryStream(archive))], root,
            new ImportLimits { MaxTotalBytes = 3 }));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void StreamsWithoutDeclaredLengthsStillRespectTheAggregateBudget()
    {
        var root = StorageRoot();
        Assert.Throws<ImportException>(() => SaveSetImporter.Import(
            [new ImportSource("data.bin", () => new MemoryStream([1, 2]))], root,
            new ImportLimits { MaxTotalBytes = 1 }));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollbackReportsBlobsItCannotDelete(bool readOnly)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows delete-sharing and read-only file semantics.");
        var root = StorageRoot();
        string? blob = null;
        FileStream? locked = null;
        IEnumerable<ImportSource> Sources()
        {
            yield return new("data.bin", () => new MemoryStream([1]));
            blob = Assert.Single(Directory.GetFiles(root));
            if (readOnly) File.SetAttributes(blob, FileAttributes.ReadOnly);
            else locked = new FileStream(blob, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new InvalidDataException("Source enumeration stopped after one file.");
        }
        try
        {
            var error = Assert.Throws<IOException>(() => SaveSetImporter.Import(Sources(), root));
            var failures = Assert.IsType<AggregateException>(error.InnerException);
            var cause = Assert.Single(failures.InnerExceptions);
            if (readOnly) Assert.IsType<UnauthorizedAccessException>(cause);
            else Assert.IsType<IOException>(cause);
            Assert.True(File.Exists(blob));
        }
        finally
        {
            locked?.Dispose();
            if (blob is not null && File.Exists(blob))
            {
                File.SetAttributes(blob, FileAttributes.Normal);
                File.Delete(blob);
            }
        }
        Assert.Empty(Directory.GetFiles(root));
    }

    private static DiskSaveSet Import(params (string Path, byte[] Content)[] files)
    {
        var zip = TestZips.Build(files);
        return SaveSetImporter.Import([new ImportSource("dump.zip", () => new MemoryStream(zip))], TestZips.TempDir());
    }

    private static string StorageRoot()
    {
        var root = TestZips.TempDir();
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RejectNegativeSeekStream : MemoryStream
    {
        public RejectNegativeSeekStream() : base(new byte[12]) => Position = 10;
        public int Seeks { get; private set; }
        public override long Seek(long offset, SeekOrigin origin)
        {
            Seeks++;
            var start = origin switch { SeekOrigin.Current => Position, SeekOrigin.End => Length, _ => 0 };
            if (start + offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            return base.Seek(offset, origin);
        }
    }

    private static byte[] TitleMetadata(string title)
    {
        const int headerSize = 20;
        const int entrySize = 16;
        const int keyTable = headerSize + entrySize;
        byte[] key = "TITLE_ID\0"u8.ToArray();
        byte[] value = Encoding.UTF8.GetBytes(title + '\0');
        var dataTable = keyTable + key.Length;
        var bytes = new byte[dataTable + value.Length];
        "\0PSF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), keyTable);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), dataTable);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(headerSize + 2), 0x0204);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(headerSize + 4), value.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(headerSize + 8), value.Length);
        key.CopyTo(bytes, keyTable);
        value.CopyTo(bytes, dataTable);
        return bytes;
    }

}
