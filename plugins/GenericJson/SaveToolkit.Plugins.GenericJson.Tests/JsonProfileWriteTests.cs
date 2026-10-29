using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonProfileWriteTests
{
    private const string Source = """
        {"Profile":{"Minigame.A":"true","MinigameFlag.A_HaveDiscovered":"True","MinigameFlag.B_HavePlayed":"TRUE",
        "MinigameCounter.A_CodyWins":"0012","FurthestUnlockedChapter":"/Game/Maps/A",
        "LastSaveChapter":"/Game/Maps/A","LastSaveProgressPoint":"/Game/Maps/A##Start","PrivateIdentity":"\u0041"},
        "CloudSettings":{"untouched":1.00},"LocalSettings":{}}
        """;

    [Fact]
    public void BooleanAdaptersPreserveFieldSpecificCasingAndAllOtherBytes()
    {
        using var fixture = Open();
        JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, "MinigameFlag.A_HaveDiscovered").ActionId, ("value", FieldValue.Of(false)));
        Assert.Contains("\"False\"", Encoding.UTF8.GetString(fixture.Bytes));
        JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/present-flags", ("enabled", FieldValue.Of(false)));
        var text = Encoding.UTF8.GetString(fixture.Bytes);
        Assert.Contains("\"false\"", text);
        Assert.Contains("\"FALSE\"", text);
        Assert.Contains("\"PrivateIdentity\":\"\\u0041\"", text);
    }

    [Fact]
    public void StringCounterAdapterAndZeroPresetPersistWithoutNumericStorageCoercion()
    {
        using var fixture = Open();
        JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, "MinigameCounter.A_CodyWins").ActionId, ("value", FieldValue.Of(20L)));
        Assert.Contains("\"MinigameCounter.A_CodyWins\":\"20\"", Encoding.UTF8.GetString(fixture.Bytes));
        JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/reset-counters");
        Assert.Contains("\"MinigameCounter.A_CodyWins\":\"0\"", Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Fact]
    public void ProgressTupleChangesExactlyExistingThreeStringsInOneUndoGroup()
    {
        using var fixture = Open();
        _ = JsonSemanticTestHelper.Actions(fixture);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/progress", ("FurthestUnlockedChapter", FieldValue.Of("/Game/Maps/B")),
            ("LastSaveChapter", FieldValue.Of("/Game/Maps/B")), ("LastSaveProgressPoint", FieldValue.Of("/Game/Maps/B##Start")));
    }

    [Fact]
    public void SingleChapterStringAdapterPersistsAndMissingTupleDoesNotInsertAnything()
    {
        using var fixture = Open();
        JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, "LastSaveChapter").ActionId, ("value", FieldValue.Of("/Game/Maps/B")));
        using var missing = new JsonSessionFixture("{\"Profile\":{\"LastSaveChapter\":\"a\"},\"CloudSettings\":{},\"LocalSettings\":{}}",
            title: "PPSA02343", fileName: "ue4savegame.dpx.sav");
        _ = JsonSemanticTestHelper.Actions(missing);
        var preview = missing.Session.PlanAction("semantic/progress", JsonArrayEditingTests.Parameters(), expectedRevision: 0, scope: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.False(missing.Session.State.CanUndo);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(68)]
    public void CounterBoundsRejectAtomicallyAndDoNotChangeHistory(int number)
    {
        using var fixture = Open();
        var action = JsonSemanticTestHelper.SetFor(fixture, "MinigameCounter.A_CodyWins");
        var before = fixture.Bytes;
        var preview = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.Of(number))), expectedRevision: 0, scope: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal(before, fixture.Bytes);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void SelectedFilteredFlagPresetSkipsNonFlagsAndEmptySelectionDoesNothing()
    {
        using var fixture = Open();
        _ = JsonSemanticTestHelper.Actions(fixture);
        var scope = new ActionScope { QueryScope = new("json-tree", new QueryableCollectionQuery { Text = "A_" }, 0) };
        var preview = fixture.Session.PlanAction("semantic/present-flags", JsonArrayEditingTests.Parameters(("enabled", FieldValue.Of(false))), scope, 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Single(preview.Plan.Changes);
        Assert.Single(preview.Plan.SkippedRows);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(Source), fixture.Bytes);
        var empty = fixture.Session.PlanAction("semantic/present-flags", JsonArrayEditingTests.Parameters(("enabled", FieldValue.Of(false))), new([]), fixture.Session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(empty.PlanId);
        Assert.Empty(empty.Plan.Changes);
    }

    private static JsonSessionFixture Open() => new(Source, title: "PPSA02343", fileName: "ue4savegame.dpx.sav");
}
