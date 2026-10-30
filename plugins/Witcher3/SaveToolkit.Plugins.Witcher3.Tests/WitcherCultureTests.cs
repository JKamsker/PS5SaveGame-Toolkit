using System.Globalization;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherCultureTests
{
    [Theory]
    [InlineData("en-US", false)]
    [InlineData("ar-SA", false)]
    [InlineData("th-TH", true)]
    [InlineData("tr-TR", true)]
    public void RootPagingUsesInvariantSentinelAndParsesInvariantSigns(string cultureName, bool overrideSigns)
    {
        const int rootCount = 201;
        const int rootBytes = 4;
        const int pageSize = 128;
        var variables = new byte[rootCount * rootBytes];
        var entries = new Sav3Entry[rootCount];
        for (var i = 0; i < rootCount; i++)
        {
            "BS\x01\0"u8.CopyTo(variables.AsSpan(i * rootBytes));
            entries[i] = new(RobustnessSave.VariablesOffset + i * rootBytes, rootBytes);
        }
        var bytes = SyntheticSave.Wrap(RobustnessSave.Payload(variables, entries));
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(cultureName).Clone();
        if (overrideSigns)
        {
            // Windows regional overrides can change signs even for th-TH and tr-TR.
            culture.NumberFormat.NegativeSign = "~";
            culture.NumberFormat.PositiveSign = "!";
        }
        using var scope = new TestCulture(culture);
        using var document = WitcherTestFiles.Open(bytes);
        var page = document.ExpandNode("children/-1/0");
        Assert.Equal($"children/-1/{pageSize}", Assert.IsType<SectionNode>(page[^1]).LazyId);
        Assert.Equal(page, document.ExpandNode("children/-1/+0"));
        Assert.NotEmpty(document.ExpandNode($"children/-1/{pageSize}"));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
