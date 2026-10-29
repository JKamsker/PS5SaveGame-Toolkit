using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithViewCharacterizationTests
{
    // Golden protocol hashes captured from ddc304b before the maintainability refactors.
    [Theory]
    [InlineData("PPSA10665", false, "3a6a7dcb7f4700e27d6bf422d42f594eb65b2c149fb52fb71ff45cda931ea820")]
    [InlineData("PPSA10665", true, "f7a447a39602f3be38dcfa6b43dd4d4ab13b4c5863ee70a6ee0a6cc74df58b01")]
    [InlineData("PPSA13803", false, "e6e6b63ca387f4a0eb02e44a03d02744190a00d943c28fb3cc16298fee9846a9")]
    public void CompleteViewPreservesPresentationAndWritableSpecs(string title, bool extended, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var bytes = FaithCharacterizationData.Combined(extended);
            using var document = new FinalFantasy16Plugin().Open(FaithTestData.File(bytes, title));
            var json = JsonSerializer.SerializeToUtf8Bytes(document.BuildView());
            Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(json)));
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
