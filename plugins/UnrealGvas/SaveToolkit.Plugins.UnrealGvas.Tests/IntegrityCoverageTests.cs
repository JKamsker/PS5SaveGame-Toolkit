using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class IntegrityCoverageTests
{
    private static readonly (string Name, uint Polynomial, bool Reflected)[] Crcs = [
        ("IEEE reflected", 0xEDB88320, true), ("CRC32C reflected", 0x82F63B78, true),
        ("IEEE normal", 0x04C11DB7, false), ("Castagnoli normal", 0x1EDC6F41, false),
    ];

    [Fact]
    public void AlgorithmsMatchPublishedIndependentChecksAndReverseArbitraryStates()
    {
        Assert.Equal(0xCBF43926U, ~new IntegrityCrc(0xEDB88320, true).Compute("123456789"u8));
        Assert.Equal(0xE3069283U, ~new IntegrityCrc(0x82F63B78, true).Compute("123456789"u8));
        Assert.Equal(0x0376E6E7U, new IntegrityCrc(0x04C11DB7, false).Compute("123456789"u8));
        Assert.Equal(0xBB86B11CU, IntegrityAlternatives.Fnv1A("123456789"u8));
        Assert.Equal(0x091E01DEU, IntegrityAlternatives.Adler32("123456789"u8));
        foreach (var definition in Crcs)
        {
            var crc = new IntegrityCrc(definition.Polynomial, definition.Reflected);
            foreach (uint initial in new[] { 0U, uint.MaxValue, 0x12345678U, 0xA596B7C8U })
                foreach (byte[] bytes in new[] { Array.Empty<byte>(), new byte[] { 0 }, Enumerable.Range(0, 256).Select(i => (byte)i).ToArray() })
                    Assert.Equal(initial, crc.RequiredInitial(bytes, crc.Compute(bytes, initial)));
        }
    }

    [Theory]
    [InlineData("PPSA01288", "Checksum")]
    [InlineData("PPSA02199", "Crc32")]
    public void NamedCoverageRejectsStandardAlternativesAndTestsConstantSeedAcrossDistinctCaptures(string title, string field)
    {
        var files = SampleSaves.Load(title).AllDiskFiles.Where(file => title == "PPSA01288"
            ? file.FileName == "General" : file.Directory.Name is "SaveGame00" or "Backup00" && file.FileName == "ue4savegame.dpx.sav").ToArray();
        Assert.True(files.Length > 2, "Archive twins must not be counted as independent checksum evidence.");
        var captures = files.DistinctBy(file => file.Sha256).Select(file => IntegrityCapture.Read(file, field)).ToArray();
        Assert.Equal(2, captures.Length);
        // Sackboy's two different snapshots actually carry the SAME stored value. Do not
        // misreport them as a controlled changing-checksum pair or discard invariant ranges.
        if (title == "PPSA01288") Assert.Equal(captures[0].Stored, captures[1].Stored);
        else Assert.NotEqual(captures[0].Stored, captures[1].Stored);
        var report = new List<object>();
        foreach (var range in captures[0].Ranges.Keys.Intersect(captures[1].Ranges.Keys, StringComparer.Ordinal))
        {
            var inputChanged = !captures[0].Ranges[range].AsSpan().SequenceEqual(captures[1].Ranges[range]);
            foreach (var capture in captures)
            {
                var bytes = capture.Ranges[range];
                var fnv = IntegrityAlternatives.Fnv1A(bytes);
                var adler = IntegrityAlternatives.Adler32(bytes);
                Assert.NotEqual(capture.Stored, fnv);
                Assert.NotEqual(capture.Stored, adler);
                report.Add(new { range, capture.Role, capture.Hash, capture.Stored, length = bytes.Length, fnv, adler });
            }
            foreach (var definition in Crcs)
            {
                var crc = new IntegrityCrc(definition.Polynomial, definition.Reflected);
                foreach (var complement in new[] { false, true })
                    foreach (var byteSwap in new[] { false, true })
                    {
                        uint Decode(uint stored) => complement ? ~Unswap(stored) : Unswap(stored);
                        uint Unswap(uint stored) => byteSwap ? BinaryPrimitives.ReverseEndianness(stored) : stored;
                        var initial = crc.RequiredInitial(captures[0].Ranges[range], Decode(captures[0].Stored));
                        foreach (var capture in captures)
                            foreach (uint standardInitial in new[] { 0U, uint.MaxValue })
                            {
                                var result = crc.Compute(capture.Ranges[range], standardInitial);
                                Assert.NotEqual(Decode(capture.Stored), result);
                                report.Add(new { range, capture.Role, crc = definition.Name, complement, byteSwap, standardInitial, result });
                            }
                        Assert.Equal(Decode(captures[0].Stored), crc.Compute(captures[0].Ranges[range], initial));
                        var matchesSecond = crc.Compute(captures[1].Ranges[range], initial) == Decode(captures[1].Stored);
                        report.Add(new { range, crc = definition.Name, complement, byteSwap, initial, inputChanged, matchesSecond });
                        Assert.False(inputChanged && matchesSecond, $"Potential changing-input constant-seed candidate: {title} {range} {definition.Name}, complement={complement}, swapped={byteSwap}. Investigate independently; never enable writes from this test alone.");
                    }
            }
        }
        // Counts/hash/checksum results only. No original bytes, row contents or account identifiers.
        File.WriteAllText(Path.Combine(Path.GetTempPath(), $"unrealgvas-r2-integrity-{title}.json"), JsonSerializer.Serialize(report));
    }
}
