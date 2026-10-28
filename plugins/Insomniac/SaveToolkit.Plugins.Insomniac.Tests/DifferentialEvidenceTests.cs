using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DifferentialEvidenceTests
{
    [Fact]
    public void SameCampaignSnapshotsKeepIdentityWhileWeaponStateCandidatesChange()
    {
        var before = PlayerCorpusTests.Find("i29slot0-s-snapshot-108976056", 0);
        var after = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        Assert.Equal(PlayerCorpusTests.Fields(before, RiftPlayerView.CampaignTag)["CampaignSaveID"].Values.Single(),
            PlayerCorpusTests.Fields(after, RiftPlayerView.CampaignTag)["CampaignSaveID"].Values.Single());
        var decoder = new RiftEvidenceDecoder();
        var oldTree = decoder.Decode(before, new DecodeLimits());
        var newTree = decoder.Decode(after, new DecodeLimits());
        Assert.False(oldTree.Truncated);
        Assert.False(newTree.Truncated);
        var diff = SemanticDiffer.Compare(oldTree, newTree, maxChanges: 10000);
        Assert.False(diff.InputTruncated);
        Assert.False(diff.Truncated);
        Assert.Contains(diff.Changes, c => c.Path == "campaign/CareerBolts/0" && c.Before == "363925" && c.After == "977095");
        Assert.Contains(diff.Changes, c => c.Kind == SemanticChangeKind.Changed && c.Path.EndsWith("/xp", StringComparison.Ordinal));
        Assert.Contains(diff.Changes, c => c.Kind == SemanticChangeKind.Changed && c.Path.EndsWith("/loaded-ammo", StringComparison.Ordinal));
        Assert.Contains(diff.Changes, c => c.Kind == SemanticChangeKind.Changed && c.Path.Contains("/mod-grid/", StringComparison.Ordinal));
        Assert.Contains(diff.Changes, c => c.Kind == SemanticChangeKind.Changed && c.Path.Contains("/mod-count/", StringComparison.Ordinal));
        var oldXp = oldTree.Values.Where(v => v.Path.EndsWith("/xp", StringComparison.Ordinal)).Select(v => v.Path).ToHashSet();
        Assert.True(newTree.Values.Count(v => oldXp.Contains(v.Path)) >= 10);
    }

    [Fact]
    public void F1PreservesZipGenerationsAndCollapsesOnlyIdenticalBackupTwins()
    {
        var samples = SampleSaves.Load("PPSA01474");
        var report = SaveSetDiffer.Compare(samples, samples, new SaveDiffOptions
        {
            BeforeGeneration = GenerationSelector.Only(0),
            AfterGeneration = GenerationSelector.Only(1),
            TitleFilter = "PPSA01474",
            FileFilters = ["*i29slot0-s-autosave/i29-s.save"],
            Decoder = new RiftEvidenceDecoder(),
            MaxSemanticChanges = 10000,
        });
        var main = Assert.Single(report.Files, f => !f.IsBackup);
        Assert.Equal(0, main.BeforeGeneration);
        Assert.Equal(1, main.AfterGeneration);
        Assert.Equal(FileDiffStatus.Changed, main.Status);
        Assert.Null(main.Semantic!.Error);
        Assert.False(main.Semantic.InputTruncated);
        Assert.Contains(main.Semantic.Changes, c => c.Path == "campaign/CareerBolts/0" && c.Before == "977095" && c.After == "23444");
        Assert.Equal(FileDiffStatus.MirrorsMain, Assert.Single(report.Files, f => f.IsBackup).Status);
    }
}
