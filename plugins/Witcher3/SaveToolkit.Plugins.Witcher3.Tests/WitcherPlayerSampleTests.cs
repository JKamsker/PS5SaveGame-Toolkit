using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherPlayerSampleTests
{
    [Fact]
    public void AllSnapshotsAndBackupTwinsMatchKnownPlayerDataAndSidecars()
    {
        var expected = new Dictionary<string, (int Xp, int Items, int Bread)>(StringComparer.Ordinal) {
            ["CheckPt.53db8.7e73c800.313c5b3.sav"] = (0, 24, 5),
            ["CheckPt.8559a.7e73c800.317a3e7.sav"] = (10, 25, 4),
            ["CheckPt.8559a.7e73c800.31c45eb.sav"] = (260, 30, 4),
            ["Manual.8559a.7e73c800.31c94f5.sav"] = (260, 30, 4),
        };
        var set = SampleSaves.Load("PPSA04018");
        var saves = EnabledWriteFixture.SelectSaves(set.AllDiskFiles);
        Assert.Equal(8, saves.Length);
        foreach (var file in saves)
        {
            var container = WitcherContainer.Parse(file.ReadAllBytes());
            var player = PlayerSaveFixture.Read(container).Player;
            var known = expected[file.FileName];
            Assert.Equal(1, player.Progress.Level);
            Assert.Equal(50, player.Progress.MaximumDefinedLevel);
            Assert.Equal(known.Xp, player.Progress.Experience);
            Assert.Equal(0, player.Progress.UsedExperience);
            Assert.Equal(0, player.Progress.AbilityPoints);
            Assert.Equal(0, player.Progress.UsedAbilityPoints);
            var inventory = Assert.IsType<PlayerInventory>(player.Inventory);
            Assert.Equal(known.Items, inventory.Items.Count);
            Assert.Equal((ushort)250, Assert.Single(inventory.Items, i => i.Name == "Crowns").Quantity);
            Assert.Equal((ushort)known.Bread, Assert.Single(inventory.Items, i => i.Name == "Bread").Quantity);
            Assert.DoesNotContain(inventory.Items, i => i.Name is "Orens" or "Florens");
            Assert.All(container.Original.AsSpan(28, container.HeaderSize - 28).ToArray(), b => Assert.Equal(0, b));
            Assert.Equal(container.Original.Length, container.Chunks[^1].Offset + container.Chunks[^1].StoredSize);
        }

        var requirements = set.AllDiskFiles.Where(f => f.FileName.EndsWith(".req", StringComparison.Ordinal)).ToArray();
        Assert.Equal(8, requirements.Length);
        Assert.All(requirements, f => Assert.Equal("content0;content1;content2;"u8.ToArray(), f.ReadAllBytes()));
    }

    [Fact]
    public void EverySampleEditsAllPlayerFieldsAndKeepsUnmodifiedVariablesBitIdentical()
    {
        foreach (var file in EnabledWriteFixture.Files())
        {
            var before = WitcherContainer.Parse(file.ReadAllBytes());
            var oldDirectory = Sav3Directory.Parse(before);
            var oldTree = Sav3Tree.Parse(before.Payload, oldDirectory);
            var player = PlayerData.Read(before, oldDirectory, oldTree)!;
            var crowns = Assert.Single(player.Inventory!.Items, i => i.Name == "Crowns");
            var bread = Assert.Single(player.Inventory.Items, i => i.Name == "Bread");
            using var document = new Witcher3Plugin().Open(file);
            _ = document.BuildView();
            ValueEdit[] edits = [new("player/level", FieldValue.Of(2)), new("player/xp", FieldValue.Of(player.Progress.Experience + 25)),
                new("player/abilityPoints", FieldValue.Of(7)), new(crowns.ValueId, FieldValue.Of(300)), new(bread.ValueId, FieldValue.Of(10))];
            var outcome = document.ApplyEdits(edits);
            Assert.True(outcome.Succeeded);
            Assert.Equal(5, outcome.Changes.Count);
            var output = Assert.Single(document.Serialize());
            Assert.Equal(file.RelativePath, output.RelativePath); // No companion rewrite.
            var after = WitcherContainer.Parse(output.Content.ToArray());
            var edited = PlayerSaveFixture.Read(after).Player;
            Assert.Equal(2, edited.Progress.Level);
            Assert.Equal(player.Progress.Experience + 25, edited.Progress.Experience);
            Assert.Equal(7, edited.Progress.AbilityPoints);
            Assert.Equal((ushort)300, Assert.Single(edited.Inventory!.Items, i => i.Name == "Crowns").Quantity);
            Assert.Equal((ushort)10, Assert.Single(edited.Inventory.Items, i => i.Name == "Bread").Quantity);
            var newDirectory = Sav3Directory.Parse(after);
            var manager = Assert.Single(oldTree.Nodes, n => n.Name == "levelManager");
            var manu = Assert.Single(oldTree.Nodes, n => n.Tag == "MANU");
            var changed = new[] { (manager.Offset, manager.Size), (manu.Offset, manu.Size), (crowns.QuantityOffset, 2), (bread.QuantityOffset, 2) };
            Assert.Equal(oldDirectory.Entries.Count, newDirectory.Entries.Count);
            for (var index = 0; index < oldDirectory.Entries.Count; index++)
            {
                var oldEntry = oldDirectory.Entries[index];
                var newEntry = newDirectory.Entries[index];
                if (changed.Any(c => c.Item1 < oldEntry.Offset + oldEntry.Size && c.Item1 + c.Item2 > oldEntry.Offset))
                {
                    continue;
                }

                Assert.Equal(oldEntry.Size, newEntry.Size);
                Assert.True(before.Payload.AsSpan(oldEntry.Offset, oldEntry.Size).SequenceEqual(after.Payload.AsSpan(newEntry.Offset, newEntry.Size)),
                    $"Unmodified variable {index} in {file.FileName} changed.");
            }

            Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
            Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void QuantityOnlyEditChangesExactlyItsTwoBodyBytesAndAllChunkLengthsAreCorrect()
    {
        foreach (var file in EnabledWriteFixture.Files())
        {
            var before = WitcherContainer.Parse(file.ReadAllBytes());
            var player = PlayerSaveFixture.Read(before).Player;
            var crowns = Assert.Single(player.Inventory!.Items, i => i.Name == "Crowns");
            using var document = new Witcher3Plugin().Open(file);
            _ = document.BuildView();
            Assert.True(document.ApplyEdits([new(crowns.ValueId, FieldValue.Of(500))]).Succeeded);
            var first = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.Equal(first, Assert.Single(document.Serialize()).Content.ToArray());
            var after = WitcherContainer.Parse(first);
            var expected = before.Payload.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(crowns.QuantityOffset), 500);
            Assert.Equal(expected, after.Payload);
            Assert.Equal(before.HeaderSize, after.HeaderSize);
            var chunk = Assert.Single(after.Chunks);
            Assert.Equal(after.Payload.Length, chunk.ExpandedSize);
            Assert.Equal(first.Length - after.HeaderSize, chunk.StoredSize);
            Assert.Equal(before.Chunks[0].EndOffset == 0 ? 0 : first.Length, chunk.EndOffset);
        }
    }
}
