using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class ParserCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NodeErrorsPrecedeAttributeErrorsAndAttributeErrorsPrecedeKeyErrors(bool adjacency)
    {
        var bytes = ResourceFixture.Create(adjacency: adjacency);
        var binary = LsfBinary.Read(bytes);
        var nodes = binary.Data(LsfLayout.NodesPair).ToArray();
        var attributes = binary.Data(LsfLayout.AttributesPair).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(adjacency ? LsfLayout.ExtendedParent : LsfLayout.CompactParent), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(LsfLayout.AttributeTypeAndLength), LsfLayout.AttributeTypeMask);
        var corrupt = binary.Rebuild(new Dictionary<int, byte[]>
        {
            [binary.Pair(LsfLayout.NodesPair)] = nodes,
            [binary.Pair(LsfLayout.AttributesPair)] = attributes,
        });
        Assert.Equal("Invalid LSOF parent reference (parents must precede children).",
            Assert.Throws<SaveFormatException>(() => LsfReader.Read(corrupt)).Message);
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(adjacency ? LsfLayout.ExtendedParent : LsfLayout.CompactParent), -1);
        var replacements = new Dictionary<int, byte[]>
        {
            [binary.Pair(LsfLayout.NodesPair)] = nodes,
            [binary.Pair(LsfLayout.AttributesPair)] = attributes,
            [binary.Pair(LsfLayout.KeysPair)] = new byte[LsfLayout.KeyRecordSize],
        };
        BinaryPrimitives.WriteInt32LittleEndian(replacements[binary.Pair(LsfLayout.KeysPair)], -1);
        corrupt = binary.Rebuild(replacements);
        Assert.Equal("Unsupported LSOF attribute type 63.",
            Assert.Throws<SaveFormatException>(() => LsfReader.Read(corrupt)).Message);
    }

    [Fact]
    public void XmlProjectionRetainsBreadthFirstOrderMetadataAndConcatenatedDirectText()
    {
        var resource = LsxReader.Read(Encoding.UTF8.GetBytes(
            "<save id='root'> before <n id='first'>one<![CDATA[ two]]><leaf/></n> after <n id='second'/></save>"));
        Assert.Equal(["save · root", "n · first", "n · second", "leaf"], resource.Nodes.Select(node => node.Name));
        Assert.Equal([-1, 0, 0, 1], resource.Nodes.Select(node => node.Parent));
        Assert.Equal([1, 2], resource.Nodes[0].Children);
        Assert.Equal([3], resource.Nodes[1].Children);
        Assert.Equal([0, 1], resource.Nodes[0].Attributes);
        Assert.Equal([2, 3], resource.Nodes[1].Attributes);
        Assert.Equal(["id", "#text", "id", "#text", "id"], resource.Attributes.Select(attribute => attribute.Name));
        Assert.Equal(["root", " before  after ", "first", "one two", "second"],
            resource.Attributes.Select(attribute => attribute.XmlValue));
        Assert.Equal([0], resource.Roots);
    }

    [Fact]
    public void XmlReservesCompleteModelBeforeCheckingTheRootName()
    {
        var budget = new ResourceBudget();
        Assert.Equal("LSX must have a <save> root element.",
            Assert.Throws<SaveFormatException>(() => LsxReader.Read("<other a='1'/>"u8.ToArray(), budget)).Message);
        budget.Reserve(0, BinaryData.MaxRecords - 1, BinaryData.MaxRecords - 1);
        Assert.Equal("BG3 document exceeds one million resource nodes or attributes.",
            Assert.Throws<SaveFormatException>(() => budget.Reserve(0, 1, 0)).Message);
    }

    [Fact]
    public void InvalidTableMetadataPrecedesPhysicalTruncation()
    {
        var bytes = ResourceFixture.Create()[..^1];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(LsfLayout.MetadataOffset + LsfLayout.NodesPair * LsfLayout.SizePairLength), 1);
        Assert.Equal("Invalid LSOF table size or excessive record count (one million limit).",
            Assert.Throws<SaveFormatException>(() => LsfBinary.Read(bytes)).Message);
    }
}
