using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class ParserBoundaryTests
{
    private const string TitleId = "PPSA18463";
    private const int ResourceHeaderSize = 64;

    [Fact]
    public void InvalidNamesParentsAndAttributeChainsAreRejected()
    {
        var original = ResourceFixture.Create();
        var nodes = ResourceHeaderSize + (int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(16));
        var attributes = nodes + (int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(32));
        foreach (var (offset, value) in new (int, uint)[]
        {
            (nodes, uint.MaxValue), // invalid name
            (nodes + 8, 0), // root parent points to itself
            (nodes + 4, 99), // nonexistent first attribute
            (nodes + 12 + 4, 0), // child shares root attribute chain
            (attributes + 4, 5), // uint32 with zero length
        })
        {
            var bytes = original.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.Throws<SaveFormatException>(() => Open("bad.lsf", bytes));
        }

        var extended = ResourceFixture.Create(adjacency: true);
        var attributeOffset = ResourceHeaderSize + (int)BinaryPrimitives.ReadUInt32LittleEndian(extended.AsSpan(16)) +
            (int)BinaryPrimitives.ReadUInt32LittleEndian(extended.AsSpan(32));
        BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(attributeOffset + 8), 0); // attribute refers to itself
        Assert.Throws<SaveFormatException>(() => Open("cyclic.lsf", extended));
    }

    [Fact]
    public void InvalidRecordCountsAndCrossResourceBudgetsFailBeforeLargeAllocations()
    {
        var bytes = ResourceFixture.Create();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 12u * (BinaryData.MaxRecords + 1u));
        Assert.Contains("million", Assert.Throws<SaveFormatException>(() => Open("huge.lsf", bytes)).Message, StringComparison.Ordinal);
        var budget = new ResourceBudget();
        budget.Reserve(BinaryData.MaxBufferSize - 1, 0, 0);
        Assert.Throws<SaveFormatException>(() => LsfReader.Read(ResourceFixture.Create(), budget));
    }

    [Fact]
    public void XmlDepthIsLimitedBeforeTheXmlTreeIsAllocated()
    {
        var xml = "<save>" + string.Concat(Enumerable.Repeat("<node>", 257)) +
            string.Concat(Enumerable.Repeat("</node>", 257)) + "</save>";
        Assert.Contains("nesting", Assert.Throws<SaveFormatException>(() => Open("deep.lsx", System.Text.Encoding.UTF8.GetBytes(xml))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeHonorsTitleMagicAndXmlEncodings()
    {
        var plugin = new BaldursGate3Plugin();
        var bytes = ResourceFixture.Create();
        Assert.Equal(ProbeScore.Title, plugin.Probe(MemorySaveSet.SingleFile("profile.lsf", bytes, TitleId)).Score);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("profile.lsf", bytes, "PPSA99999")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("profile.dat", bytes, TitleId)).IsMatch);
        var xml = " \r\n<save><region id='Test'/></save>";
        foreach (var encoding in new[] { System.Text.Encoding.UTF8, System.Text.Encoding.Unicode, System.Text.Encoding.BigEndianUnicode })
        {
            var data = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
            Assert.True(plugin.Probe(MemorySaveSet.SingleFile("test.lsx", data, TitleId)).IsMatch);
            using var document = Open("test.lsx", data);
            Assert.False(document.CanEdit);
        }
    }

    [Fact]
    public void DeterministicMutationCorpusOnlyRaisesFormatErrorsOrPreservesTheInput()
    {
        var random = new Random(18463);
        (string Name, byte[] Bytes)[] inputs =
        [
            ("test.lsf", ResourceFixture.Create()),
            ("test.lsf", ResourceFixture.Create(adjacency: true, compression: 2)),
            ("test.lsv", PackageFixture.Create(method: 3, payloads: [("test.lsf", ResourceFixture.Create())])),
        ];
        foreach (var (name, original) in inputs)
        {
            for (var i = 0; i < 100; i++)
            {
                var bytes = original.ToArray();
                bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                try
                {
                    using var document = Open(name, bytes);
                    Assert.True(Assert.Single(document.Serialize()).Content.Span.SequenceEqual(bytes));
                }
                catch (SaveFormatException)
                {
                    // Structurally valid mutations need not fail: the package digest is unverified.
                }
            }
        }
    }

    private static ISaveDocument Open(string name, byte[] bytes) => new BaldursGate3Plugin().Open(
        MemorySaveSet.SingleFile(name, bytes, TitleId));
}
