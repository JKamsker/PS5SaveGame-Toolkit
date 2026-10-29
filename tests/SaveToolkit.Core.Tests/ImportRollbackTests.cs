using System.Buffers.Binary;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportRollbackTests
{
    [Theory]
    [InlineData("size")]
    [InlineData("count")]
    [InlineData("crc")]
    [InlineData("truncated")]
    [InlineData("not-zip")]
    [InlineData("name")]
    [InlineData("read")]
    [InlineData("open")]
    [InlineData("enumerate")]
    public void Failed_import_removes_its_blobs_and_can_be_retried(string failure)
    {
        string root = TestZips.TempDir();
        byte[] archive = TestZips.Build(("first.bin", [1]), ("second.bin", [2, 3]));
        var limits = new ImportLimits();
        ImportSource second = new("dump.zip", () => new MemoryStream(archive));
        switch (failure)
        {
            case "size": limits = new ImportLimits { MaxTotalBytes = 2 }; break;
            case "count": limits = new ImportLimits { MaxEntries = 1 }; break;
            case "crc":
                int central = archive.AsSpan().IndexOf("PK\x01\x02"u8);
                int next = central + 46 + BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(central + 28));
                archive[next + 16] ^= 1;
                break;
            case "truncated": second = new("partial.zip", () => new MemoryStream(archive[..^12])); break;
            case "not-zip": second = new("fake.zip", () => new MemoryStream("not a zip"u8.ToArray())); break;
            case "name": second = new(new string('x', 256), () => new MemoryStream([2])); break;
            case "read": second = new("broken.bin", () => new FailingReadStream()); break;
            case "open": second = new("broken.bin", () => throw new IOException("Download stopped.")); break;
        }
        IEnumerable<ImportSource> Sources()
        {
            yield return new("prefix.bin", () => new MemoryStream([9]));
            if (failure == "enumerate") throw new IOException("Source enumeration failed.");
            yield return second;
        }

        Assert.ThrowsAny<Exception>(() => SaveSetImporter.Import(Sources(), root, limits));
        Assert.True(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any(),
            "Failed imports must not leave complete or partial blobs behind.");
        var retry = SaveSetImporter.Import([new ImportSource("retry.bin", () => new MemoryStream([7]))], root);
        Assert.Equal([7], Assert.Single(Assert.Single(retry.Directories).Files).ReadAllBytes());
    }

    [Fact]
    public void Rollback_preserves_preexisting_files_and_a_blob_name_collision()
    {
        string root = TestZips.TempDir();
        Directory.CreateDirectory(root);
        string sentinel = Path.Combine(root, "unrelated.txt");
        string collision = Path.Combine(root, "000001.blob");
        File.WriteAllBytes(sentinel, [8]);
        File.WriteAllBytes(collision, [9]);
        byte[] archive = TestZips.Build(("first.bin", [1]), ("second.bin", [2]));

        Assert.Throws<IOException>(() => SaveSetImporter.Import(
            [new ImportSource("dump.zip", () => new MemoryStream(archive))], root));

        Assert.Equal([8], File.ReadAllBytes(sentinel));
        Assert.Equal([9], File.ReadAllBytes(collision));
        Assert.Equal(2, Directory.GetFiles(root).Length);
        Assert.False(File.Exists(Path.Combine(root, "000000.blob")));
    }

    private sealed class FailingReadStream : MemoryStream
    {
        private bool _read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read) throw new IOException("Download stopped.");
            _read = true;
            buffer[offset] = 1;
            return 1;
        }
    }
}
