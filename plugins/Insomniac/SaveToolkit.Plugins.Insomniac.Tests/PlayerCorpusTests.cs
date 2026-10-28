using System.Buffers.Binary;
using System.Numerics;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class PlayerCorpusTests
{
    [Theory]
    [InlineData("i29slot0-s-autosave", 0, 977095, 260875, 46376.29296875, 531.1996459960938, 584, 1, 7, 4)]
    [InlineData("i29slot0-s-autosave", 1, 23444, 14858, 7053.69091796875, 40, 20, 0, 0, 0)]
    [InlineData("i29slot0-s-snapshot-108976056", 0, 363925, 226875, 38983.37890625, 306.7997131347656, 304, 1, 7, 3)]
    [InlineData("i29slot0-s-snapshot-1802099279", 0, 4713, 2500, 1804.2432861328125, 0, 0, 0, 0, 0)]
    [InlineData("i29slot1-s-autosave", 0, 1297735, 260875, 46398.02734375, 531.1996459960938, 584, 1, 7, 4)]
    public void KnownPlayerValuesMatchSpecificRecords(string directory, int generation, long bolts, long spent,
        double time, double raritanium, double raritaniumSpent, long difficulty, long goldBolts, long spybots)
    {
        using var document = new InsomniacPlugin().Open(Find(directory, generation));
        var view = Assert.IsType<TabsNode>(document.BuildView());
        Assert.Equal("Player", view.Tabs[0].Title);
        Assert.Equal(bolts, Value("CareerBolts").AsInt64());
        Assert.Equal(spent, Value("SpentBolts").AsInt64());
        Assert.Equal(bolts - spent, Value("AvailableBolts").AsInt64());
        Assert.Equal(time, Value("Playtime").AsDouble());
        Assert.Equal(raritanium, Value("CareerRaritanium").AsDouble());
        Assert.Equal(raritaniumSpent, Value("CareerRaritaniumSpend").AsDouble());
        Assert.Equal(difficulty, Value("Difficulty").AsInt64());
        Assert.Equal(goldBolts, Value("GoldBoltCount").AsInt64());
        Assert.Equal(spybots, Value("SpybotCount").AsInt64());
        FieldValue Value(string name) => document.GetValue($"player/{name}");
    }

    [Fact]
    public void TypesAreReadFromDescriptorsRatherThanAssumingPcSchemaWidths()
    {
        var late = Fields(Find("i29slot0-s-autosave", 0), RiftPlayerView.CampaignTag);
        var early = Fields(Find("i29slot0-s-autosave", 1), RiftPlayerView.CampaignTag);
        Assert.Equal(DdlValueKind.UInt32, late["CareerBolts"].Kind);
        Assert.Equal(DdlValueKind.UInt16, early["CareerBolts"].Kind);
        Assert.Equal(DdlValueKind.Float, late["CareerRaritanium"].Kind);
        Assert.Equal(DdlValueKind.UInt8, early["CareerRaritanium"].Kind);
        Assert.Equal(DdlValueKind.Float, late["Playtime"].Kind);
        Assert.Equal(DdlValueKind.Double, Fields(Find("i29slot0-s-autosave", 0), RiftPlayerView.SpeedrunTag)["SpeedrunTime"].Kind);
        Assert.NotEqual(late["Playtime"].Offset, early["Playtime"].Offset);
        Assert.Equal(DdlValueKind.Null, Fields(Find("i29slot0-s-autosave", 1), RiftPlayerView.GoldBoltTag)["CollectedBoltHashes"].Kind);
    }

    [Fact]
    public void NamedCountersCorroborateAllPrimaryRecordsAndRetainTheAlternateBranch()
    {
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(f => f.FileName == "i29-s.save" && !f.Directory.IsBackup).ToArray();
        Assert.Equal(36, files.Length);
        var records = files.Select(f => (File: f, Fields: Fields(f, RiftPlayerView.CampaignTag))).ToArray();
        var campaigns = records.GroupBy(r => r.Fields["CampaignSaveID"].Values[0].AsUInt64()).ToArray();
        Assert.Equal([17, 19], campaigns.Select(g => g.Count()).Order().ToArray());
        foreach (var campaign in campaigns)
        {
            // The one alternate early branch is documented, not forced into a monotonic sequence.
            var ordered = campaign.Where(r => r.File.Directory.Name != "i29slot0-s-snapshot-1992350878")
                .OrderBy(r => r.Fields["Playtime"].Values[0].AsDouble()).ToArray();
            foreach (var name in new[] { "CareerBolts", "SpentBolts", "CareerExp", "CareerWeaponExp", "CareerRaritanium", "CareerRaritaniumSpend" })
                for (var index = 1; index < ordered.Length; index++)
                    Assert.True(ordered[index].Fields[name].Values[0].AsDouble() >= ordered[index - 1].Fields[name].Values[0].AsDouble(), name);
        }
        var alternate = Fields(Find("i29slot0-s-snapshot-1992350878", 0), RiftPlayerView.CampaignTag);
        Assert.Equal(8218, alternate["CareerBolts"].Values[0].AsInt64());
        Assert.Equal(3839.539306640625, alternate["Playtime"].Values[0].AsDouble());
        Assert.Equal(1, alternate["RYNOPlansCollected"].Values[0].AsInt64());
        foreach (var record in records)
        {
            var gold = Fields(record.File, RiftPlayerView.GoldBoltTag)["CollectedBoltHashes"];
            Assert.Equal(record.Fields["LevelGoldBolts"].Values.Sum(v => v.AsInt64()), gold.Count);
            Assert.Equal(record.Fields["LevelRYNOPlans"].Values.Sum(v => v.AsInt64()),
                BitOperations.PopCount(record.Fields["RYNOPlansCollected"].Values[0].AsUInt64()));
            // The suspicious eight-byte section follows XP, not a section-body digest.
            var container = Dat1Container.Parse(record.File.ReadAllBytes());
            var mirror = container.Sections.Single(s => s.Tag == 0xBC60B495);
            Assert.Equal(record.Fields["CareerExp"].Values[0].AsUInt64(),
                BinaryPrimitives.ReadUInt64LittleEndian(container.SectionBytes(mirror)));
        }
    }

    [Fact]
    public void MeaningfulPlayerEditsAreRejectedAtomicallyWithUnchangedOutput()
    {
        var file = Find("i29slot0-s-autosave", 0);
        using var document = new InsomniacPlugin().Open(file);
        _ = document.BuildView();
        Assert.False(document.CanEdit);
        var previous = document.GetValue("player/CareerBolts");
        var outcome = document.ApplyEdits([
            new("player/CareerBolts", FieldValue.Of(999999L)),
            new("player/InfiniteHealthEnabled", FieldValue.Of(true)),
        ]);
        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.Errors.Count);
        Assert.Empty(outcome.Changes);
        Assert.Equal(previous, document.GetValue("player/CareerBolts"));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CuratedConfidenceAndWarningsDistinguishStoredValuesFromInferences()
    {
        using var document = new InsomniacPlugin().Open(Find("i29slot0-s-autosave", 0));
        var player = Assert.IsType<TabsNode>(document.BuildView()).Tabs[0];
        var fields = player.Children.OfType<SectionNode>().SelectMany(s => s.Children).OfType<FieldNode>()
            .Concat(player.Children.OfType<FieldNode>()).ToDictionary(f => f.ValueId);
        Assert.Equal(Confidence.Verified, fields["player/CareerBolts"].Confidence);
        Assert.Equal(Confidence.Inferred, fields["player/AvailableBolts"].Confidence);
        Assert.Equal(Confidence.Inferred, fields["player/SpybotCount"].Confidence);
        Assert.NotNull(fields["player/CareerRaritanium"].Warning);
        Assert.All(fields.Values, field => Assert.True(field.Spec.ReadOnly));
    }

    internal static ISaveFile Find(string name, int generation) => SampleSaves.Load("PPSA01474").AllDiskFiles.Single(
        f => f.FileName == "i29-s.save" && f.Directory.Name == name && f.Directory.Generation == generation && !f.Directory.IsBackup);

    internal static IReadOnlyDictionary<string, DdlField> Fields(ISaveFile file, uint tag)
    {
        var container = Dat1Container.Parse(file.ReadAllBytes());
        return new DdlReader(container, container.Sections.Single(s => s.Tag == tag)).Read();
    }
}
