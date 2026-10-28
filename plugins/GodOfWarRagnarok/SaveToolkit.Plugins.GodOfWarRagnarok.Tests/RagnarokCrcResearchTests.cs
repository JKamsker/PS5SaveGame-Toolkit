using System.Buffers.Binary;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokCrcResearchTests(ITestOutputHelper output)
{
    private static readonly (string Name, uint Polynomial)[] Candidates =
        [("CRC32-IEEE", 0xEDB88320), ("CRC32C", 0x82F63B78)];

    [Theory]
    [InlineData(0xEDB88320u, 0xCBF43926u)]
    [InlineData(0x82F63B78u, 0xE3069283u)]
    public void CombineRangeAndSeedRecoveryAgreeWithIndependentBytewiseCalculation(uint polynomial, uint vector)
    {
        var crc = new RagnarokCrcCandidate(polynomial);
        Assert.Equal(vector, crc.Calculate("123456789"u8));
        var bytes = Enumerable.Range(0, 257).Select(value => (byte)value).ToArray();
        var prefixes = crc.Prefixes(bytes, Enumerable.Range(0, bytes.Length + 1));
        foreach (var split in new[] { 0, 1, 4, 17, 128, 256, 257 })
        {
            var first = crc.Calculate(bytes.AsSpan(0, split));
            var second = crc.Calculate(bytes.AsSpan(split));
            Assert.Equal(crc.Calculate(bytes), crc.Combine(first, second, bytes.Length - split));
            Assert.Equal(second, crc.Range(prefixes, split, bytes.Length));
            Assert.Equal(first, crc.Shift(crc.Shift(first, bytes.Length - split), bytes.Length - split, reverse: true));
            var custom = Independent(bytes.AsSpan(split), polynomial, 0x13579BDF);
            Assert.Equal(0x13579BDFu, crc.RecoverInitial(custom, second, bytes.Length - split));
        }
    }

    [Fact]
    public void AllRecordsTestEmbeddedFieldExclusionsAndConsistentSeedsAcrossGenerations()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles.Where(file => !file.IsSystemFile).ToArray();
        Assert.Equal(156, files.Length);
        var hypotheses = new Dictionary<string, List<uint>>(StringComparer.Ordinal);
        var standardMatches = new List<string>();
        var comparisons = 0;
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var preferences = file.FileName == "userpreferences";
            var ranges = new[] { ("whole", 0, bytes.Length), ("no-footer", 0, bytes.Length - 4),
                ("after-time", 8, bytes.Length - 4), ("after-header", 64, bytes.Length - 4) }.ToList();
            if (!preferences) ranges.Add(("after-metadata", RagnarokFormat.DirectoryMarkerOffset, bytes.Length - 4));
            // Only unresolved outer-header words and the last 32 bytes are hypotheses; no secret values are logged.
            var fields = Enumerable.Range(0, 10).Select(i => ($"header+{24 + i * 4}", 24 + i * 4))
                .Concat(Enumerable.Range(1, 8).Select(i => ($"EOF-{i * 4}", bytes.Length - i * 4))).ToArray();
            foreach (var (algorithm, polynomial) in Candidates)
            {
                var crc = new RagnarokCrcCandidate(polynomial);
                var boundaries = ranges.SelectMany(range => new[] { range.Item2, range.Item3 })
                    .Concat(fields.SelectMany(field => new[] { field.Item2, field.Item2 + 4 }));
                var prefixes = crc.Prefixes(bytes, boundaries);
                foreach (var (coverage, start, end) in ranges)
                foreach (var (field, offset) in fields)
                foreach (var bigEndian in new[] { false, true })
                {
                    var stored = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset))
                        : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                    if (stored is 0 or uint.MaxValue) continue;
                    foreach (var mode in new[] { "unchanged", "zero", "omit" })
                    {
                        var length = end - start;
                        var calculated = crc.Range(prefixes, start, end);
                        if (mode != "unchanged")
                        {
                            if (offset < start || offset + 4 > end) continue;
                            var before = crc.Range(prefixes, start, offset);
                            var after = crc.Range(prefixes, offset + 4, end);
                            if (mode == "zero") before = crc.Combine(before, crc.Calculate(new byte[4]), 4);
                            else length -= 4;
                            calculated = crc.Combine(before, after, end - offset - 4);
                        }
                        foreach (var xorOut in new[] { uint.MaxValue, 0u })
                        {
                            var normalized = stored ^ xorOut ^ uint.MaxValue;
                            var key = $"{preferences}/{algorithm}/{coverage}/{field}/{bigEndian}/{mode}/{xorOut}";
                            var seed = crc.RecoverInitial(normalized, calculated, length);
                            if (!file.Directory.IsBackup)
                            {
                                if (!hypotheses.TryGetValue(key, out var values)) hypotheses[key] = values = [];
                                values.Add(seed);
                            }
                            if (seed is 0 or uint.MaxValue) standardMatches.Add(key);
                            comparisons++;
                        }
                    }
                }
            }
        }
        // Backups are exercised, but only distinct primary generations count as independent seed evidence.
        var consistent = hypotheses.Where(pair => pair.Value.Count >= 3 && pair.Value.Distinct().Count() == 1).Select(pair => pair.Key).ToArray();
        output.WriteLine($"156 records; {comparisons} field/range/encoding/XOR comparisons; {standardMatches.Count} standard-seed leads; {consistent.Length} consistent custom-seed hypotheses.");
        Assert.Empty(standardMatches);
        Assert.Empty(consistent);
    }

    private static uint Independent(ReadOnlySpan<byte> bytes, uint polynomial, uint initial)
    {
        var crc = initial;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : polynomial);
        }
        return ~crc;
    }
}
