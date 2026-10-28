using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokAuthenticationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("CRC32C", "123456789", "E3069283")]
    [InlineData("MD5", "abc", "900150983CD24FB0D6963F7D28E17F72")]
    [InlineData("SHA-1", "abc", "A9993E364706816ABA3E25717850C26C9CD0D89D")]
    [InlineData("SHA-256", "abc", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("SHA-512", "abc", "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F")]
    public void DigestCandidatesMatchPublishedVectors(string algorithm, string input, string expected)
    {
        Assert.Equal(expected, Convert.ToHexString(RagnarokAuthentication.Calculate(algorithm, System.Text.Encoding.ASCII.GetBytes(input))));
    }

    [Fact]
    public void DigestSearchIncludesUnalignedTargetsAndLastPossibleOffset()
    {
        var digest = RagnarokAuthentication.Calculate("SHA-256", "abc"u8);
        var bytes = new byte[3 + digest.Length];
        digest.CopyTo(bytes, 3);
        Assert.Equal(3, Assert.Single(RagnarokAuthentication.Find(bytes, digest)));
        bytes[^1] ^= 1;
        Assert.Empty(RagnarokAuthentication.Find(bytes, digest));
    }

    [Fact]
    public void AllPrimaryGenerationsHaveFiniteReproducibleCoverageWithoutAuthenticationClaims()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles
            .Where(file => !file.Directory.IsBackup && file.FileName.StartsWith("GOWRSAVE", StringComparison.Ordinal)).ToArray();
        Assert.Equal(76, files.Length);
        var computations = 0;
        var matches = new List<string>();
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var index = RagnarokRecordIndex.Parse(bytes, false);
            var probes = RagnarokAuthentication.Probe(bytes, index);
            Assert.InRange(probes.Count, 60, 66); // Empty hero rows are omitted, never hashed as meaningful coverage.
            foreach (var probe in probes)
            {
                Assert.True(probe.Length > 0);
                Assert.True(probe.Offset >= 0 && probe.Offset <= bytes.Length - probe.Length);
                computations++;
                foreach (var offset in probe.MatchingOffsets)
                {
                    matches.Add($"{file.RelativePath}: {probe.Algorithm}/{probe.Coverage} at 0x{offset:X}");
                }
            }
        }

        output.WriteLine($"{files.Length} primary generations, {computations} finite digest probes, {matches.Count} matches.");
        Assert.Equal(4_956, computations);
        Assert.Empty(matches);
    }
}
