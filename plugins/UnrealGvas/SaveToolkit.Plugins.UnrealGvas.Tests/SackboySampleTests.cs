using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class SackboySampleTests
{
    [Fact]
    public void EverySnapshotAndImageCompanionRoundTripsWithoutLosingBytes()
    {
        var files = SampleSaves.Load("PPSA01288").AllDiskFiles.Where(f => !f.IsSystemFile).ToArray();
        Assert.Equal(20, files.Length);
        Assert.Equal(6, files.DistinctBy(f => f.Sha256).Count());
        var plugin = new UnrealGvasPlugin();
        foreach (var file in files)
        {
            Assert.Equal(ProbeScore.Engine, plugin.Probe(file).Score);
            Assert.Contains("Sackboy", plugin.Probe(file).Label!);
            using var document = plugin.Open(file);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void GeneralHasVerifiedTaggedCurrencyAndUnknownChecksumBlocksEditing()
    {
        var files = SampleSaves.Load("PPSA01288").AllDiskFiles.Where(f => f.FileName == "General").DistinctBy(f => f.Sha256).ToArray();
        Assert.Equal(2, files.Length);
        var currencies = new List<Int128>();
        foreach (var file in files)
        {
            var parsed = GvasFile.Parse(file.ReadAllBytes());
            Assert.Equal("/Script/GingerBread.GingerbreadSaveUser", parsed.Header.SaveClass);
            Assert.Equal((ushort)1, parsed.Header.EnginePatch);
            var nodes = GvasFile.Descendants(parsed.Properties).ToArray();
            currencies.Add((Int128)Assert.Single(nodes, n => n.Name == "CollectabellsStored").Scalar!.Value);
            Assert.Contains(nodes, n => n.Name == "Checksum");
            using var document = new UnrealGvasPlugin().Open(file);
            Assert.False(document.CanEdit);
            var fields = GvasFormatTests.Expand(document);
            Assert.All(fields, f => Assert.True(f.Spec.ReadOnly));
        }
        Assert.Equal(new Int128[] { 322, 445 }, currencies.Order().ToArray());
    }

    [Fact]
    public void ImagesAreGvasWithUnrealChunkCompressionRatherThanPngOrJpeg()
    {
        var files = SampleSaves.Load("PPSA01288").AllDiskFiles
            .Where(f => f.FileName.StartsWith("LevelImages_", StringComparison.Ordinal) || f.FileName.StartsWith("CostumeThumbnails_", StringComparison.Ordinal))
            .DistinctBy(f => f.Sha256).ToArray();
        Assert.Equal(4, files.Length);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var parsed = GvasFile.Parse(bytes);
            Assert.Equal("/Script/GingerBread.GingerbreadSaveImageSet", parsed.Header.SaveClass);
            Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10 }) < 0);
            Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xFF, 0xD8, 0xFF }) < 0);
        }
        var level = files.Single(f => f.FileName == "LevelImages_04").ReadAllBytes();
        const int zlibOffset = 0x6E4;
        Assert.Equal(131072UL, BinaryPrimitives.ReadUInt64LittleEndian(level.AsSpan(zlibOffset - 8)));
        using var input = new MemoryStream(level, zlibOffset, 12144, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        Assert.Equal(131072, output.Length);
        Assert.True(level.AsSpan().IndexOf("SurfaceWidth"u8) > 0);
        Assert.True(level.AsSpan().IndexOf("PixelFormat"u8) > 0);
    }
}
