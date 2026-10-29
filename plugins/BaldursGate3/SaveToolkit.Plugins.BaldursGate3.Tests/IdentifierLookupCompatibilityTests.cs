using System.Text;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class IdentifierLookupCompatibilityTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("single")]
    [InlineData("duplicate")]
    [InlineData("mixed-types")]
    [InlineData("wrong-type")]
    [InlineData("wrong-case")]
    [InlineData("at-limit")]
    [InlineData("over-limit")]
    public void ObjectLabelsRequireOneExactTypedAttributeWithinTheByteLimit(string scenario)
    {
        byte[] entityBytes = [1, .. new byte[15]];
        byte[] templateBytes = [2, .. new byte[15]];
        var template = LsmfAccess.Guid(templateBytes);
        var text = scenario == "at-limit" ? new string('x', 511) : scenario == "over-limit" ? new string('x', 512) : "OBJ_Goldpile";
        var stats = new ResourceAttribute("Stats", LarianAttributeType.FixedString, Encoding.UTF8.GetBytes(text + '\0'));
        var creator = new ResourceNode("Creator", 0);
        creator.Attributes.AddRange([0, 1]);
        var item = new ResourceNode("Item", -1);
        item.Attributes.Add(2);
        var attributes = new List<ResourceAttribute>
        {
            new("Entity", LarianAttributeType.UUID, entityBytes),
            new("TemplateID", LarianAttributeType.FixedString, Encoding.UTF8.GetBytes(template + '\0')),
            new("CurrentTemplate", LarianAttributeType.UUID, templateBytes),
        };
        if (scenario != "missing")
        {
            item.Attributes.Add(attributes.Count);
            attributes.Add(scenario switch
            {
                "wrong-type" => stats with { Type = LarianAttributeType.String },
                "wrong-case" => stats with { Name = "stats" },
                _ => stats,
            });
        }
        if (scenario is "duplicate" or "mixed-types")
        {
            item.Attributes.Add(attributes.Count);
            attributes.Add(scenario == "mixed-types" ? stats with { Type = LarianAttributeType.String } : stats);
        }
        var resource = new LarianResource("synthetic", [new("Creators", -1), creator, item], attributes, [0, 2]);
        var index = new LsmfObjectIndex(resource);
        Assert.Equal(scenario is "single" or "at-limit" ? text : "Item (unknown Stats)", index.Find(LsmfAccess.Guid(entityBytes)));
        Assert.Null(index.Find(LsmfAccess.Guid(new byte[16])));
    }
}
