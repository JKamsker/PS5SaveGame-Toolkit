using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class TitleMalformedInputTests
{
    [Fact]
    public void EveryTruncatedJediZlibStreamDisablesTheOverlayWithoutLosingBytes()
    {
        byte[] plain = ActorProperties();
        byte[] compressed = Compress(plain);
        for (var length = 0; length < compressed.Length; length++)
            AssertInvalidJedi(Jedi(compressed[..length], (uint)plain.Length));
        using var valid = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save", Jedi(compressed, (uint)plain.Length)));
        Assert.True(valid.CanEdit);
        Assert.Contains(Assert.IsAssignableFrom<IQueryableDocument>(valid).Collections, collection => collection.Id == "jedi/actor-tags");
    }

    [Theory]
    [InlineData("header")]
    [InlineData("trailer")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("huge")]
    [InlineData("empty")]
    [InlineData("no-tags")]
    public void InvalidJediStreamsAndLengthsFailBoundedValidation(string corruption)
    {
        byte[] plain = ActorProperties();
        byte[] compressed = Compress(plain);
        uint declared = (uint)plain.Length;
        switch (corruption)
        {
            case "header": compressed[0] ^= 0xFF; break;
            case "trailer": compressed[^1] ^= 1; break;
            case "short": declared--; break;
            case "long": declared++; break;
            case "huge": declared = uint.MaxValue; break;
            case "empty": compressed = Compress([]); declared = 0; break;
            case "no-tags": compressed = Compress(new byte[32]); declared = 32; break;
        }
        AssertInvalidJedi(Jedi(compressed, declared));
    }

    [Theory]
    [InlineData("PPSA01288", 6)]
    [InlineData("PPSA02199", 3)]
    [InlineData("PPSA01603", 17)]
    public void EveryClaimedDistinctSampleRejectsTruncatedPropertySpansAndBadVersionTables(string title, int expected)
    {
        var plugin = new UnrealGvasPlugin();
        var samples = SampleSaves.Load(title).AllDiskFiles.Where(file => !file.IsSystemFile && plugin.Probe(file).IsMatch)
            .DistinctBy(file => file.Sha256).ToArray();
        Assert.Equal(expected, samples.Length);
        foreach (var sample in samples)
        {
            byte[] original = sample.ReadAllBytes();
            var parsed = GvasFile.Parse(original);
            // Resolve the real header fields rather than relying on a private sample offset.
            int countOffset, rootOffset;
            using (var stream = new MemoryStream(original, writable: false))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 4 + 2 * sizeof(int) + 3 * sizeof(ushort) + sizeof(uint);
                int units = reader.ReadInt32();
                stream.Position += units < 0 ? -(long)units * sizeof(char) : units;
                stream.Position += sizeof(int); // Custom-version format.
                countOffset = checked((int)stream.Position);
                stream.Position += sizeof(int) + parsed.Header.CustomVersionCount * (16 + sizeof(int));
                int classUnits = reader.ReadInt32();
                stream.Position += classUnits < 0 ? -(long)classUnits * sizeof(char) : classUnits;
                rootOffset = checked((int)stream.Position);
            }
            int[] cuts = parsed.Properties.Count == 0
                ? [0, 4, rootOffset, rootOffset + sizeof(int), rootOffset + sizeof(int) + "None"u8.Length]
                : [0, 4, parsed.Properties[0].Offset - 1, parsed.Properties[^1].Offset,
                    parsed.Properties[^1].Offset + parsed.Properties[^1].Length - 1];
            foreach (int cut in cuts.Distinct())
                Assert.Throws<SaveFormatException>(() => plugin.Open(MemorySaveSet.SingleFile("save", original.AsSpan(0, cut).ToArray(), title)));
            byte[] corrupt = original.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(corrupt.AsSpan(countOffset), int.MaxValue);
            Assert.False(plugin.Probe(MemorySaveSet.SingleFile("save", corrupt, title)).IsMatch);
            Assert.Contains("count", Assert.Throws<SaveFormatException>(() =>
                plugin.Open(MemorySaveSet.SingleFile("save", corrupt, title))).Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertInvalidJedi(byte[] bytes)
    {
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save", bytes, "PPSA02199"));
        Assert.False(document.CanEdit);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.DoesNotContain(queryable.Collections, collection => collection.Id == "jedi/actor-tags");
        var actors = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Actors");
        Assert.Contains(actors.Children.OfType<TextNode>(), text => text.Tone == Tone.Warning && !string.IsNullOrWhiteSpace(text.Text));
        Assert.All(GvasFormatTests.Expand(document), field => Assert.True(field.Spec.ReadOnly));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] Jedi(byte[] compressed, uint declared)
    {
        byte[] bytes = GvasFixture.Build(w =>
        {
            GvasFixture.Property(w, "CompressedActorRecords", "ArrayProperty",
                GvasFixture.Body(b => { b.Write(compressed.Length); b.Write(compressed); }), "ByteProperty");
            GvasFixture.Property(w, "UncompressedActorRecordsSize", "UInt32Property", GvasFixture.Body(b => b.Write(declared)));
            GvasFixture.Property(w, "Crc32", "UInt32Property", new byte[sizeof(uint)]);
        }, minor: 25, saveClass: "/Script/RsGameTechRT.RsSaveGameObject", customVersions: 56);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 518); // Observed package version.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 0); // Observed engine patch.
        return bytes;
    }

    private static byte[] ActorProperties() => GvasFixture.Body(w =>
    {
        GvasFixture.Property(w, "ActorRecords", "IntProperty", GvasFixture.Body(b => b.Write(42)));
        GvasFixture.String(w, "None");
    });

    private static byte[] Compress(byte[] plain)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(plain);
        return output.ToArray();
    }
}
