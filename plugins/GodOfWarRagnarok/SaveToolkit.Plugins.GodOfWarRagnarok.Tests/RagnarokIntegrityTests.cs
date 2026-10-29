using System.Buffers.Binary;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokIntegrityTests
{
    private const uint CheckVectorCrc = 0xCBF43926;
    private const int VectorOffset = 8;

    [Theory]
    [InlineData("313233343536373839", CheckVectorCrc, 1)]
    [InlineData("00", 0xD202EF8Du, 1)]
    [InlineData("FF", 0xFF000000u, 1)]
    [InlineData("FFFFFFFF", uint.MaxValue, 2)]
    public void CrcProbesMatchCurrentVectors(string hex, uint expected, int matches)
    {
        AssertCrcVector(Convert.FromHexString(hex), expected, matches);
    }

    [Fact]
    public void CrcProbeCoversEveryByteValue()
    {
        var bytes = Enumerable.Range(byte.MinValue, byte.MaxValue + 1).Select(value => (byte)value).ToArray();
        AssertCrcVector(bytes, 0x29058C73);
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(1, false, 0)]
    [InlineData(24, false, 1)]
    [InlineData(28, false, 0)]
    [InlineData(0, true, 0)]
    public void CrcTargetsStayAlignedLittleEndianAndExcludeTheFinalWord(int target, bool bigEndian, int matches)
    {
        var bytes = new byte[32];
        "123456789"u8.CopyTo(bytes.AsSpan(VectorOffset));
        if (bigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(target), CheckVectorCrc);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(target), CheckVectorCrc);

        var evidence = RagnarokIntegrity.Probe(bytes, Grammar(new RagnarokSpan(RagnarokSpanKind.Header, VectorOffset, 9, 1, "Observed")));

        Assert.Equal(new RagnarokChecksumProbe("CRC32-IEEE", "Header@0x8", 9, 7, matches), evidence.Probes[2]);
        Assert.Equal(matches > 0, evidence.HasCandidateMatch);
    }

    [Fact]
    public void CoverageKeepsInputOrderAndExcludesOpaqueEmptyAndSentinelSpans()
    {
        var bytes = new byte[32];
        "123456789"u8.CopyTo(bytes.AsSpan(VectorOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, CheckVectorCrc);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(sizeof(uint)), CheckVectorCrc);
        var evidence = RagnarokIntegrity.Probe(bytes, Grammar(
            new(RagnarokSpanKind.WalletRows, VectorOffset, 9, 1, "Inferred"),
            new(RagnarokSpanKind.Opaque, 20, 4, 1, "Unknown"),
            new(RagnarokSpanKind.DirectoryIds, 24, 0, 0, "Observed"),
            new(RagnarokSpanKind.TrailingSentinel, 28, 4, 1, "Observed sentinel; not a checksum"),
            new(RagnarokSpanKind.Header, VectorOffset, 9, 1, "Observed")));

        Assert.Equal(new[]
        {
            ("CRC32-IEEE", "whole-file"), ("Adler32", "whole-file"),
            ("CRC32-IEEE", "WalletRows@0x8"), ("Adler32", "WalletRows@0x8"),
            ("CRC32-IEEE", "Header@0x8"), ("Adler32", "Header@0x8"),
        }, evidence.Probes.Select(probe => (probe.Algorithm, probe.Coverage)));
        Assert.Equal(new RagnarokChecksumProbe("CRC32-IEEE", "WalletRows@0x8", 9, 7, 2), evidence.Probes[2]);
        Assert.Equal(new RagnarokChecksumProbe("Adler32", "WalletRows@0x8", 9, 7, 0), evidence.Probes[3]);
        Assert.True(evidence.HasCandidateMatch);
    }

    [Fact]
    public void EmptyInputRetainsTheWholeFileProbes()
    {
        var evidence = RagnarokIntegrity.Probe([], Grammar());
        Assert.Equal(new[]
        {
            new RagnarokChecksumProbe("CRC32-IEEE", "whole-file", 0, -1, 0),
            new RagnarokChecksumProbe("Adler32", "whole-file", 0, -1, 0),
        }, evidence.Probes);
        Assert.False(evidence.HasCandidateMatch);
    }

    private static void AssertCrcVector(byte[] vector, uint expected, int matches = 1)
    {
        var bytes = new byte[sizeof(uint) + vector.Length + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, expected);
        vector.CopyTo(bytes, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - sizeof(uint)), uint.MaxValue);
        var evidence = RagnarokIntegrity.Probe(bytes, Grammar(
            new RagnarokSpan(RagnarokSpanKind.Header, sizeof(uint), vector.Length, 1, "Observed")));

        Assert.Equal(new RagnarokChecksumProbe("CRC32-IEEE", "Header@0x4", vector.Length,
            (bytes.Length - sizeof(uint)) / sizeof(uint), matches), evidence.Probes[2]);
        Assert.True(evidence.HasCandidateMatch);
    }

    private static RagnarokGrammarEvidence Grammar(params RagnarokSpan[] spans) => new(spans, [], [], []);

    [Fact]
    public void FiniteChecksumCoverageHasNoCrcOrAdlerCandidateMatch()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles
            .First(candidate => !candidate.IsSystemFile && !candidate.Directory.IsBackup && candidate.FileName == "GOWRSAVE0");
        var bytes = file.ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        Assert.Equal(798, Assert.Single(index.Evidence.Spans, span => span.Kind == RagnarokSpanKind.DirectoryIds).Count);
        Assert.Equal(211, Assert.Single(index.Evidence.Spans, span => span.Kind == RagnarokSpanKind.WalletRows &&
            span.Offset == index.WalletOffset + RagnarokFormat.WalletHeaderSize).Count);
        Assert.Equal(1_019, index.Evidence.HashCandidates.Count);
        Assert.Contains(index.Evidence.Spans, span => span.Kind == RagnarokSpanKind.Opaque);
        Assert.Empty(index.Evidence.CompressionCandidates);
        var evidence = RagnarokIntegrity.Probe(bytes, index.Evidence);

        Assert.True(evidence.Probes.Count >= 20);
        Assert.All(evidence.Probes, probe => Assert.Equal(0, probe.Matches));
        Assert.False(evidence.HasCandidateMatch);
        Assert.All(evidence.Probes, probe => Assert.Equal((bytes.Length - sizeof(uint)) / sizeof(uint), probe.ComparedWords));
    }
}
