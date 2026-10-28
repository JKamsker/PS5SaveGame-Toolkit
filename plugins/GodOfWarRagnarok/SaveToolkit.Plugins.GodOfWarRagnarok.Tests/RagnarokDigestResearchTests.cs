using System.Security.Cryptography;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>Offline finite embedded-digest hypotheses; only counts and hypothesis names leave this test.</summary>
public sealed class RagnarokDigestResearchTests(ITestOutputHelper output)
{
    private static readonly (string Name, HashAlgorithmName Algorithm, int Width)[] Algorithms =
        [("MD5", HashAlgorithmName.MD5, 16), ("SHA-1", HashAlgorithmName.SHA1, 20),
            ("SHA-256", HashAlgorithmName.SHA256, 32), ("SHA-512", HashAlgorithmName.SHA512, 64)];

    [Fact]
    public void AllRecordsTestZeroedOmittedAndTruncatedHeaderOrFooterDigests()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles.Where(file => !file.IsSystemFile).ToArray();
        Assert.Equal(156, files.Length);
        var cache = new Dictionary<string, (int Comparisons, IReadOnlyList<string> Matches)>(StringComparer.Ordinal);
        var comparisons = 0;
        var matches = new List<string>();
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            // Identical backup twins repeat exactly the same hypotheses; retain all 156 record assertions.
            var key = Convert.ToHexString(SHA256.HashData(bytes));
            if (!cache.TryGetValue(key, out var result))
            {
                result = Probe(bytes);
                cache.Add(key, result);
            }
            comparisons += result.Comparisons;
            matches.AddRange(result.Matches);
        }
        output.WriteLine($"156 records, {cache.Count} distinct byte sequences; {comparisons} embedded digest comparisons; {matches.Count} matches.");
        Assert.Equal(78, cache.Count);
        Assert.Empty(matches);
    }

    [Fact]
    public void EmbeddedFieldProbeFindsSyntheticZeroedOmittedAndTruncatedDigests()
    {
        foreach (var mode in new[] { "zero", "omit" })
        foreach (var width in new[] { 4, 8, 32 })
        {
            var bytes = Enumerable.Range(0, 512).Select(value => (byte)(value * 7)).ToArray();
            var digest = Digest(HashAlgorithmName.SHA256, bytes, 0, bytes.Length - 4, 32, width, mode);
            digest.AsSpan(0, width).CopyTo(bytes.AsSpan(32));
            var result = Probe(bytes);
            Assert.Contains(result.Matches, match => match == $"SHA-256/no-footer/header+32/{width}/{mode}/prefix");
        }
    }

    private static (int Comparisons, IReadOnlyList<string> Matches) Probe(byte[] bytes)
    {
        var matches = new List<string>();
        var comparisons = 0;
        var ranges = new[] { ("whole", 0, bytes.Length), ("no-footer", 0, bytes.Length - 4), ("after-time", 8, bytes.Length - 4) };
        foreach (var (name, algorithm, digestWidth) in Algorithms)
        foreach (var width in new[] { 4, 8, digestWidth }.Distinct())
        {
            var targets = new[] { ("header+24", 24), ("header+28", 28), ("header+32", 32),
                ($"EOF-{width + 4}", bytes.Length - width - 4) };
            foreach (var (coverage, start, end) in ranges)
            foreach (var (target, offset) in targets)
            foreach (var mode in new[] { "unchanged", "zero", "omit" })
            {
                if (offset < start || offset + width > end) continue;
                var stored = bytes.AsSpan(offset, width);
                // Uniform sentinel/padding fields are not plausible digest evidence.
                if (stored.IndexOfAnyExcept((byte)0) < 0 || stored.IndexOfAnyExcept(byte.MaxValue) < 0) continue;
                var digest = Digest(algorithm, bytes, start, end, offset, width, mode);
                var prefix = digest.AsSpan(0, width);
                var suffix = digest.AsSpan(digest.Length - width, width);
                if (stored.SequenceEqual(prefix)) matches.Add($"{name}/{coverage}/{target}/{width}/{mode}/prefix");
                if (stored.SequenceEqual(suffix)) matches.Add($"{name}/{coverage}/{target}/{width}/{mode}/suffix");
                Array.Reverse(digest);
                if (stored.SequenceEqual(digest.AsSpan(0, width))) matches.Add($"{name}/{coverage}/{target}/{width}/{mode}/reversed-prefix");
                if (stored.SequenceEqual(digest.AsSpan(digest.Length - width, width))) matches.Add($"{name}/{coverage}/{target}/{width}/{mode}/reversed-suffix");
                comparisons += 4;
            }
        }
        return (comparisons, matches);
    }

    private static byte[] Digest(HashAlgorithmName algorithm, ReadOnlySpan<byte> bytes, int start, int end, int offset, int width, string mode)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        if (mode == "unchanged") hash.AppendData(bytes[start..end]);
        else
        {
            hash.AppendData(bytes[start..offset]);
            if (mode == "zero") hash.AppendData(new byte[width]);
            hash.AppendData(bytes[(offset + width)..end]);
        }
        return hash.GetHashAndReset();
    }
}
