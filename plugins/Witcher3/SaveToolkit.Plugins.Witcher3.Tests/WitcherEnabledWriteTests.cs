using System.Buffers.Binary;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherEnabledWriteTests
{
    [Fact]
    public void OmittedZeroRankIsInsertedPreservingAllOtherBytesAndUndoReturnsExactInput()
    {
        foreach (var file in EnabledWriteFixture.Files())
        {
            var initial = EnabledWriteFixture.Read(file.ReadAllBytes());
            var id = initial.Features.SkillEdits!.Ranks.First(r => r.Original == 0 && r.Maximum > 1).Entry.Id;
            var input = EnabledWriteFixture.WithoutSkillLevel(file.ReadAllBytes(), id);
            var before = EnabledWriteFixture.Read(input);
            var rank = before.Features.SkillEdits!.Ranks.Single(r => r.Entry.Id == id);
            Assert.Null(rank.Entry.Record.Find("level"));
            using var document = WitcherTestFiles.Open(input);
            _ = document.BuildView();
            _ = document.ExpandNode("ability/skills/0");
            var applied = document.ApplyEdits([new(rank.ValueId, FieldValue.Of(rank.Maximum))]);
            Assert.True(applied.Succeeded);
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            var reopened = EnabledWriteFixture.Read(output);
            Assert.Equal(rank.Maximum, reopened.Features.SkillEdits!.Ranks.Single(r => r.Entry.Id == id).Value);
            EnabledWriteFixture.SkillPreservation(input, output, rank);
            EnabledWriteFixture.SemanticChanges(input, output, ["container/expandedBytes", "directory/namePointer",
                $"entity/{before.Player.EntityId}/abilities/skills/{id}/level"]);
            Assert.True(document.ApplyEdits(applied.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(document.ApplyEdits(applied.Changes.Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray()).Succeeded);
            Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void CombinedWritesAcrossSkillPagesAndDictionaryGrowthUndoTogether()
    {
        foreach (var file in EnabledWriteFixture.Files())
        {
            var input = file.ReadAllBytes();
            var before = EnabledWriteFixture.Read(input);
            var ranks = before.Features.SkillEdits!.Ranks;
            var item = before.Player.Inventory!.Items.First(i => i.CanEditDurability);
            var copies = before.Features.CopyEdits[0];
            var plugin = new Witcher3Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("mixed-writes", file, plugin, document);
            EnabledWriteFixture.Bind(session, before.Features);
            ValueEdit[] edits = [new(ranks[0].ValueId, FieldValue.Of(1)), new(ranks[^1].ValueId, FieldValue.Of(1)),
                new("player/abilityPoints", FieldValue.Of(7)), new(item.DurabilityId, FieldValue.Of(50.0)), new(copies.ValueId, FieldValue.Of(0))];
            var applied = session.ApplyEdits(edits.Select(e => EnabledWriteFixture.Incoming(e.ValueId, e.Value)).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(applied.Ok);
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            var after = EnabledWriteFixture.Read(output);
            Assert.Equal(7, after.Player.Progress.AbilityPoints);
            Assert.Equal(50, after.Player.Inventory!.Items.Single(i => i.Id == item.Id).Durability);
            Assert.All(new[] { ranks[0], ranks[^1] }, r => Assert.Equal(1, after.Features.SkillEdits!.Ranks.Single(a => a.Entry.Id == r.Entry.Id).Value));
            Assert.Equal(0, after.Features.CopyEdits.Single(c => c.Card.Index == copies.Card.Index).Value);
            Assert.True(session.Undo().Ok);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("durability")]
    [InlineData("skill")]
    [InlineData("ordinary-card")]
    [InlineData("leader-card")]
    public void EveryEnabledWritePersistsRejectsInvalidValuesAndSupportsSessionUndoRedo(string kind)
    {
        Assert.Equal(8, EnabledWriteFixture.Files().Count);
        foreach (var file in EnabledWriteFixture.Files())
        {
            var input = file.ReadAllBytes();
            var before = EnabledWriteFixture.Read(input);
            var plugin = new Witcher3Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("enabled-writes", file, plugin, document);
            EnabledWriteFixture.Bind(session, before.Features);
            var inventory = before.Player.Inventory!.Items;
            var rank = before.Features.SkillEdits!.Ranks.First(r => r.Original == 0 && r.Maximum > 1);
            var item = inventory.First(i => i.CanEditDurability);
            var card = before.Features.CopyEdits.First(c => c.Card.Leader == (kind == "leader-card"));
            var id = kind switch { "durability" => item.DurabilityId, "skill" => rank.ValueId, _ => card.ValueId };
            var proposed = kind switch {
                "durability" => FieldValue.Of(42.25), "skill" => FieldValue.Of(2),
                "leader-card" => FieldValue.Of(0), _ => FieldValue.Of(card.Value == card.Maximum ? card.Value - 1 : card.Value + 1),
            };
            var invalid = kind switch {
                "durability" => FieldValue.Of(double.NaN), "skill" => FieldValue.Of(rank.Maximum + 1), _ => FieldValue.Of(card.Maximum + 1),
            };
            // Also exercise plugin validation directly; bypassing Core must not publish an invalid field.
            Assert.False(document.ApplyEdits([new("player/level", FieldValue.Of(2)), new(id, invalid)]).Succeeded);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(document.ApplyEdits([new(id, FieldValue.Of(-1))]).Succeeded);
            Assert.False(document.ApplyEdits([new(id, FieldValue.Of("invalid"))]).Succeeded);
            Assert.False(document.ApplyEdits([new(id, kind == "durability" ? FieldValue.Of(101.0) : FieldValue.Of(1.5))]).Succeeded);
            var outcome = session.ApplyEdits([EnabledWriteFixture.Incoming(id, proposed)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(outcome.Ok, string.Join("; ", outcome.Errors.Select(e => e.Message)));
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            using var reopened = plugin.Open(SaveToolkit.Testing.MemorySaveSet.SingleFile("Manual.sav", output, "PPSA04018"));
            _ = reopened.BuildView();
            if (kind == "skill") { _ = reopened.ExpandNode("ability/skills/0"); }
            if (kind.EndsWith("card", StringComparison.Ordinal)) { _ = ((SaveToolkit.Sdk.Collections.IQueryableDocument)reopened).Query(GwentCollection.CollectionId, new() { Limit = 200 }); }
            if (kind == "durability")
            {
                _ = ((SaveToolkit.Sdk.Collections.IQueryableDocument)reopened).Query(InventoryViews.CollectionId, new() { Limit = 200 });
            }
            Assert.Equal(proposed, reopened.GetValue(id));
            var root = $"entity/{before.Player.EntityId}";
            var expectedPaths = new List<string>();
            if (kind == "skill")
            {
                EnabledWriteFixture.SkillPreservation(input, output, rank);
                expectedPaths.Add($"{root}/abilities/skills/{rank.Entry.Id}/level");
                if (WitcherContainer.Parse(output).Payload.Length != before.Container.Payload.Length)
                {
                    expectedPaths.Add("container/expandedBytes");
                    expectedPaths.Add("directory/namePointer");
                }
            }
            else
            {
                var expected = before.Container.Payload.ToArray();
                if (kind == "durability")
                {
                    BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(item.DurabilityOffset), (float)proposed.AsDouble());
                    expectedPaths.Add($"{root}/inventory/{item.Id}/durability");
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(card.Card.CopiesOffset), proposed.AsInt32());
                    expectedPaths.Add(card.ValueId);
                }
                Assert.Equal(expected, WitcherContainer.Parse(output).Payload);
            }
            EnabledWriteFixture.SemanticChanges(input, output, expectedPaths);
            Assert.True(session.Undo().Ok);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }
}
