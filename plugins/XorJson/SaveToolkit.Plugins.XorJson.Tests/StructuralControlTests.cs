using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class StructuralControlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericScalarAndContainerControlsRetainTheirIdsAndParameters(bool xor)
    {
        var original = XorJsonTests.Encode("{\"n\":-0,\"rows\":[1.00E+02]}", xor);
        using var document = XorJsonTests.Open(original);
        var nodes = StructuralTests.Nodes(document);
        var actions = nodes.OfType<ActionNode>().Where(action => action.ActionId != "json/inspect" &&
            action.ActionId != "json/bulk-set").ToArray();
        Assert.Equal(["json/add/0", "json/remove/1", "json/replace/1", "json/add/2", "json/remove/2",
            "json/replace/2", "json/remove/3", "json/replace/3"], actions.Select(action => action.ActionId));
        Assert.All(actions, action => Assert.Equal(
            "Advanced raw JSON operation. Unknown game dependencies may make the save invalid.", action.Confirm));
        Assert.Equal(["key", "json"], actions[0].Parameters.Select(parameter => parameter.ValueId));
        Assert.Equal("json", Assert.Single(actions[3].Parameters).ValueId);
        Assert.Empty(actions[1].Parameters);
        Assert.Equal("json", Assert.Single(actions[2].Parameters).ValueId);
        Assert.Equal(2, nodes.OfType<SectionNode>().Count(section => section.Title == "Structural actions"));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CharacterControlsRemainPublishedButDispatchHonorsTheWriteGate(bool online)
    {
        var original = Encoding.UTF8.GetBytes("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472}," +
            "\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":" + (online ? "true" : "false") + ",\"Level\":1}}}");
        using var document = XorJsonTests.Open(original);
        var nodes = StructuralTests.Nodes(document);
        var level = nodes.OfType<FieldNode>().Single(field => field.Label == "Level");
        Assert.Equal(online, level.Spec.ReadOnly);
        Assert.Contains(nodes.OfType<ActionNode>(), action => action.ActionId == "json/add/6");
        Assert.DoesNotContain(nodes.OfType<SectionNode>(), section => section.Title == "Level");
        var parameters = new Dictionary<string, FieldValue> { ["key"] = FieldValue.Of("future"), ["json"] = FieldValue.Of("-0.00E+02") };
        if (online)
        {
            var error = Assert.Throws<SaveFormatException>(() => document.InvokeAction("json/add/6", parameters));
            Assert.Equal("Writes are disabled for this character version or online mode.", error.Message);
        }
        else
        {
            var outcome = document.InvokeAction("json/add/6", parameters);
            Assert.True(outcome.StructureChanged);
            Assert.Contains(StructuralTests.Nodes(document).OfType<FieldNode>(), field => field.Label == "future" && field.Value.AsExactNumber() == "-0.00E+02");
            _ = document.RestoreMemento(outcome.Revert!);
        }
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
