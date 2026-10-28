using System.Text;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LsmfObjectIndexTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateIdentityAcceptsBothObservedStoredTypesWithoutConvertingNamespaces(bool textual)
    {
        var index = new LsmfObjectIndex(Resource(textual));
        Assert.Equal("OBJ_Test", index.Find("00000001-0000-0000-0000-000000000000"));
        Assert.Null(index.Find("00000000-0000-0000-0000-000000000000"));
    }

    [Fact]
    public void RepeatedTemplatesMayShareALabelButConflictingLabelsAndCreatorMappingsStayUnresolved()
    {
        var resource = Resource(false);
        var item = new ResourceNode("Item", -1);
        item.Attributes.AddRange([0, 1]);
        resource = resource with { Nodes = [.. resource.Nodes, item] };
        Assert.Equal("OBJ_Test", new LsmfObjectIndex(resource).Find("00000001-0000-0000-0000-000000000000"));
        item.Attributes[1] = 4;
        resource = resource with { Attributes = [.. resource.Attributes, new("Stats", LarianAttributeType.FixedString, "OBJ_Other\0"u8.ToArray())] };
        Assert.Null(new LsmfObjectIndex(resource).Find("00000001-0000-0000-0000-000000000000"));
        var duplicate = Resource(false);
        duplicate = duplicate with { Nodes = [.. duplicate.Nodes, duplicate.Nodes[2]] };
        Assert.Null(new LsmfObjectIndex(duplicate).Find("00000001-0000-0000-0000-000000000000"));
    }

    private static LarianResource Resource(bool textual)
    {
        var item = new ResourceNode("Item", -1);
        item.Attributes.AddRange([0, 1]);
        var creator = new ResourceNode("Creator", 1);
        creator.Attributes.AddRange([2, 3]);
        return new LarianResource("test", [item, new ResourceNode("Creators", -1), creator],
        [
            new("CurrentTemplate", LarianAttributeType.UUID, new byte[16]),
            new("Stats", LarianAttributeType.FixedString, "OBJ_Test\0"u8.ToArray()),
            new("Entity", LarianAttributeType.UUID, Convert.FromHexString("01000000000000000000000000000000")),
            textual ? new("TemplateID", LarianAttributeType.FixedString, Encoding.UTF8.GetBytes("00000000-0000-0000-0000-000000000000\0"))
                : new("TemplateID", LarianAttributeType.UUID, new byte[16]),
        ], []);
    }
}
