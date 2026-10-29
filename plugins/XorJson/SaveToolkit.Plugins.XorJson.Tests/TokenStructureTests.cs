using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class TokenStructureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParserRetainsPreorderIdsDuplicateOccurrencesAndUtf8Offsets(bool xor)
    {
        const string text = "\uFEFF{\"é\":{\"x\":-0,\"x\":1.00E+02},\"rows\":[null,true,\"\\u0041\"]}\r\n";
        var bytes = XorJsonTests.Encode(text, xor);
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        Assert.Equal(["value/2", "value/3", "value/5", "value/6", "value/7"], fields.Select(field => field.ValueId));
        Assert.Equal(["x", "x", "[0]", "[1]", "[2]"], fields.Select(field => field.Label));
        Assert.Equal(["-0", "1.00E+02", "null", "true", "A"], fields.Select(field => field.Value.ToString()));
        Assert.Equal([
            "$.member(\"\\u00E9\",0).member(\"x\",0); original UTF-8 token at 0xE.",
            "$.member(\"\\u00E9\",0).member(\"x\",1); original UTF-8 token at 0x15.",
            "$.member(\"rows\",0).element(0); original UTF-8 token at 0x27.",
            "$.member(\"rows\",0).element(1); original UTF-8 token at 0x2C.",
            "$.member(\"rows\",0).element(2); original UTF-8 token at 0x31.",
        ], fields.Select(field => field.Description));
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertRemoveAndReplaceRenumberPathsWhileRetainingSurvivingIdsAndTrivia(bool xor)
    {
        const string original = "{ \"rows\" : [ 1.00E+02, -0 ], \"\\u0078\" : 7, \"x\":8 }\r\n";
        using var document = XorJsonTests.Open(XorJsonTests.Encode(original, xor));
        var originalFields = XorJsonTests.Fields(document);
        var removed = document.InvokeAction("json/remove/2", new Dictionary<string, FieldValue>());
        var remaining = XorJsonTests.Fields(document);
        Assert.Equal(originalFields[1].ValueId, remaining[0].ValueId);
        Assert.StartsWith("$.member(\"rows\",0).element(0);", remaining[0].Description!);
        var added = document.InvokeAction("json/add/1", Parameters("{\"new\":2.00e+1}"));
        var inserted = XorJsonTests.Fields(document).Single(field => field.Label == "new");
        Assert.Equal("value/7", inserted.ValueId);
        var replaced = document.InvokeAction("json/replace/4", Parameters("[true,null]"));
        Assert.Equal(XorJsonTests.Encode("{ \"rows\" : [ -0,{\"new\":2.00e+1} ], \"\\u0078\" : [true,null], \"x\":8 }\r\n", xor),
            document.Serialize().Single().Content.ToArray());
        var actions = StructuralTests.Nodes(document).OfType<ActionNode>();
        Assert.Contains(actions, action => action.ActionId == "json/remove/4"); // Replaced root retains its old ID.
        _ = document.RestoreMemento(replaced.Revert!);
        _ = document.RestoreMemento(added.Revert!);
        _ = document.RestoreMemento(removed.Revert!);
        Assert.Equal(XorJsonTests.Encode(original, xor), document.Serialize().Single().Content.ToArray());
    }

    private static Dictionary<string, FieldValue> Parameters(string json) => new() { ["json"] = FieldValue.Of(json) };
}
