using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class FormatTests
{
    [Theory]
    [InlineData(15u)]
    [InlineData(16u)]
    [InlineData(18u)]
    public void Packages_ReadDocumentedLayoutsAndEveryCompression(uint version)
    {
        byte[] payload = [.. Enumerable.Range(0, 4096).Select(i => (byte)i)];
        for (byte method = 0; method <= 3; method++)
        {
            var package = LspkPackage.Read(PackageFixture.Create(version, method, ("test.bin", payload)));
            var entry = Assert.Single(package.Entries);
            Assert.Equal(version, package.Version);
            Assert.Equal("test.bin", entry.Name);
            Assert.Equal(payload, package.Extract(entry));
        }
    }

    [Theory]
    [InlineData(5u, false, (byte)0)]
    [InlineData(5u, true, (byte)1)]
    [InlineData(6u, true, (byte)2)]
    [InlineData(7u, false, (byte)2)]
    [InlineData(7u, true, (byte)3)]
    public void Resources_ReadLayoutsKeysAndCompressedSections(uint version, bool adjacency, byte compression)
    {
        var resource = LsfReader.Read(ResourceFixture.Create(version, adjacency, compression));
        Assert.Equal(2, resource.Nodes.Count);
        Assert.Equal(3, resource.Attributes.Count);
        Assert.Equal([0], resource.Roots);
        Assert.Equal([1], resource.Nodes[0].Children);
        Assert.Equal([0, 2], resource.Nodes[0].Attributes);
        Assert.Equal([1], resource.Nodes[1].Attributes);
        Assert.Equal(7, AttributeDecoder.Read(resource.Attributes[0]).AsInt32());
        Assert.Equal("hello", AttributeDecoder.Read(resource.Attributes[1]).AsString());
        Assert.True(AttributeDecoder.Read(resource.Attributes[2]).AsBool());
        Assert.Equal(version >= 6 && adjacency ? "Counter" : null, resource.Nodes[0].KeyAttribute);
    }

    [Fact]
    public void MetadataTwoUsesCompactRecordsAndRawLz4IsSupported()
    {
        var resource = LsfReader.Read(ResourceFixture.Create(compression: 2, framed: false, metadata: 2));
        Assert.Equal("hello", AttributeDecoder.Read(resource.Attributes[1]).AsString());
    }

    [Fact]
    public void MalformedPackageHeadersAreRejectedAsSaveFormatExceptions()
    {
        var original = PackageFixture.Create(payloads: [("test.bin", new byte[32])]);
        foreach (var (offset, value) in new (int, uint)[] { (4, 99), (8, uint.MaxValue), (16, uint.MaxValue), (38, 2) })
        {
            var bytes = original.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
            Reject("bad.lsv", bytes);
        }

        var solid = original.ToArray();
        solid[20] = 4;
        Assert.Contains("Solid", Reject("solid.lsv", solid).Message, StringComparison.Ordinal);
        Reject("short.lsv", original[..39]);
        Reject("truncated.lsv", original[..^1]);
        Reject("duplicate.lsv", PackageFixture.Create(payloads: [("same", [1]), ("same", [2])]));
    }

    [Fact]
    public void CorruptedPayloadsAndResourcesAreRejected()
    {
        var compressed = PackageFixture.Create(method: 3, payloads: [("test.bin", new byte[64])]);
        compressed[40] ^= 0xff; // destroy the zstd frame magic
        Reject("bad.lsv", compressed);
        Reject("bad.lsv", PackageFixture.Create(payloads: [("invalid.lsf", "garbage"u8.ToArray())]));
        var original = ResourceFixture.Create();
        foreach (var offset in new[] { 4, 16, 32, 40, 48, 60 })
        {
            var bytes = original.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), uint.MaxValue);
            Reject("bad.lsf", bytes);
        }

        Reject("short.lsf", original[..63]);
        Reject("truncated.lsf", original[..^1]);
        var unterminated = ResourceFixture.Create();
        unterminated[^2] = 1; // hello string terminator
        Reject("bad.lsf", unterminated);
    }

    [Theory]
    [InlineData("<other />")]
    [InlineData("<save><region></save>")]
    [InlineData("<!DOCTYPE save [<!ENTITY x SYSTEM 'file:///never-read'>]><save>&x;</save>")]
    public void InvalidXmlIsRejectedWithoutResolvingEntities(string xml) => Reject("bad.lsx", System.Text.Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void LsxRetainsElementAndAttributeMetadata()
    {
        var resource = LsxReader.Read("<save><version major='4'/><region id='Test'><node id='Root'><attribute id='Count' type='int32' value='7'/></node></region></save>"u8.ToArray());
        Assert.Equal(5, resource.Nodes.Count);
        Assert.Contains(resource.Nodes, node => node.Name == "node · Root");
        Assert.Contains(resource.Attributes, attribute => attribute.Name == "value" && attribute.XmlValue == "7");
    }

    private static SaveFormatException Reject(string name, byte[] bytes) => Assert.Throws<SaveFormatException>(() =>
        new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile(name, bytes, "PPSA18463")));
}
