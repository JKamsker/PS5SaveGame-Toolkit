using System.Text.Json;
using System.Text.Json.Nodes;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Core;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class TextCapacityContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(4)]
    public void FieldAndActionParameterWireSpecsRetainOptionalCapacity(int? capacity)
    {
        var spec = new TextSpec { MaxUtf8Bytes = capacity, MaxLength = 12 };
        var field = new FieldNode("caption", "Caption", spec, FieldValue.Of(""));
        ViewNode view = new SectionNode("Save", [field, new ActionNode("rename", "Rename") { Parameters = [field] }]);
        var json = JsonNode.Parse(JsonSerializer.Serialize(view, ToolkitJson.Options))!;
        foreach (var wireSpec in new[] { json["children"]![0]!["spec"]!, json["children"]![1]!["parameters"]![0]!["spec"]! })
        {
            Assert.Equal(12, wireSpec["maxLength"]!.GetValue<int>());
            Assert.Equal(capacity.HasValue, wireSpec.AsObject().ContainsKey("maxUtf8Bytes"));
            if (capacity is { } bytes) Assert.Equal(bytes, wireSpec["maxUtf8Bytes"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task OpenApiPublishesOptionalIntegerByteCapacity()
    {
        using var host = OpenApiContractTests.CreateHost("Production");
        using var client = host.CreateClient();
        var json = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        var schema = json["components"]!["schemas"]!["TextSpec"]!;
        var capacity = schema["properties"]!["maxUtf8Bytes"]!;
        Assert.Equal(["integer", "null", "string"], capacity["type"]!.AsArray().Select(item => item!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Equal("int32", capacity["format"]!.GetValue<string>());
        Assert.DoesNotContain(schema["required"]?.AsArray() ?? [], item => item?.GetValue<string>() == "maxUtf8Bytes");
    }
}
