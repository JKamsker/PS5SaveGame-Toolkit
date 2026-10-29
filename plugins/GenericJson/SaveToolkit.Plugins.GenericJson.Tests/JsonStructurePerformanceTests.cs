using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonStructurePerformanceTests
{
    [Fact]
    public void CachedStructureReviewRetainsTheCompleteIdentityDigest()
    {
        var tree = JsonTokenTree.Parse("[1,2,3]");
        var root = tree.Nodes[0];
        var first = JsonStructureActions.StructureValue(root).AsString();
        var digest = Convert.ToHexStringLower(SHA256.HashData("value/1, value/2, value/3"u8));
        Assert.Equal("array (3 entries): value/1, value/2, value/3; order/name SHA-256 " + digest, first);
        Assert.Same(first, JsonStructureActions.StructureValue(root).AsString());
        root.Children[0].Current = JsonEditingTests.Number("99");
        Assert.Same(first, JsonStructureActions.StructureValue(root).AsString());
    }

    [Theory]
    [InlineData("array/0/move", "[1,2,3]", "from", 0, "to", 2)]
    [InlineData("array/0/remove", "[1,2,3]", "at", 1, "", 0)]
    [InlineData("object/0/remove", "{\"a\":1,\"a\":2}", "at", 0, "", 0)]
    public void ReindexInvalidatesCachedMembershipAndOrder(string action, string source, string firstKey, int firstValue, string secondKey, int secondValue)
    {
        var tree = JsonTokenTree.Parse(source);
        var original = JsonStructureActions.StructureValue(tree.Nodes[0]);
        var parameters = new Dictionary<string, FieldValue> { [firstKey] = FieldValue.Of((long)firstValue) };
        if (secondKey.Length != 0) parameters.Add(secondKey, FieldValue.Of((long)secondValue));
        JsonStructureActions.Mutate(tree, action, parameters);
        Assert.NotEqual(original, JsonStructureActions.StructureValue(tree.Nodes[0]));
    }

    [Fact]
    public void LongMemberRenameInvalidatesFullNameDigestBeyondDisplayedPrefix()
    {
        var name = new string('x', 500);
        var tree = JsonTokenTree.Parse("{\"" + name + "\":1}");
        var before = JsonStructureActions.StructureValue(tree.Nodes[0]);
        JsonStructureActions.Mutate(tree, "object/0/rename", new Dictionary<string, FieldValue>
        {
            ["at"] = FieldValue.Of(0L), ["name"] = FieldValue.Of(name + "y"),
        });
        var after = JsonStructureActions.StructureValue(tree.Nodes[0]);
        Assert.NotEqual(before, after);
        Assert.Equal(before.AsString()[..160], after.AsString()[..160]);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"" + name + "y\":1}"), JsonTreeWriter.Write(tree));
    }
}
