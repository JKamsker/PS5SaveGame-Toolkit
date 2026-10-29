using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ProtocolCompatibilityTests
{
    [Fact]
    public void SampleRootAllCollectionPagesAndEvidenceKeepTheirCompleteProtocol()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(file, context);
        var root = Fingerprint(document.BuildView());
        _ = CyberpunkSampleTests.ExpandAll(document); // Register the lazy per-item part collections too.
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var metadata in queryable.Collections)
        {
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(metadata));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var offset = 0; offset < metadata.TotalCount; offset += 200)
            {
                var page = queryable.Query(metadata.Id, new() { Offset = offset, Limit = 200, IncludeEmpty = true });
                Assert.Equal(metadata.TotalCount, page.FilteredCount);
                Assert.NotEmpty(page.Rows);
                Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key), row.Key));
                hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(page));
            }
            Assert.Equal(metadata.TotalCount, keys.Count);
        }
        var collections = Convert.ToHexString(hash.GetHashAndReset());
        var decoded = EvidenceTests.Decoder().Decode(file, new DecodeLimits());
        Assert.False(decoded.Truncated);
        Assert.Empty(decoded.Warnings);
        var evidence = Fingerprint(decoded.Values);
        var actual = $"{root}|{collections}|{evidence}";
        // Captured against 67a282b/cbb30ea before the remaining reader and row-builder extractions.
        const string expected = "3A4D2C217039FD4631A1F3ED16C6572ADA0C03DC216F5251E99B6E319F9E36F6|" +
            "4AEA16A9B3128BA7FA05C7C3A070A0F4918AA62E5FAC3CB51DC9E395C5DDBF1D|" +
            "3B762727A62052AB9036DF0858E4C668AB3DD476628931BA739FEDD32B911FBF";
        Assert.True(expected == actual, actual);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
