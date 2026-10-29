using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class EvidenceTests
{
    [Fact]
    public void BaselineCorpus_VerifiesWithoutPretendingToHaveIndependentGenerations()
    {
        var set = SampleSaves.Load("PPSA03169");
        var manifest = FixtureManifest.Load(Path.Combine(AppContext.BaseDirectory, "Evidence", "fixtures.json"));
        var fixture = Assert.Single(manifest.Fixtures);
        var file = set.AllDiskFiles.First(file => file.FileName == "memory.dat");
        var options = FixtureVerifier.OptionsFor(fixture, manifest, new HookSaveDecoder(new EldenRingEvidenceDecoder())) with
        {
            Churn = new ChurnFilter(EvidencePrivacy.Masks(file)),
            Bytes = new ByteDiffOptions { ContextBytes = 0, MaxPreviewBytes = 1 },
        };
        var result = FixtureVerifier.Verify(fixture, set, set, options);
        Assert.True(result.Passed, string.Join("; ", result.Expectations.Where(expectation => !expectation.Passed).Select(expectation => expectation.Message)));
        Assert.False(result.Report.HasChanges);
        Assert.Equal(1, result.Report.Before.Generations);
        Assert.Equal(1, result.Report.Before.Twins.Identical);
        Assert.False(fixture.Control);
    }

    [Fact]
    public void SemanticDiff_AlignsMovedPhysicalRowsByHandleAndAcquisitionIdentity()
    {
        var before = new SemanticFixture();
        before.Row("held", "common", 0, 0xB00050F0, 12, 77);
        var after = new SemanticFixture();
        after.Row("held", "common", 9, 0xB00050F0, 13, 77);
        var decoder = new HookSaveDecoder(new EldenRingEvidenceDecoder());
        var difference = SemanticDiffer.Compare(decoder.Decode(before.Save.File(), new()), decoder.Decode(after.Save.File(), new()));
        var change = Assert.Single(difference.Changes);
        Assert.EndsWith("/quantity", change.Path);
        Assert.Equal("12", change.Before);
        Assert.Equal("13", change.After);
        Assert.Equal(0, difference.Added);
        Assert.Equal(0, difference.Removed);
        var bounded = decoder.Decode(before.Save.File(), new() { MaxValues = 2 });
        Assert.True(bounded.Truncated);
        Assert.Equal(2, bounded.Values.Count);
    }

    [Fact]
    public void EquipmentEvidenceAllowsOnlyApprovedNumericBytesAtBothEndpoints()
    {
        var before = new SemanticFixture();
        var after = new SemanticFixture();
        var slot = EldenRingParser.Parse(after.Save.Bytes).Slots[0];
        var range = SemanticReader.Read(after.Save.Bytes, slot).Ranges.Single(range => range.Name == "EquipData");
        after.Save.Write32(range.Offset, 1);
        after.Save.Write32(slot.ProfileOffset + 0x17E, 1);
        after.Save.Bytes[after.Player + 0x110] = 1; // Synthetic excluded value, never a real credential.
        var beforeFile = before.Save.File();
        var afterFile = after.Save.File();
        var report = SaveSetDiffer.Compare(beforeFile.Directory.SaveSet, afterFile.Directory.SaveSet, new()
        {
            Churn = new ChurnFilter([.. EvidencePrivacy.Masks(beforeFile), .. EvidencePrivacy.Masks(afterFile)]),
            Bytes = new ByteDiffOptions { ContextBytes = 0, MaxPreviewBytes = 1 },
            Decoder = new HookSaveDecoder(new EldenRingEvidenceDecoder()),
        });
        var file = Assert.Single(report.Files);
        Assert.Equal(FileDiffStatus.Changed, file.Status);
        Assert.Equal(1, file.Bytes!.IgnoredBytes);
        Assert.Equal(new long[] { range.Offset, slot.ProfileOffset + 0x17E }, file.Bytes.Ranges.Select(change => change.Offset));
        Assert.Equal(2, file.Semantic!.Total);
    }

    [Fact]
    public void NonAllowlistedBytes_AreMaskedAndNeverAppearInDiffPreviews()
    {
        var before = new SemanticFixture();
        var after = new SemanticFixture();
        after.Save.Bytes[after.Player + 0x110] = 1; // synthetic nonsecret mutation inside the excluded password region.
        var beforeFile = before.Save.File();
        var afterFile = after.Save.File();
        var report = SaveSetDiffer.Compare(beforeFile.Directory.SaveSet, afterFile.Directory.SaveSet, new()
        {
            Churn = new ChurnFilter([.. EvidencePrivacy.Masks(beforeFile), .. EvidencePrivacy.Masks(afterFile)]),
            Bytes = new ByteDiffOptions { ContextBytes = 0, MaxPreviewBytes = 1 },
            Decoder = new HookSaveDecoder(new EldenRingEvidenceDecoder()),
        });
        var file = Assert.Single(report.Files);
        Assert.Equal(FileDiffStatus.ChurnOnly, file.Status);
        Assert.Empty(file.Bytes!.Ranges);
        Assert.Equal(1, file.Bytes.IgnoredBytes);
        Assert.Equal(0, file.Semantic!.Total);
    }
}
