using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class EvidenceTests
{
    [Fact]
    public void F1CorpusBaselineHasOneGenerationAndOneIdenticalGameplayBackup()
    {
        var sample = SampleSaves.Load("PPSA04027");
        using var stream = typeof(EvidenceTests).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.Cyberpunk2077.Tests.Evidence.corpus.json");
        Assert.NotNull(stream);
        using var json = JsonDocument.Parse(stream);
        var corpus = json.RootElement;
        var archive = SampleSaves.FindArchive("PPSA04027")!;
        using var archiveStream = File.OpenRead(archive);
        Assert.Equal(corpus.GetProperty("archiveSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(archiveStream)));
        var report = SaveSetDiffer.Compare(sample, sample, new SaveDiffOptions { FileFilters = ["sav.dat"], Decoder = new CyberpunkEvidenceDecoder() });
        Assert.False(report.HasChanges);
        Assert.Equal(corpus.GetProperty("generations").GetInt32(), report.Before.Generations);
        Assert.Equal(corpus.GetProperty("identicalBackupTwins").GetInt32(), report.Before.Twins.Identical);
        Assert.Empty(report.Before.Twins.Mismatches);
        Assert.All(report.Files, file => Assert.Equal(corpus.GetProperty("gameplaySha256").GetString(), file.BeforeSha256));
        var decoded = new CyberpunkEvidenceDecoder().Decode(ProgressionSampleTests.Files()[0], new DecodeLimits());
        Assert.False(decoded.Truncated);
        Assert.Empty(decoded.Warnings);
        Assert.Equal(2750, decoded.Values.Count(value => value.Path.StartsWith("facts[", StringComparison.Ordinal)));
    }

    [Fact]
    public void F1DecoderLocatesSyntheticQuantityChangeByFullIdentityAndDetectsOpaqueBody()
    {
        var file = ProgressionSampleTests.Files()[0];
        var original = CsavContainer.Parse(file.ReadAllBytes());
        var tree = CsavTree.Parse(original);
        var item = InventoryReader.Read(original, tree).Single(item => item.NodeId == 49);
        var payload = original.Payload.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(item.QuantityOffset!.Value), 601);
        var edited = MemorySaveSet.SingleFile("sav.dat", original.Rebuild(payload), "PPSA04027");
        var decoder = new CyberpunkEvidenceDecoder();
        var comparison = SemanticDiffer.Compare(decoder.Decode(file, new DecodeLimits()), decoder.Decode(edited, new DecodeLimits()));
        Assert.Equal(2, comparison.Total);
        var quantity = Assert.Single(comparison.Changes, change => change.Path.EndsWith("/quantity", StringComparison.Ordinal));
        Assert.Contains("container=1;identity=", quantity.Path);
        Assert.Equal("600", quantity.Before);
        Assert.Equal("601", quantity.After);
        Assert.Contains(comparison.Changes, change => change.Path.EndsWith("/bodySha256", StringComparison.Ordinal));
        Assert.True(decoder.Decode(file, new DecodeLimits { MaxValues = 4 }).Truncated);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new DecodeLimits { MaxFileBytes = 1 }));
    }
}
