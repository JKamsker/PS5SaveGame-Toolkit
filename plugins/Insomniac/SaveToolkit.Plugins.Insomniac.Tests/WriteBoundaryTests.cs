using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

/// <summary>Private, in-memory research only. Parser acceptance cannot establish
/// game integrity or PS5 acceptance. Production scalar edits retain this warning.</summary>
public sealed class WriteBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WidthPreservingBoltProbeChangesOneSemanticAtomWithoutProvingGameAcceptance(int generation)
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", generation);
        var original = file.ReadAllBytes();
        var bytes = original.ToArray();
        var fields = PlayerCorpusTests.Fields(file, RiftPlayerView.CampaignTag);
        var bolts = fields["CareerBolts"];
        var width = bolts.Kind switch { DdlValueKind.UInt16 => 2, DdlValueKind.UInt32 => 4, _ => throw new InvalidOperationException() };
        var value = checked(bolts.Values.Single().AsUInt32() + 1);
        if (width == 2) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bolts.Offset), checked((ushort)value));
        else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bolts.Offset), value);
        CheckDiff(file, original, bytes, bolts.Offset, width, "campaign/CareerBolts/0");
    }

    [Fact]
    public void ExistingUnlockedOptionProbeChangesOnlyItsBooleanAtom()
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var original = file.ReadAllBytes();
        var bytes = original.ToArray();
        var option = PlayerCorpusTests.Fields(file, RiftPlayerView.GoldBoltTag)["RPGEnabled"];
        Assert.Equal(DdlValueKind.Bool, option.Kind);
        Assert.Equal(7, PlayerCorpusTests.Fields(file, RiftPlayerView.GoldBoltTag)["CollectedBoltHashes"].Count);
        bytes[option.Offset] ^= 1;
        CheckDiff(file, original, bytes, option.Offset, 1, "gold-options/RPGEnabled");
    }

    [Fact]
    public void CareerXpProbeExposesUnrepairedMirrorEvenWhenParserAcceptsIt()
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var bytes = file.ReadAllBytes();
        var xp = PlayerCorpusTests.Fields(file, RiftPlayerView.CampaignTag)["CareerExp"];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(xp.Offset), checked(xp.Values.Single().AsUInt32() + 1));
        var parsed = Dat1Container.Parse(bytes);
        var mirror = parsed.Sections.Single(s => s.Tag == 0xBC60B495);
        var stored = new DdlSections(parsed).Roots[RiftPlayerView.CampaignTag]["CareerExp"].Values.Single().AsUInt64();
        Assert.NotEqual(stored, BinaryPrimitives.ReadUInt64LittleEndian(parsed.SectionBytes(mirror)));
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", bytes));
        Assert.False(document.CanEdit);
        // Opening arbitrary bytes is deliberately not an integrity oracle.
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void CheckDiff(SaveToolkit.Sdk.Saves.ISaveFile file, byte[] original, byte[] patched,
        int offset, int width, string expectedPath)
    {
        var changes = Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]).ToArray();
        Assert.NotEmpty(changes);
        Assert.All(changes, index => Assert.InRange(index, offset, offset + width - 1));
        Assert.Equal(original.Length, patched.Length);
        var candidate = MemorySaveSet.SingleFile("i29-s.save", patched);
        var decoder = new RiftEvidenceDecoder();
        var diff = SemanticDiffer.Compare(decoder.Decode(file, new()), decoder.Decode(candidate, new()), maxChanges: 10000);
        Assert.False(diff.InputTruncated);
        var atom = Assert.Single(diff.Changes);
        Assert.Equal(expectedPath, atom.Path);
        Assert.Equal(SemanticChangeKind.Changed, atom.Kind);
        using var document = new InsomniacPlugin().Open(candidate);
        Assert.False(document.CanEdit);
        Assert.Equal(patched, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(PluginMaturity.Experimental, new InsomniacPlugin().Info.Maturity);
    }
}
