using System.Text;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonTypeCharacterizationTests
{
    [Theory]
    [InlineData("{}", "object")]
    [InlineData("[]", "array")]
    [InlineData("null", "null")]
    [InlineData("true", "boolean")]
    [InlineData("false", "boolean")]
    [InlineData("\"1\"", "string")]
    [InlineData("-0.00e+00", "number")]
    [InlineData("1e999999999999999999999", "number")]
    public void SearchAndExplicitSchemaAgreeOnTokenType(string source, string type)
    {
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.json", Encoding.UTF8.GetBytes(source)));
        var row = Assert.Single(((IQueryableDocument)document).Query("json-tree", new()).Rows);
        Assert.Equal(type, Assert.IsType<TextNode>(row.Cells["type"]).Text);
        JsonTemplateSchema.Parse("{\"type\":\"" + type + "\"}").Validate(JsonTokenTree.Parse(source).Nodes[0]);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void SearchAndSchemaUseCurrentScalarTypeRatherThanImportedTokenKind()
    {
        var tree = JsonTokenTree.Parse("1");
        var search = new JsonSearch(tree);
        tree.Nodes[0].Current = JsonEditingTests.Text("1");
        JsonTemplateSchema.Parse("{\"type\":\"string\"}").Validate(tree.Nodes[0]);
        var row = Assert.Single(search.Query(tree, tree.Nodes[0], new(), (_, _) => new TextNode(""), _ => new TextNode("")).Rows);
        Assert.Equal("string", Assert.IsType<TextNode>(row.Cells["type"]).Text);
        tree.Nodes[0].Current = FieldValue.Null;
        JsonTemplateSchema.Parse("{\"type\":\"null\"}").Validate(tree.Nodes[0]);
    }
}
