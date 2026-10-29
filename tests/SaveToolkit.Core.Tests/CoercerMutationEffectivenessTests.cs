using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class CoercerMutationEffectivenessTests
{
    private const double FirstInexactInteger = 9_007_199_254_740_992d;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JsonBooleansRetainTheirValue(bool value)
    {
        var result = FieldValueCoercer.FromJson(JsonSerializer.SerializeToElement(value), new BoolSpec());
        Assert.True(result.Ok, result.Error);
        Assert.Equal(value, result.Value.AsBool());
    }

    [Fact]
    public void BooleanFieldsRejectTextAndNumericValues()
    {
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("true"), new BoolSpec()).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(1L), new BoolSpec()).Ok);
    }

    [Fact]
    public void IntegerLowerBoundIsInclusive()
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Of(0L), IntegerSpec.UInt8);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(0, result.Value.AsInt32());
    }

    [Theory]
    [InlineData(FirstInexactInteger - 1, true)]
    [InlineData(-FirstInexactInteger + 1, true)]
    [InlineData(FirstInexactInteger, false)]
    [InlineData(-FirstInexactInteger, false)]
    [InlineData(1.5, false)]
    public void FloatingPointIntegersRequireAnExactSafeMagnitude(double value, bool accepted)
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Of(value), IntegerSpec.Int64);
        Assert.Equal(accepted, result.Ok);
        if (accepted) Assert.Equal((Int128)value, result.Value.AsInteger());
    }

    [Theory]
    [InlineData("0xnot-hex", false)]
    [InlineData("0x80000000000000000000000000000000", false)]
    [InlineData("0x7fffffffffffffffffffffffffffffff", true)]
    public void HexParsingRejectsInvalidAndOverflowingValues(string text, bool accepted)
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Of(text), new IntegerSpec(0, Int128.MaxValue));
        Assert.Equal(accepted, result.Ok);
        if (accepted) Assert.Equal(Int128.MaxValue, result.Value.AsInteger());
    }

    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    public void WideFlagFieldsAcceptTheHighestRepresentableValue(int bitWidth)
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Of(Int128.MaxValue), new FlagsSpec([], bitWidth));
        Assert.True(result.Ok, result.Error);
        Assert.Equal(Int128.MaxValue, result.Value.AsInteger());
    }

    [Theory]
    [InlineData(-2d, false)]
    [InlineData(-1d, true)]
    [InlineData(1d, true)]
    [InlineData(2d, false)]
    public void FloatBoundsRejectBothSidesAndAcceptTheirEndpoints(double value, bool accepted)
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Of(value), new FloatSpec { Min = -1, Max = 1 });
        Assert.Equal(accepted, result.Ok);
        if (accepted) Assert.Equal(value, result.Value.AsDouble());
    }

    [Fact]
    public void Float32MaximumIsAnInclusiveLimit()
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Of((double)float.MaxValue), FloatSpec.Single);
        Assert.True(result.Ok, result.Error);
        Assert.Equal((double)float.MaxValue, result.Value.AsDouble());
    }

    [Theory]
    [InlineData("\ntext")]
    [InlineData("\rtext")]
    public void SingleLineTextRejectsALineBreakAtTheFirstCharacter(string text) =>
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(text), new TextSpec()).Ok);

    [Fact]
    public void ExactNumberCoercionEnforcesThePublishedBounds()
    {
        var spec = new ExactNumberSpec { Max = "1" };
        Assert.False(FieldValueCoercer.Normalize(FieldValue.ExactNumber("2"), spec).Ok);
    }

    [Fact]
    public void NormalizedUnionRetainsTheSelectedTypeAndTheCoercedInnerValue()
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Union("integer", FieldValue.Of("12")),
            new UnionSpec([new("integer", "Integer", IntegerSpec.UInt8)]));
        Assert.True(result.Ok, result.Error);
        Assert.Equal(FieldValue.Union("integer", FieldValue.Of(12L)), result.Value);
    }
}
