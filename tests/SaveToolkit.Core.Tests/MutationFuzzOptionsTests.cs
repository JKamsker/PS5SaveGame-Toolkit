using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class MutationFuzzOptionsTests
{
    [Fact]
    public void AbsentOverrides_PreserveTheExistingDefaultsAndPluginOverrides()
    {
        var defaults = Read();
        Assert.Equal(0x40C0FFEEu, defaults.Seed);
        Assert.Equal(200, defaults.Mutations);
        Assert.Null(defaults.TimeBudget);
        Assert.Equal(new(123u, 17, null), MutationFuzzOptions.Read(123u, 17, _ => null));
    }

    [Theory]
    [InlineData("0x40C0FFEE", 0x40C0FFEEu)]
    [InlineData("1086390254", 0x40C0FFEEu)]
    [InlineData("0XFFFFFFFF", uint.MaxValue)]
    [InlineData("4294967295", uint.MaxValue)]
    [InlineData("0", 0u)]
    public void HexAndDecimalSeeds_AreAccepted(string value, uint expected)
    {
        var options = Read((MutationFuzzOptions.SeedVariable, value));
        Assert.Equal(expected, options.Seed);
        Assert.Equal(200, options.Mutations);
        Assert.Null(options.TimeBudget);
    }

    [Fact]
    public void Overrides_AreIndependentAndApplyTogether()
    {
        var options = Read((MutationFuzzOptions.SeedVariable, "0x1234"),
            (MutationFuzzOptions.MutationsVariable, "5000"), (MutationFuzzOptions.TimeBudgetVariable, "60"));
        Assert.Equal(new(0x1234u, 5000, TimeSpan.FromSeconds(60)), options);
        Assert.Equal(5000, Read((MutationFuzzOptions.MutationsVariable, "5000")).Mutations);
        Assert.Equal(TimeSpan.FromSeconds(60), Read((MutationFuzzOptions.TimeBudgetVariable, "60")).TimeBudget);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("0x")]
    [InlineData("0x100000000")]
    [InlineData("4294967296")]
    [InlineData("garbage")]
    public void InvalidSeeds_NameTheVariableAndRequiredFormat(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => Read((MutationFuzzOptions.SeedVariable, value)));
        Assert.Contains(MutationFuzzOptions.SeedVariable, error.Message);
        Assert.Contains("unsigned 32-bit", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("00:01:00")]
    [InlineData("2147483648")]
    [InlineData("garbage")]
    public void InvalidCountsAndBudgets_NameTheVariableAndEffectiveSeed(string value)
    {
        foreach (var variable in new[] { MutationFuzzOptions.MutationsVariable, MutationFuzzOptions.TimeBudgetVariable })
        {
            var error = Assert.Throws<ArgumentException>(() => Read((variable, value), (MutationFuzzOptions.SeedVariable, "42")));
            Assert.Contains(variable, error.Message);
            Assert.Contains("integer from 1", error.Message);
            Assert.Contains("seed=0x0000002A", error.Message);
        }
    }

    private static MutationFuzzOptions Read(params (string Name, string Value)[] variables)
    {
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);
        return MutationFuzzOptions.Read(SaveMutation.DefaultSeed, 200, name => environment.GetValueOrDefault(name));
    }
}
