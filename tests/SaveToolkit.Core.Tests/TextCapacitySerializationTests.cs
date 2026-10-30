using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class TextCapacitySerializationTests
{
    // Exact pre-capacity SDK JSON, also reproduced against base22d9748f in the CI investigation.
    private const string LegacyDefault = """{"kind":"text","MaxLength":null,"Multiline":false,"ReadOnly":false,"Unit":null}""";
    private const string LegacyWeb = """{"kind":"text","maxLength":null,"multiline":false,"readOnly":false,"unit":null}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsentAndExplicitNullKeepTheCompleteLegacyShape(bool webNames)
    {
        var options = Options(webNames);
        var expected = webNames ? LegacyWeb : LegacyDefault;
        Assert.Equal(expected, JsonSerializer.Serialize<FieldSpec>(new TextSpec(), options));
        var explicitNull = JsonNode.Parse(expected)!.AsObject();
        explicitNull[Name("MaxUtf8Bytes", webNames)] = null;
        foreach (var json in new[] { expected, explicitNull.ToJsonString() })
        {
            var restored = Assert.IsType<TextSpec>(JsonSerializer.Deserialize<FieldSpec>(json, options));
            Assert.Null(restored.MaxUtf8Bytes);
            Assert.Equal(expected, JsonSerializer.Serialize<FieldSpec>(restored, options));
            Assert.True(FieldSpecMetadata.Agree(new TextSpec(), restored));
        }
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void DefaultSerializersPreserveOnlySuppliedCapacityAcrossViewsAndParameters(bool webNames, int? capacity)
    {
        var options = Options(webNames);
        var spec = new TextSpec { MaxLength = 12, MaxUtf8Bytes = capacity };
        var field = new FieldNode("caption", "Caption", spec, FieldValue.Of(""));
        var nested = new FieldNode("nested", "Nested", new NullableSpec(new UnionSpec([new("text", "Text", spec)])), FieldValue.Null);
        ViewNode view = new SectionNode("Save", [field, nested, new ActionNode("rename", "Rename") { Parameters = [field] }]);
        var json = JsonNode.Parse(JsonSerializer.Serialize(view, options))!;
        var children = json[Name("Children", webNames)]!;
        var wireSpecs = new[]
        {
            children[0]![Name("Spec", webNames)]!,
            children[1]![Name("Spec", webNames)]![Name("Inner", webNames)]![Name("Options", webNames)]![0]![Name("Spec", webNames)]!,
            children[2]![Name("Parameters", webNames)]![0]![Name("Spec", webNames)]!,
        };
        foreach (var wire in wireSpecs)
        {
            Assert.Equal(capacity.HasValue, wire.AsObject().ContainsKey(Name("MaxUtf8Bytes", webNames)));
            if (capacity is { } bytes) Assert.Equal(bytes, wire[Name("MaxUtf8Bytes", webNames)]!.GetValue<int>());
            var restored = JsonSerializer.Deserialize<FieldSpec>(wire.ToJsonString(), options)!;
            Assert.Equal(spec, restored);
            Assert.True(FieldSpecMetadata.Agree(spec, restored));
        }
    }

    private static JsonSerializerOptions Options(bool webNames) => new(webNames ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General)
    {
        // Other optional legacy properties remain null; only the additive hint owns its omission policy.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static string Name(string property, bool webNames) => webNames ? JsonNamingPolicy.CamelCase.ConvertName(property) : property;
}
