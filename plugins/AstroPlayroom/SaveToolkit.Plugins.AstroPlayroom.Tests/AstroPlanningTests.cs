using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroPlanningTests
{
    private const string CollectAllAction = "collectibles/collect-all";
    private const string SetCoinsAction = "common/set-coins";

    [Fact]
    public void CompletionAndProgressSummariesUseExistingRecordsAndFlags()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples().Single(file => file.Directory.Generation == 1 && !file.Directory.IsBackup), context);
        var tabs = Assert.IsType<TabsNode>(document.BuildView()).Tabs;

        var collection = Assert.Single(tabs, tab => tab.Title == "Collectibles");
        var candidate = Assert.Single(collection.Children.OfType<SectionNode>(), section => section.Title == "Candidate-area progress");
        var stats = Assert.IsType<StatsNode>(candidate.Children[0]);
        Assert.Equal("13/164", Assert.Single(stats.Items, item => item.Label == "Saved collectibles owned").Value);
        Assert.Equal(8, Assert.IsType<TableNode>(candidate.Children[2]).Rows.Count);

        var progress = Assert.Single(tabs, tab => tab.Title == "Progress");
        var stored = Assert.Single(progress.Children.OfType<SectionNode>(), section => section.Title == "Stored progress summary");
        Assert.Contains("not a gameplay completion proof", Assert.IsType<TextNode>(stored.Children[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectAllPreviewSkipsEveryExistingRecordWithoutProposingWrites()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples()[0], context);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);

        var plan = planner.PlanAction(CollectAllAction, new Dictionary<string, FieldValue>(), new());

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(164, plan.SkippedRows.Count);
        Assert.Contains("Collectible writes are disabled", Assert.Single(plan.Warnings), StringComparison.Ordinal);
        Assert.Equal(AstroTestData.Samples()[0].ReadAllBytes(), AstroTestData.Output(document));
    }

    [Fact]
    public void CoinPreviewAppliesOneChangeAndRoundTripsTheObservedStorageTransition()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples()[0], context);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(SetCoinsAction,
            new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(1749L) }, new());

        var change = Assert.Single(plan.Changes);
        Assert.Equal("Coins (Common/NumCoin)", change.Label);
        Assert.Equal(FieldValue.Of(0L), change.OldValue);
        Assert.Equal(FieldValue.Of(1749L), change.NewValue);
        var outcome = planner.ApplyActionPlan(SetCoinsAction,
            new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(1749L) }, new(), plan);

        Assert.True(outcome.Succeeded);
        Assert.Single(outcome.Changes);
        using var reopened = AstroTestData.Open(AstroTestData.Output(document));
        Assert.Equal(FieldValue.Of(1749L), AstroTestData.Find(reopened, AstroTestData.CoinsPath).Value);
    }

    [Fact]
    public void SessionAppliesCoinPlanAsOneUndoableGroupAndRejectsDirectCollectAction()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples()[0];
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("astro-plan", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
        _ = session.BuildView();

        var direct = session.InvokeAction(CollectAllAction, new Dictionary<string, JsonElement>(), session.State.Revision);
        Assert.False(direct.Ok);
        Assert.Contains("requires a preview", Assert.Single(direct.Errors).Message, StringComparison.Ordinal);

        var preview = session.PlanAction(SetCoinsAction,
            new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(1749L) },
            expectedRevision: session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Single(preview.Plan.Changes);
        var applied = session.ApplyActionPlan(preview.PlanId!, session.State.Revision);
        Assert.True(applied.Ok);
        Assert.Single(applied.Changes);
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
    }
}
