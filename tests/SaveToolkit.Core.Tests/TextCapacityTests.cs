using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class TextCapacityTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("ASCII", 5)]
    [InlineData("é", 2)]
    [InlineData("e\u0301", 3)]
    [InlineData("日本", 6)]
    [InlineData("🎮", 4)]
    [InlineData("\uFEFF", 3)]
    [InlineData("\0", 1)]
    public void LogicalValueFitsExactlyWithoutNormalizationOrStorageTerminator(string text, int bytes)
    {
        var spec = new TextSpec { MaxUtf8Bytes = bytes };
        var result = FieldValueCoercer.Normalize(FieldValue.Of(text), spec);
        Assert.True(result.Ok);
        Assert.Equal(text, result.Value.AsString());
        Assert.True(FieldValueCoercer.FromJson(JsonSerializer.SerializeToElement(text), spec).Ok);
        if (bytes > 0)
            Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(text), spec with { MaxUtf8Bytes = bytes - 1 }).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(text + "a"), spec).Ok);
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void InvalidScalarsAreControlledErrorsWithoutReplacement(int surrogate)
    {
        var text = "a" + (char)surrogate + "b";
        var result = FieldValueCoercer.Normalize(FieldValue.Of(text), new TextSpec { MaxUtf8Bytes = 100 });
        Assert.False(result.Ok);
        Assert.Equal("Text must contain valid Unicode scalar sequences.", result.Error);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of(text), new TextSpec()).Ok); // Legacy opt-out.
    }

    [Theory]
    [InlineData("\"\\uD800\"")]
    [InlineData("{\"kind\":\"union\",\"type\":\"text\",\"value\":\"\\uDC00\"}")]
    public void MalformedEscapedScalarsDoNotEscapeJsonValidation(string json)
    {
        using var document = JsonDocument.Parse(json);
        FieldSpec spec = json.StartsWith('"') ? new TextSpec { MaxUtf8Bytes = 10 }
            : new UnionSpec([new("text", "Text", new TextSpec { MaxUtf8Bytes = 10 })]);
        Assert.False(FieldValueCoercer.FromJson(document.RootElement, spec).Ok);
    }

    [Fact]
    public void CapacityIsOptionalNonnegativeAndIndependentOfUtf16AndLinePolicy()
    {
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(""), new TextSpec { MaxUtf8Bytes = -1 }).Ok);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of("🎮"), new TextSpec { MaxUtf8Bytes = int.MaxValue }).Ok);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of("🎮"), new TextSpec { MaxLength = 2, MaxUtf8Bytes = 4 }).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("🎮"), new TextSpec { MaxLength = 1, MaxUtf8Bytes = 4 }).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("a\nb"), new TextSpec { MaxUtf8Bytes = 3 }).Ok);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of("a\nb"), new TextSpec { MaxUtf8Bytes = 3, Multiline = true }).Ok);
    }

    [Fact]
    public void WireRoundtripAndMetadataAgreementRetainNestedCapacity()
    {
        FieldSpec spec = new NullableSpec(new UnionSpec([new("caption", "Caption", new TextSpec { MaxLength = 10, MaxUtf8Bytes = 0 })]));
        var snapshot = FieldSpecMetadata.Snapshot(spec);
        var json = JsonSerializer.Serialize(spec, ToolkitJson.Options);
        Assert.Contains("\"maxUtf8Bytes\":0", json, StringComparison.Ordinal);
        Assert.True(FieldSpecMetadata.Agree(spec, snapshot));
        Assert.True(FieldSpecMetadata.Agree(spec, JsonSerializer.Deserialize<FieldSpec>(json, ToolkitJson.Options)!));
        Assert.False(FieldSpecMetadata.Agree(new TextSpec { MaxUtf8Bytes = 4 }, new TextSpec { MaxUtf8Bytes = 5 }));
        Assert.DoesNotContain("maxUtf8Bytes", JsonSerializer.Serialize<FieldSpec>(new TextSpec(), ToolkitJson.Options), StringComparison.Ordinal);
    }
}
