using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokIntegrityTests
{
    [Fact]
    public void FiniteChecksumCoverageHasNoCrcOrAdlerCandidateMatch()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles
            .First(candidate => !candidate.IsSystemFile && !candidate.Directory.IsBackup && candidate.FileName == "GOWRSAVE0");
        var bytes = file.ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        Assert.Equal(798, Assert.Single(index.Evidence.Spans, span => span.Kind == RagnarokSpanKind.DirectoryIds).Count);
        Assert.Equal(211, Assert.Single(index.Evidence.Spans, span => span.Kind == RagnarokSpanKind.WalletRows).Count);
        Assert.Equal(1_019, index.Evidence.HashCandidates.Count);
        Assert.Contains(index.Evidence.Spans, span => span.Kind == RagnarokSpanKind.Opaque);
        Assert.Empty(index.Evidence.CompressionCandidates);
        var evidence = RagnarokIntegrity.Probe(bytes, index.Evidence);

        Assert.Equal(20, evidence.Probes.Count);
        Assert.All(evidence.Probes, probe => Assert.Equal(0, probe.Matches));
        Assert.False(evidence.HasCandidateMatch);
        Assert.All(evidence.Probes, probe => Assert.Equal(bytes.Length / sizeof(uint), probe.ComparedWords));
    }
}
