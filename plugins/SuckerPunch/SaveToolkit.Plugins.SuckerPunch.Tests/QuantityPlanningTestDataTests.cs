using SaveToolkit.Sdk.Values;
using static SaveToolkit.Plugins.SuckerPunch.Tests.QuantityPlanningTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class QuantityPlanningTestDataTests
{
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public void TargetPreservesExactIntegerAndReturnsIndependentParameters(long value)
    {
        var first = Target(value);
        var second = Target(value);
        Assert.Equal("quantity", Assert.Single(first).Key);
        Assert.Equal(FieldValueKind.Integer, first["quantity"].Kind);
        Assert.Equal(value, first["quantity"].AsInteger());
        Assert.Equal(first, second);
        first["quantity"] = FieldValue.Null;
        first.Add("extra", FieldValue.Of(true));
        Assert.Single(second);
        Assert.Equal(value, second["quantity"].AsInteger());
        Assert.Equal(second, Target(value));
    }
}
