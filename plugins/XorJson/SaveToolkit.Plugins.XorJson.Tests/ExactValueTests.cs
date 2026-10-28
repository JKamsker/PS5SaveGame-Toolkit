using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class ExactValueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HugeNumbersNegativeZeroAndSpellingSurviveEditsAndUndo(bool xor)
    {
        const string original = "{\"a\":9007199254740993123456789012345678901234567890,\"b\":-0,\"c\":1.2300E+0002,\"d\":1e-9999,\"e\":1e9999}";
        var bytes = XorJsonTests.Encode(original, xor);
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        Assert.All(fields, f => Assert.IsType<ExactNumberSpec>(Assert.IsType<NullableSpec>(f.Spec).Inner));
        Assert.Equal("-0", fields[1].Value.AsExactNumber());
        const string next = "9.876543210987654321000e+12345";
        Assert.True(document.ApplyEdits([new(fields[2].ValueId, FieldValue.ExactNumber(next))]).Succeeded);
        Assert.Equal(XorJsonTests.Encode(original.Replace("1.2300E+0002", next, StringComparison.Ordinal), xor), document.Serialize()[0].Content.ToArray());
        using var reopened = XorJsonTests.Open(document.Serialize()[0].Content.ToArray());
        _ = XorJsonTests.Fields(reopened);
        Assert.Equal(next, reopened.GetValue(fields[2].ValueId).AsExactNumber());
        Assert.True(document.ApplyEdits([new(fields[2].ValueId, fields[2].Value)]).Succeeded);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void NullEmptyTextAndAbsentMembersRemainDistinctAndTypeChoiceIsExplicit()
    {
        const string original = "{\"null\":null,\"text\":\"\",\"number\":-0}";
        using var document = XorJsonTests.Open(XorJsonTests.Encode(original));
        var fields = XorJsonTests.Fields(document);
        Assert.Equal(3, fields.Count);
        Assert.IsType<UnionSpec>(Assert.IsType<NullableSpec>(fields[0].Spec).Inner);
        Assert.False(document.ApplyEdits([new(fields[0].ValueId, FieldValue.Of(""))]).Succeeded);
        Assert.False(document.ApplyEdits([new(fields[0].ValueId, FieldValue.Union("number", FieldValue.Of("1")))]).Succeeded);
        Assert.True(document.ApplyEdits([
            new(fields[0].ValueId, FieldValue.Union("string", FieldValue.Of(""))),
            new(fields[2].ValueId, FieldValue.Null),
        ]).Succeeded);
        Assert.Equal(XorJsonTests.Encode("{\"null\":\"\",\"text\":\"\",\"number\":null}"), document.Serialize()[0].Content.ToArray());
        Assert.True(document.ApplyEdits([new(fields[0].ValueId, fields[0].Value), new(fields[2].ValueId, fields[2].Value)]).Succeeded);
        Assert.Equal(XorJsonTests.Encode(original), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void BeyondHardLimitsIsPreservedReadOnlyWithoutBlockingOtherFields()
    {
        var number = "1" + new string('0', 16_384);
        var bytes = XorJsonTests.Encode("[" + number + ",1e1000000001,0]");
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        Assert.True(fields[0].Spec.ReadOnly);
        Assert.True(fields[1].Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(fields[0].ValueId, FieldValue.Of("1"))]).Succeeded);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        Assert.True(document.ApplyEdits([new(fields[2].ValueId, FieldValue.ExactNumber("-0.000E+00"))]).Succeeded);
        Assert.EndsWith(",-0.000E+00]", Encoding.UTF8.GetString(document.Serialize()[0].Content.ToArray().Select(b => unchecked((byte)(b + 1))).ToArray()));
    }

    [Fact]
    public void TypedPathsDistinguishDuplicateMembersLiteralPunctuationAndArrayIndices()
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("{\"a/b\":1,\"a/b\":2,\"0\":3,\"a\":[4]}"));
        var fields = XorJsonTests.Fields(document);
        Assert.Equal(4, fields.Select(f => f.ValueId).Distinct().Count());
        Assert.Contains("member(\"a/b\",0)", fields[0].Description!);
        Assert.Contains("member(\"a/b\",1)", fields[1].Description!);
        Assert.Contains("member(\"0\",0)", fields[2].Description!);
        Assert.Contains("element(0)", fields[3].Description!);
    }

    [Fact]
    public void OversizedScalarBatchRollsBackEveryFieldBeforeReturning()
    {
        var bytes = XorJsonTests.Encode("{\"n\":-0,\"s\":\"\\u0041\"}");
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        // UTF-16 length fits, but escaped JSON output exceeds the byte budget.
        var tooLargeWhenEscaped = new string('\u0001', 3 * 1024 * 1024);
        var result = document.ApplyEdits([new(fields[0].ValueId, FieldValue.ExactNumber("1")), new(fields[1].ValueId, FieldValue.Of(tooLargeWhenEscaped))]);
        Assert.False(result.Succeeded);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        Assert.Equal(fields[0].Value, document.GetValue(fields[0].ValueId));
        Assert.Equal(fields[1].Value, document.GetValue(fields[1].ValueId));
    }
}
