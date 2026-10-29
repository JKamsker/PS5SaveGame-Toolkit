using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class CharacterActionRegressionTests
{
    private const int OpeningLevel = 40;
    private const int TargetLevel = 25;
    private const int HistoryCycles = 3;

    [Theory]
    [InlineData("PPSA01911", "ff7remake000", 0, 1)]
    [InlineData("PPSA01911", "ff7remake000", 0, TargetLevel)]
    [InlineData("PPSA01911", "ff7remake000", 0, OpeningLevel)]
    [InlineData("PPSA01911", "ff7remakeplus000", 5, 1)]
    [InlineData("PPSA01911", "ff7remakeplus000", 5, TargetLevel)]
    [InlineData("PPSA01911", "ff7remakeplus000", 5, OpeningLevel)]
    [InlineData("PPSA08664", "ff7rebirth000", 0, 1)]
    [InlineData("PPSA08664", "ff7rebirth000", 0, TargetLevel)]
    [InlineData("PPSA08664", "ff7rebirth000", 0, OpeningLevel)]
    [InlineData("PPSA18936", "ff7rebirth000", 0, 1)]
    [InlineData("PPSA18936", "ff7rebirth000", 0, TargetLevel)]
    [InlineData("PPSA18936", "ff7rebirth000", 0, OpeningLevel)]
    public void LevelPlansRestoreExactOpeningPairAcrossRepeatedApplyUndoRedo(string title, string name, int character, int openingXpTier)
    {
        using var fixture = new CoverageFixture(title, name, (bytes, schema) =>
        {
            var at = CharacterLayout.Offset(schema, character);
            bytes[at] = OpeningLevel;
            CoverageFixture.Write(bytes, at + CharacterLayout.ExperienceDelta,
                ProgressionEdits.CharacterCurves(schema)[character][openingXpTier - 1]);
        });
        var offset = CharacterLayout.Offset(fixture.Schema, character);
        var openingXp = CoverageFixture.Read(fixture.Original, offset + CharacterLayout.ExperienceDelta);
        var targetXp = ProgressionEdits.CharacterCurves(fixture.Schema)[character][TargetLevel - 1];
        var levelId = $"character/{character}/level";
        var xpId = $"character/{character}/experience";
        for (var cycle = 0; cycle < HistoryCycles; cycle++)
        {
            var preview = fixture.Preview(CoverageFixture.LevelAction, character, TargetLevel);
            Assert.Empty(preview.Plan.Errors);
            var xp = Assert.Single(preview.Plan.Changes, change => change.Derived);
            Assert.Equal(xpId, xp.ValueId);
            Assert.Equal(openingXp, xp.OldValue.AsUInt32());
            Assert.Equal(targetXp, xp.NewValue.AsUInt32());
            Assert.NotNull(preview.PlanId);
            Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(TargetLevel, fixture.Document.GetValue(levelId).AsInt32());
            Assert.Equal(targetXp, fixture.Document.GetValue(xpId).AsUInt32());
            var edited = fixture.Bytes();
            var allowed = openingXpTier == TargetLevel ? new[] { offset }
                : new[] { offset }.Concat(Enumerable.Range(offset + CharacterLayout.ExperienceDelta, sizeof(uint)));
            ByteChangeAssertions.OnlyOffsetsChanged(fixture.Original, edited, allowed);
            for (var replay = 0; replay < HistoryCycles; replay++)
            {
                Assert.True(fixture.Session.Undo().Ok);
                Assert.Equal(OpeningLevel, fixture.Document.GetValue(levelId).AsInt32());
                Assert.Equal(openingXp, fixture.Document.GetValue(xpId).AsUInt32());
                Assert.Equal(fixture.Original, fixture.Bytes());
                Assert.False(fixture.Session.State.CanUndo);
                Assert.True(fixture.Session.Redo().Ok);
                Assert.Equal(TargetLevel, fixture.Document.GetValue(levelId).AsInt32());
                Assert.Equal(targetXp, fixture.Document.GetValue(xpId).AsUInt32());
                Assert.Equal(edited, fixture.Bytes());
            }
            Assert.True(fixture.Session.Undo().Ok);
            Assert.Equal(fixture.Original, fixture.Bytes());
        }
    }

    [Theory]
    [InlineData("PPSA01911", "ff7remake000", CoverageFixture.RecoveryAction)]
    [InlineData("PPSA01911", "ff7remake000", CoverageFixture.LevelAction)]
    [InlineData("PPSA01911", "ff7remakeplus000", CoverageFixture.RecoveryAction)]
    [InlineData("PPSA01911", "ff7remakeplus000", CoverageFixture.LevelAction)]
    [InlineData("PPSA08664", "ff7rebirth000", CoverageFixture.RecoveryAction)]
    [InlineData("PPSA08664", "ff7rebirth000", CoverageFixture.LevelAction)]
    [InlineData("PPSA18936", "ff7rebirth000", CoverageFixture.RecoveryAction)]
    [InlineData("PPSA18936", "ff7rebirth000", CoverageFixture.LevelAction)]
    public void RenderedTableActionsUseCloudsRowKeyWhileDocumentAndEmptyScopesStayDistinct(string title, string name, string actionId)
    {
        using var fixture = new CoverageFixture(title, name, (bytes, schema) =>
        {
            for (var character = 0; character < Ff7Schema.CharacterCount; character++)
            {
                var at = CharacterLayout.Offset(schema, character);
                bytes[at] = OpeningLevel;
                CoverageFixture.Write(bytes, at + CharacterLayout.ExperienceDelta, 0);
                CoverageFixture.Write(bytes, at + CharacterLayout.HpDelta, 0);
                CoverageFixture.Write(bytes, at + CharacterLayout.HpDelta + CharacterLayout.MaximumVitalDelta, 100);
                CoverageFixture.Write(bytes, at + CharacterLayout.MpDelta, 0);
                CoverageFixture.Write(bytes, at + CharacterLayout.MpDelta + CharacterLayout.MaximumVitalDelta, 10);
            }
        });
        var tabs = Assert.IsType<TabsNode>(fixture.Document.BuildView());
        var view = Assert.Single(tabs.Tabs, tab => tab.Title == "Characters");
        var table = Assert.Single(view.Children.OfType<TableNode>());
        var cloud = Assert.Single(table.Rows, row => row.Key == "character/0");
        Assert.Equal("Cloud Strife", Assert.IsType<TextNode>(cloud.Cells["name"]).Text);
        // Only table descendants inherit F3 selection in the renderer (F6 / #27).
        foreach (var row in table.Rows)
        {
            var action = Assert.Single(row.Cells.Values.OfType<ActionNode>(), node => node.ActionId == actionId);
            Assert.True(action.SupportsPlanning);
            Assert.Contains(table.Columns, column => row.Cells[column.Key] == action);
        }
        var documentAction = Assert.Single(view.Children.OfType<ActionNode>(), action => action.ActionId == actionId);
        Assert.True(documentAction.SupportsPlanning);
        var parameters = actionId == CoverageFixture.LevelAction
            ? new Dictionary<string, JsonElement> { ["level"] = JsonSerializer.SerializeToElement(TargetLevel) } : [];
        var all = fixture.Session.PlanAction(documentAction.ActionId, parameters, new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(all.Plan.Errors);
        Assert.Equal(Ff7Schema.CharacterCount, all.Plan.Changes.Select(change => change.ValueId.Split('/')[1]).Distinct().Count());
        var renderedAction = Assert.Single(cloud.Cells.Values.OfType<ActionNode>(), action => action.ActionId == actionId);
        var empty = fixture.Session.PlanAction(renderedAction.ActionId, parameters, new([]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(empty.Plan.Changes);
        Assert.NotNull(empty.PlanId);
        Assert.True(fixture.Session.ApplyActionPlan(empty.PlanId, empty.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
        var preview = fixture.Session.PlanAction(renderedAction.ActionId, parameters, new([cloud.Key]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(new[] { cloud.Key }, preview.Scope.RowKeys);
        Assert.NotEmpty(preview.Plan.Changes);
        Assert.All(preview.Plan.Changes, change => Assert.StartsWith(cloud.Key + "/", change.ValueId));
        Assert.NotNull(preview.PlanId);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var offset = CharacterLayout.Offset(fixture.Schema, 0);
        var allowed = actionId == CoverageFixture.LevelAction
            ? new[] { offset }.Concat(Enumerable.Range(offset + CharacterLayout.ExperienceDelta, sizeof(uint)))
            : Enumerable.Range(offset + CharacterLayout.HpDelta, sizeof(uint))
                .Concat(Enumerable.Range(offset + CharacterLayout.MpDelta, sizeof(uint)));
        ByteChangeAssertions.OnlyOffsetsChanged(fixture.Original, fixture.Bytes(), allowed);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }
}
