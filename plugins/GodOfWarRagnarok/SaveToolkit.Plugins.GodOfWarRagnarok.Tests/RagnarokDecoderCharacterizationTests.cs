using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokDecoderCharacterizationTests
{
    [Theory]
    // Captured from the original test-only decoder before separating evidence categories.
    [InlineData(false, 5, "60EFDE71F1D0FF87EFE0A5F625EB0F17DFC1FBE42DF2470D59B72DAC9BC18AB3")]
    [InlineData(false, 200_000, "3180AAED625EE631EB0C19D4A5D6B14B312FCC1BA83637ED5F90F531CEC7EDED")]
    [InlineData(true, 5, "60EFDE71F1D0FF87EFE0A5F625EB0F17DFC1FBE42DF2470D59B72DAC9BC18AB3")]
    [InlineData(true, 200_000, "9BC6E946D814601624F825ABA270FDBD44370EE4DE6A740F4F01DF1B11EB1B08")]
    public void SemanticEvidenceRetainsItsCompleteOrderedOutputAndTruncation(bool nested, int limit, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var bytes = nested ? new RagnarokNestedFixture().Create() : RagnarokTestSupport.LegacyGameplay();
            var tree = new RagnarokDiffDecoder().Decode(RagnarokTestSupport.GameplayFile(bytes), new DecodeLimits
            { MaxValues = limit, MaxValueLength = 48 });
            Assert.Equal(limit == 5, tree.Truncated);
            var json = JsonSerializer.Serialize(tree);
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            Assert.True(expected == actual, actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
