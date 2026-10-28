using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ExactValueTests
{
    [Theory]
    [InlineData("12345678901234567890")]
    [InlineData("1.2345678901234567")]
    [InlineData("-0.0000E+0002")]
    [InlineData("1e1000000")]
    [InlineData("1e-1000000")]
    public void ExactNumber_PreservesLexemeAndWireType(string text)
    {
        var value = FieldValue.ExactNumber(text);
        var wire = JsonSerializer.Serialize(value, ToolkitJson.Options);
        Assert.Contains("\"kind\":\"exactNumber\"", wire, StringComparison.Ordinal);
        Assert.Equal(value, JsonSerializer.Deserialize<FieldValue>(wire, ToolkitJson.Options));
        Assert.NotEqual(FieldValue.Of(text), value);
        Assert.Throws<InvalidOperationException>(() => value.AsString());
        using var json = JsonDocument.Parse(wire);
        var result = FieldValueCoercer.FromJson(json.RootElement, new ExactNumberSpec());
        Assert.True(result.Ok, result.Error);
        Assert.Equal(text, result.Value.AsExactNumber());
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData(".1")]
    [InlineData("1.")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData(" 1")]
    [InlineData("1\n")]
    [InlineData("١")]
    public void ExactNumber_RejectsInvalidGrammar(string text)
    {
        Assert.NotNull(ExactJsonNumber.Validate(text));
        Assert.Throws<ArgumentException>(() => FieldValue.ExactNumber(text));
        var json = JsonSerializer.SerializeToElement(new { kind = "exactNumber", text });
        Assert.False(FieldValueCoercer.FromJson(json, new ExactNumberSpec()).Ok);
    }

    [Theory]
    [InlineData("1.20000000000000001", "1.2", "1.20000000000000002", true)]
    [InlineData("1.20000000000000003", "1.2", "1.20000000000000002", false)]
    [InlineData("-1e1000000", "-2e1000000", "-1e999999", true)]
    [InlineData("1e-1000000", "0", "2e-1000000", true)]
    [InlineData("-0E999999", "0.0", "0e-1", true)]
    [InlineData("10.00", "1e1", "100e-1", true)]
    public void ExactNumber_UsesExactInclusiveBounds(string text, string min, string max, bool ok) =>
        Assert.Equal(ok, ExactJsonNumber.Validate(text, new ExactNumberSpec { Min = min, Max = max }) is null);

    [Fact]
    public void ExactNumber_EnforcesResourceLimitsAndSpecValidity()
    {
        Assert.NotNull(ExactJsonNumber.Validate(new string('1', ExactJsonNumber.DefaultMaxLength + 1)));
        Assert.Throws<ArgumentException>(() => FieldValue.ExactNumber(new string('1', ExactJsonNumber.HardMaxLength + 1)));
        Assert.NotNull(ExactJsonNumber.Validate("1e1000001"));
        Assert.NotNull(ExactJsonNumber.Validate("1e-1000001"));
        Assert.NotNull(ExactJsonNumber.Validate("1e" + new string('9', 1000)));
        Assert.Null(ExactJsonNumber.Validate("1e+00000000000000000001"));
        Assert.NotNull(ExactJsonNumber.Validate("1", new ExactNumberSpec { MaxLength = int.MaxValue }));
        Assert.NotNull(ExactJsonNumber.Validate("1", new ExactNumberSpec { Min = "NaN" }));
        Assert.NotNull(ExactJsonNumber.Validate("1", new ExactNumberSpec { Min = "2", Max = "1" }));
    }

    [Fact]
    public void RawJsonNumber_IsReadExactlyBeforeAnyFloatingPointConversion()
    {
        using var json = JsonDocument.Parse("1.2345678901234567890e+10000");
        var result = FieldValueCoercer.FromJson(json.RootElement, new NullableSpec(new ExactNumberSpec()));
        Assert.True(result.Ok, result.Error);
        Assert.Equal(json.RootElement.GetRawText(), result.Value.AsExactNumber());
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("1"), new ExactNumberSpec()).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(1d), new ExactNumberSpec()).Ok);
    }

    [Fact]
    public void Nullable_DistinguishesNullMissingAndEmptyText()
    {
        var spec = new NullableSpec(new TextSpec());
        Assert.True(FieldValueCoercer.FromJson(JsonSerializer.SerializeToElement<string?>(null), spec).Value.IsNull);
        Assert.Equal(FieldValue.Of(""), FieldValueCoercer.FromJson(JsonSerializer.SerializeToElement(""), spec).Value);
        Assert.False(FieldValueCoercer.FromJson(default, spec).Ok);
        foreach (var text in new[] { "{}", "[]", "{\"kind\":\"exactNumber\",\"text\":\"1\",\"text\":\"2\"}" })
        {
            using var json = JsonDocument.Parse(text);
            Assert.False(FieldValueCoercer.FromJson(json.RootElement, spec).Ok);
        }
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Null, new TextSpec()).Ok);
        var node = new SaveToolkit.Sdk.Views.FieldNode("value", "Value", spec, FieldValue.Null);
        var wire = JsonSerializer.SerializeToElement(node, ToolkitJson.Options);
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("value").ValueKind);
    }

    [Fact]
    public void Union_PreservesExplicitChoiceAndInnerConstraints()
    {
        FieldSpec spec = new NullableSpec(new UnionSpec([
            new("id", "Integer", IntegerSpec.UInt8), new("text", "Text", new TextSpec { MaxLength = 3 }),
            new("number", "Exact number", new ExactNumberSpec()),
        ]));
        var reopenedSpec = JsonSerializer.Deserialize<FieldSpec>(JsonSerializer.Serialize(spec, ToolkitJson.Options), ToolkitJson.Options)!;
        foreach (var value in new[] { FieldValue.Union("id", FieldValue.Of(12L)), FieldValue.Union("text", FieldValue.Of("12")),
                     FieldValue.Union("number", FieldValue.ExactNumber("1.2300E+10")), FieldValue.Null })
        {
            var json = JsonSerializer.SerializeToElement(value, ToolkitJson.Options);
            var result = FieldValueCoercer.FromJson(json, reopenedSpec);
            Assert.True(result.Ok, result.Error);
            Assert.Equal(value, result.Value);
        }
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Union("id", FieldValue.Of(256L)), spec).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Union("text", FieldValue.Of("long")), spec).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Union("unknown", FieldValue.Null), spec).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("12"), spec).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Null, spec with { ReadOnly = true }).Ok);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Null, spec with { ReadOnly = true }, allowReadOnly: true).Ok);
    }

    [Theory]
    [InlineData("{\"kind\":\"union\",\"type\":\"text\"}")]
    [InlineData("{\"kind\":\"union\",\"type\":\"text\",\"value\":\"\",\"extra\":1}")]
    [InlineData("{\"kind\":\"union\",\"type\":\"text\",\"value\":\"\",\"value\":null}")]
    [InlineData("{\"kind\":\"union\",\"type\":\"\",\"value\":\"\"}")]
    [InlineData("{\"kind\":\"union\",\"type\":\"text\",\"value\":null}")]
    public void Union_RejectsMalformedWireValues(string wire)
    {
        using var json = JsonDocument.Parse(wire);
        var spec = new UnionSpec([new("text", "Text", new TextSpec()), new("", "Invalid", new TextSpec())]);
        Assert.False(FieldValueCoercer.FromJson(json.RootElement, spec).Ok);
    }
}
