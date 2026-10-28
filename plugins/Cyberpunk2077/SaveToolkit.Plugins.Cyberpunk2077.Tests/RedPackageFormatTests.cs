using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class RedPackageFormatTests
{
    [Fact]
    public void AppendingNamesPreservesIndicesOpaqueChunksAndPadding()
    {
        var bytes = Fixture();
        var original = new RedPackage(bytes, true);
        var appended = original.WithNames(["currentExp", "expToLevel", "currentExp", "Int32"]);
        Assert.Equal(original.Names.Concat(["currentExp", "expToLevel"]), appended.Names);
        Assert.Equal(original.Types, appended.Types);
        foreach (var index in Enumerable.Range(0, original.Types.Count))
            Assert.Equal(original.Chunk(index).Rebuild(new Dictionary<string, byte[]>()),
                appended.Chunk(index).Rebuild(new Dictionary<string, byte[]>()));
        for (var index = 0; index < original.Names.Count; index++)
            Assert.Equal(original.NameIndex(original.Names[index]), appended.NameIndex(original.Names[index]));
        Assert.Same(appended, appended.WithNames(["currentExp"]));
        Assert.Throws<SaveFormatException>(() => original.WithNames(["bad\nname"]));
        Assert.Throws<SaveFormatException>(() => original.WithNames([new string('x', 255)]));
        var chunk = appended.Chunk(0);
        var reopened = new RedPackage(appended.Rebuild(0, chunk.Rebuild(new Dictionary<string, byte[]> { ["currentExp"] = [7, 0, 0, 0] })), true);
        Assert.Equal(7, reopened.Chunk(0).Int("currentExp"));
        Assert.Equal(chunk.Raw(chunk.Require("opaque", "Blob")), reopened.Chunk(0).Raw(reopened.Chunk(0).Require("opaque", "Blob")));
        Assert.Equal(bytes, original.Rebuild(0, original.Chunk(0).Rebuild(new Dictionary<string, byte[]>())));
    }

    [Fact]
    public void AddingOmittedIntRetainsUnknownDataPaddingNamesAndFollowingChunk()
    {
        var bytes = Fixture();
        var package = new RedPackage(bytes, true);
        var chunk = package.Chunk(0);
        Assert.Equal(0, chunk.Int("unspent"));
        Assert.Equal(bytes, package.Rebuild(0, chunk.Rebuild(new Dictionary<string, byte[]>())));
        var modified = chunk.Rebuild(new Dictionary<string, byte[]> { ["unspent"] = [42, 0, 0, 0] });
        var rebuilt = new RedPackage(package.Rebuild(0, modified), true);
        Assert.Equal(package.Names, rebuilt.Names);
        Assert.Equal(package.Types, rebuilt.Types);
        Assert.Equal(42, rebuilt.Chunk(0).Int("unspent"));
        Assert.Equal(chunk.Raw(chunk.Require("opaque", "Blob")), rebuilt.Chunk(0).Raw(rebuilt.Chunk(0).Require("opaque", "Blob")));
        Assert.Equal(package.Chunk(1).Rebuild(new Dictionary<string, byte[]>()), rebuilt.Chunk(1).Rebuild(new Dictionary<string, byte[]>()));
        Assert.Equal(new byte[] { 0xA1, 0xB2 }, modified.AsSpan(18, 2).ToArray());
    }

    [Fact]
    public void CorruptClassHeadersAndTruncationsThrowFormatErrors()
    {
        var bytes = Fixture();
        for (var length = 0; length < 28; length++)
        {
            var truncated = bytes[..length];
            Assert.Throws<SaveFormatException>(() => new RedPackage(truncated, true));
        }

        byte[] invalid = [1, 0, 2, 0, 3, 0, 255, 255, 255, 127];
        var package = new RedPackage(bytes, true);
        Assert.Throws<SaveFormatException>(() => new PackageClass(package, invalid, 0, invalid.Length));
        byte[] badName = [1, 0, 255, 255, 3, 0, 10, 0, 0, 0, 0];
        Assert.Throws<SaveFormatException>(() => new PackageClass(package, badName, 0, badName.Length));
        byte[] overlapping = [2, 0, 2, 0, 3, 0, 18, 0, 0, 0, 4, 0, 5, 0, 18, 0, 0, 0, 0];
        Assert.Throws<SaveFormatException>(() => new PackageClass(package, overlapping, 0, overlapping.Length));
    }

    private static byte[] Fixture()
    {
        // Independent v4/6-section package: two chunks, one opaque property, padding, no point balance.
        string[] names = ["PlayerDevelopmentData", "OtherChunk", "opaque", "Blob", "unspent", "Int32"];
        var namesSize = names.Sum(n => Encoding.ASCII.GetByteCount(n) + 1);
        var namesData = names.Length * 4;
        var chunkDescriptors = namesData + namesSize;
        const int chunkCount = 2;
        var chunkData = chunkDescriptors + chunkCount * 8;
        byte[] first = [1, 0, 2, 0, 3, 0, 12, 0, 0, 0, 0xA1, 0xB2, 0xCC, 0xDD, 0xEE];
        byte[] second = [0, 0, 0xF1, 0xF2];
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write(new byte[] { 4, 0, 6, 0 });
        writer.Write(chunkCount);
        writer.Write(0);
        writer.Write(namesData);
        writer.Write(chunkDescriptors);
        writer.Write(chunkData);
        writer.Write(0); // Scriptable root IDs count.
        var offset = namesData;
        foreach (var name in names)
        {
            var size = Encoding.ASCII.GetByteCount(name) + 1;
            writer.Write((uint)offset | ((uint)size << 24));
            offset += size;
        }

        foreach (var name in names)
        {
            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write((byte)0);
        }

        writer.Write(0); writer.Write(chunkData);
        writer.Write(1); writer.Write(chunkData + first.Length);
        writer.Write(first); writer.Write(second);
        writer.Flush();
        return output.ToArray();
    }
}
