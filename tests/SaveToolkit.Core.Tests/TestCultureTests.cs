using System.Globalization;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class TestCultureTests
{
    [Fact]
    public void NestedScopesRestoreBothAmbientCultures()
    {
        var initialCulture = CultureInfo.CurrentCulture;
        var initialUiCulture = CultureInfo.CurrentUICulture;
        var outerCulture = CultureInfo.GetCultureInfo("th-TH");
        using (new TestCulture(outerCulture))
        {
            using (new TestCulture(CultureInfo.InvariantCulture))
            {
                Assert.Same(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
                Assert.Same(CultureInfo.InvariantCulture, CultureInfo.CurrentUICulture);
            }
            Assert.Same(outerCulture, CultureInfo.CurrentCulture);
            Assert.Same(outerCulture, CultureInfo.CurrentUICulture);
        }
        Assert.Same(initialCulture, CultureInfo.CurrentCulture);
        Assert.Same(initialUiCulture, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void ScopeRestoresBothCulturesWhenTheBodyThrows()
    {
        var initialCulture = CultureInfo.CurrentCulture;
        var initialUiCulture = CultureInfo.CurrentUICulture;
        var expected = new InvalidOperationException("Test body failed.");
        Action body = () =>
        {
            using var culture = new TestCulture(CultureInfo.GetCultureInfo("th-TH"));
            throw expected;
        };
        var actual = Assert.Throws<InvalidOperationException>(body);
        Assert.Same(expected, actual);
        Assert.Same(initialCulture, CultureInfo.CurrentCulture);
        Assert.Same(initialUiCulture, CultureInfo.CurrentUICulture);
    }
}
