using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class FloatLimitConsistencyTests
{
    [Theory]
    [InlineData(3.4028234663852886e38, true)]
    [InlineData(-3.4028234663852886e38, true)]
    [InlineData(3.4028235e38, false)]
    [InlineData(-3.4028235e38, false)]
    [InlineData(1e300, false)]
    public void SinglePrecisionUsesItsRepresentableRange(double value, bool accepted)
    {
        var result = FieldValueCoercer.Normalize(FieldValue.Of(value), FloatSpec.Single);
        Assert.Equal(accepted, result.Ok);
        if (accepted) Assert.True(double.IsFinite(result.Value.AsDouble()));
    }

    [Fact]
    public void SinglePrecisionChecksDeclaredBoundsAfterRounding()
    {
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(0.1), FloatSpec.Single with { Max = 0.1 }).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(-0.1), FloatSpec.Single with { Min = -0.1 }).Ok);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of(1.00000001), FloatSpec.Single with { Max = 1 }).Ok);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of(-1.00000001), FloatSpec.Single with { Min = -1 }).Ok);
    }
}
