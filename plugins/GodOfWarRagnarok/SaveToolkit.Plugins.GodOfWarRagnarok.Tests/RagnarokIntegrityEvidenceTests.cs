using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokIntegrityEvidenceTests
{
    private const int CandidateOffset = 0x480;
    private const int OpaqueMutationOffset = 0x600;
    // Python zlib.crc32 over this fixture's 64-byte outer header, independently calculated.
    private const uint FixtureHeaderCrc = 0x29136B21;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedCrcLeadDoesNotAuthenticateOpaqueBytesOrPromoteWrites(bool mutateOpaqueBytes)
    {
        var bytes = RagnarokTestSupport.LegacyGameplay();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(CandidateOffset), FixtureHeaderCrc);
        if (mutateOpaqueBytes) bytes[OpaqueMutationOffset] ^= 1;
        var index = RagnarokRecordIndex.Parse(bytes, false);
        Assert.Contains(index.Evidence.Spans, span => span.Kind == RagnarokSpanKind.Opaque &&
            span.Offset <= OpaqueMutationOffset && OpaqueMutationOffset < span.Offset + span.Length);
        var evidence = RagnarokIntegrity.Probe(bytes, index.Evidence);
        Assert.Equal(1, Assert.Single(evidence.Probes, probe =>
            probe.Algorithm == "CRC32-IEEE" && probe.Coverage == "Header@0x0").Matches);
        Assert.True(evidence.HasCandidateMatch); // Deliberately constructed lead, not authentication.

        var plugin = new GodOfWarRagnarokPlugin();
        Assert.Equal(PluginMaturity.Experimental, plugin.Info.Maturity);
        using var document = plugin.Open(MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330"));
        Assert.Contains(RagnarokTestSupport.EagerNodes(document.BuildView()).OfType<TextNode>(), node =>
            node.Tone == Tone.Warning && node.Text.Contains(RagnarokWrites.IntegrityWarning, StringComparison.Ordinal));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("wallet/0")));
        var quantity = Assert.IsType<FieldNode>(Assert.Single(table.Rows).Cells["quantity"]);
        Assert.Equal(Confidence.Inferred, quantity.Confidence);
        Assert.Equal(RagnarokWrites.IntegrityWarning, quantity.Warning);
        var result = document.ApplyEdits([new(quantity.ValueId, FieldValue.Of(0L))]);
        Assert.True(result.Succeeded);
        Assert.Contains(RagnarokWrites.IntegrityWarning, result.Warnings);
        Assert.Contains(RagnarokWrites.EvidenceWarning, result.Warnings);
        var expected = bytes.ToArray();
        // Only the literal fixture counter changes; the candidate word is never treated as repairable integrity.
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(0x51C), 0);
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
