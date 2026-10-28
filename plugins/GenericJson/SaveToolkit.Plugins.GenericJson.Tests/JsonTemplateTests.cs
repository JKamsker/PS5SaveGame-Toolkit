using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonTemplateTests
{
    [Theory]
    [InlineData("null", "null")]
    [InlineData("string", "\"\"")]
    [InlineData("number", "0")]
    [InlineData("boolean", "false")]
    [InlineData("array", "[]")]
    [InlineData("object", "{}")]
    public void ExplicitTypeChoiceDoesNotCopyNeighborOrUseCustomText(string type, string expected)
    {
        using var fixture = new JsonSessionFixture("[ {\"unknownGameFlag\":true} ]");
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("template", FieldValue.Of(type)), ("json", FieldValue.Of("invalid ignored custom text")));
        Assert.StartsWith("[ " + expected + ",", Encoding.UTF8.GetString(fixture.Bytes), StringComparison.Ordinal);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal("[ {\"unknownGameFlag\":true} ]", Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Fact]
    public void SchemaGeneratesOnlyExplicitRequiredPropertiesAndRetainsExactDefaultLexemes()
    {
        const string schema = """
            {"type":"object","required":["n","entries"],"additionalProperties":false,"properties":{
              "n":{"type":"number","default":1.230000e-999999},
              "optional":{"type":"boolean","default":true},
              "entries":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"object","required":["text"],"properties":{"text":{"type":"string","default":"\u0041"}}}}
            }}
            """;
        using var fixture = new JsonSessionFixture("{}");
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "object/0/insert", ("at", FieldValue.Of(0L)), ("name", FieldValue.Of("explicit")),
            ("template", FieldValue.Of("schema")), ("schema", FieldValue.Of(schema)));
        var text = Encoding.UTF8.GetString(fixture.Bytes);
        Assert.Contains("1.230000e-999999", text, StringComparison.Ordinal);
        Assert.DoesNotContain("optional", text, StringComparison.Ordinal);
        Assert.Contains("\"entries\":[{\"text\":\"\\u0041\"},{\"text\":\"\\u0041\"}]", text, StringComparison.Ordinal);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal("{}", Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"required\":[\"x\"],\"properties\":{\"x\":{\"type\":\"number\"}}}", "{}")]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":false}", "{\"extra\":1}")]
    [InlineData("{\"type\":\"object\"}", "{\"x\":1,\"x\":2}")]
    [InlineData("{\"type\":\"array\",\"minItems\":1}", "[]")]
    [InlineData("{\"type\":\"array\",\"maxItems\":1}", "[0,1]")]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"number\"}}", "[\"1\"]")]
    [InlineData("{\"type\":\"null\"}", "\"\"")]
    public void CustomTemplatesMustSatisfySuppliedSchemaWithoutSilentRepair(string schema, string json) => Reject(schema, json, "custom");

    [Theory]
    [InlineData("{\"type\":\"number\",\"minimum\":0}")]
    [InlineData("{\"type\":\"object\",\"$ref\":\"https://example.com/schema\"}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"unused\":{\"type\":\"string\",\"pattern\":\".*\"}}}")]
    [InlineData("{\"type\":\"object\",\"type\":\"object\"}")]
    [InlineData("{\"type\":\"array\",\"minItems\":2,\"maxItems\":1}")]
    [InlineData("{\"type\":\"array\",\"minItems\":-1}")]
    [InlineData("{\"type\":\"array\",\"minItems\":0.5}")]
    [InlineData("{\"type\":\"array\",\"minItems\":129,\"items\":{\"type\":\"number\"}}")]
    [InlineData("{\"type\":\"array\",\"minItems\":1}")]
    [InlineData("{\"type\":\"number\",\"default\":\"1\"}")]
    [InlineData("{\"type\":\"object\",\"required\":[\"x\"]}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"null\"}},\"required\":[\"x\",\"x\"]}")]
    [InlineData("{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"}}")]
    [InlineData("{\"type\":\"integer\"}")]
    public void UnsupportedAmbiguousAndInvalidSchemasRejectExplicitly(string schema) => Reject(schema);

    [Fact]
    public void SchemaExpansionAndTextBudgetsRejectWithoutMutating()
    {
        Reject("{\"type\":\"array\",\"minItems\":128,\"items\":{\"type\":\"array\",\"minItems\":128,\"items\":{\"type\":\"number\"}}}");
        Reject("{\"type\":\"array\",\"minItems\":128,\"items\":{\"type\":\"string\",\"default\":\"" + new string('a', 16_384) + "\"}}");
        Reject(new string(' ', 64 * 1024 + 1));
        Reject("{\"type\":\"array\",\"default\":[" + string.Join(',', Enumerable.Repeat("null", 4096)) + "]}");
    }

    [Fact]
    public void CustomValidationPreservesExplicitBytesAndDoesNotFillOptionalDefaults()
    {
        using var fixture = new JsonSessionFixture("[]");
        _ = fixture.Fields();
        const string json = "{ \"n\" : 1.000e-999999 }";
        const string schema = """
            {"type":"object","required":["n"],"additionalProperties":false,
             "properties":{"n":{"type":"number"},"optional":{"type":"boolean","default":true}}}
            """;
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)),
            ("template", FieldValue.Of("custom")), ("schema", FieldValue.Of(schema)), ("json", FieldValue.Of(json)));
        Assert.Equal("[" + json + "]", Encoding.UTF8.GetString(fixture.Bytes));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal("[]", Encoding.UTF8.GetString(fixture.Bytes));
    }

    private static void Reject(string schema, string json = "null", string template = "schema")
    {
        using var fixture = new JsonSessionFixture("[]");
        _ = fixture.Fields();
        var preview = fixture.Session.PlanAction("array/0/insert", JsonArrayEditingTests.Parameters(
            ("at", FieldValue.Of(0L)), ("template", FieldValue.Of(template)), ("schema", FieldValue.Of(schema)), ("json", FieldValue.Of(json))));
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal("[]", Encoding.UTF8.GetString(fixture.Bytes));
        Assert.False(fixture.Session.State.CanUndo);
    }
}
