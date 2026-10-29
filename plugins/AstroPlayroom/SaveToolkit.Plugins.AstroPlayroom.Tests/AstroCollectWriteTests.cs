using System.Text;
using System.Text.RegularExpressions;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroCollectWriteTests
{
    private const string CollectAction = "collectibles/collect-all";

    [Fact]
    public void CollectAllRoundTripsEverySampleWithOnlyIntendedOwnershipSpansAndDisplacement()
    {
        foreach (var file in AstroTestData.Samples())
        {
            var plugin = new AstroPlayroomPlugin();
            var original = file.ReadAllBytes();
            using var document = plugin.Open(file);
            var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
            var plan = planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), new());
            var expectedChanges = file.Directory.Generation == 0 ? 164 : 151;
            Assert.Equal(expectedChanges, plan.Changes.Count);
            Assert.Equal(164 - expectedChanges, plan.SkippedRows.Count);
            Assert.Equal(original, AstroTestData.Output(document));

            var outcome = planner.ApplyActionPlan(CollectAction, new Dictionary<string, FieldValue>(), new(), plan);
            Assert.True(outcome.Succeeded);
            Assert.True(outcome.StructureChanged);
            Assert.Equal(expectedChanges, outcome.Changes.Count);
            Assert.Equal(plan.Warnings, outcome.Warnings);
            var output = AstroTestData.Output(document);
            using var reopened = plugin.Open(AstroTestData.File(output));
            Assert.All(plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
            Assert.Empty(Assert.IsAssignableFrom<IActionPlanningDocument>(reopened)
                .PlanAction(CollectAction, new Dictionary<string, FieldValue>(), new()).Changes);

            // Independent text oracle: replace literal missing ownership spans only inside the
            // three collection groups. Unknown text, whitespace, cinematic flags and suffix stay exact.
            var terminator = original.AsSpan(AstroTestData.TextOffset).IndexOf((byte)0) + AstroTestData.TextOffset;
            var text = Encoding.UTF8.GetString(original, AstroTestData.TextOffset, terminator - AstroTestData.TextOffset);
            var expectedText = Regex.Replace(text, @"<(Artifact|Puzzle|Prop)>.*?</\1>", group =>
                group.Value.Replace("<IsGot>false</IsGot>", "<IsGot>true</IsGot>", StringComparison.Ordinal), RegexOptions.Singleline);
            var expected = AstroTestData.Bytes(expectedText);
            Assert.Equal(expected, output);
            Assert.False(ByteDiffer.Compare(expected, output).HasChanges);
            var bytes = ByteDiffer.Compare(original, output);
            Assert.True(bytes.HasChanges);
            Assert.False(bytes.Truncated);
            var firstOwnership = text.IndexOf("<IsGot>false</IsGot>", StringComparison.Ordinal) + AstroTestData.TextOffset;
            Assert.All(bytes.Ranges, range =>
            {
                Assert.True(range.Offset >= firstOwnership);
                Assert.True(range.End <= terminator);
            });

            AssertSemanticOwnershipChanges(plugin, file, output, plan);
        }
    }

    private static void AssertSemanticOwnershipChanges(AstroPlayroomPlugin plugin, ISaveFile file, byte[] output, ActionPlan plan)
    {
        var decoder = new PluginViewDecoder(plugin);
        var diff = SemanticDiffer.Compare(decoder.Decode(file, new()), decoder.Decode(AstroTestData.File(output), new()));
        Assert.Null(diff.Error);
        Assert.False(diff.InputTruncated);
        Assert.False(diff.Truncated);
        Assert.Empty(diff.Warnings);
        Assert.Equal(plan.Changes.Count * 2, diff.Total);
        Assert.Equal(0, diff.Added);
        Assert.Equal(0, diff.Removed);
        Assert.Equal(plan.Changes.Select(c => c.ValueId).Order(StringComparer.Ordinal),
            diff.Changes.Where(c => c.Path.StartsWith('/'))
                .Select(c => c.Path).Order(StringComparer.Ordinal));
        Assert.Equal(plan.Changes.Count, diff.Changes.Count(c => c.Path.StartsWith("collections/", StringComparison.Ordinal)));
    }

    [Fact]
    public void FullCollectPlanAppliesAsOneSessionUndoAndRedoWithLiveCatalogueRefresh()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples().Where(file => !file.Directory.IsBackup))
        {
            var document = plugin.Open(file, context);
            using var session = new DocumentSession("astro-collect", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
            _ = session.BuildView();
            var original = file.ReadAllBytes();
            var preview = session.PlanAction(CollectAction, new Dictionary<string, System.Text.Json.JsonElement>(),
                expectedRevision: session.State.Revision);
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, session.State.Revision).Ok);
            var edited = AstroTestData.Output(document);
            Assert.StartsWith("50/50 owned", AstroCatalogueTestData.Group(document, "Artifacts").Badge, StringComparison.Ordinal);
            Assert.StartsWith("96/96 owned", AstroCatalogueTestData.Group(document, "Puzzle pieces").Badge, StringComparison.Ordinal);
            Assert.StartsWith("18/18 owned", AstroCatalogueTestData.Group(document, "Props").Badge, StringComparison.Ordinal);
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, AstroTestData.Output(document));
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);
            Assert.True(session.Redo().Ok);
            Assert.Equal(edited, AstroTestData.Output(document));
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, AstroTestData.Output(document));
        }
    }

    [Theory]
    [InlineData("Artifacts", "Artifact")]
    [InlineData("Puzzle pieces", "Puzzle")]
    [InlineData("Props", "Prop")]
    public void SelectedRowsCollectOnlyTheChosenMissingFlagAndKeepItsConfidence(string title, string group)
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples().Single(file => file.Directory.Generation == 1 && !file.Directory.IsBackup);
        using var document = plugin.Open(file, context);
        var original = file.ReadAllBytes();
        var originalFields = AstroTestData.Fields(document).Where(field => field.ValueId.StartsWith('/')).ToArray();
        var table = Assert.Single(AstroCatalogueTestData.Group(document, title).Children.OfType<TableNode>());
        var missing = table.Rows.First(row => Assert.IsType<TextNode>(row.Cells["status"]).Text == "Missing");
        var owned = table.Rows.First(row => Assert.IsType<TextNode>(row.Cells["status"]).Text == "Owned");
        var action = Assert.Single(Assert.IsType<SectionNode>(missing.Cells["details"]).Children.OfType<ActionNode>());
        Assert.Equal(CollectAction, action.ActionId);
        Assert.True(action.SupportsPlanning);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope([missing.Key, owned.Key]);
        var plan = planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), scope);
        var change = Assert.Single(plan.Changes);
        Assert.StartsWith(group + "/", change.Label, StringComparison.Ordinal);
        Assert.Equal(missing.Key + "/IsGot[0]/text[0]", change.ValueId);
        Assert.Equal(FieldValue.Of(false), change.OldValue);
        Assert.Equal(FieldValue.Of(true), change.NewValue);
        Assert.Equal(owned.Key, Assert.Single(plan.SkippedRows).RowKey);
        var field = Assert.Single(originalFields, field => field.ValueId == change.ValueId);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.Contains("not validated on console", field.Warning, StringComparison.Ordinal);
        Assert.Equal(original, AstroTestData.Output(document));
        var outcome = planner.ApplyActionPlan(CollectAction, new Dictionary<string, FieldValue>(), scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(new ValueChange(change.ValueId, FieldValue.Of(false), FieldValue.Of(true)), Assert.Single(outcome.Changes));
        var output = AstroTestData.Output(document);
        using var reopened = plugin.Open(AstroTestData.File(output));
        Assert.Equal(FieldValue.Of(true), reopened.GetValue(change.ValueId));
        Assert.All(originalFields.Where(field => field.ValueId != change.ValueId),
            field => Assert.Equal(field.Value, reopened.GetValue(field.ValueId)));

        // Derive the expected patch from the selected saved key, independently of the plan.
        var key = Assert.IsType<TextNode>(missing.Cells["key"]).Text.Split('/')[1];
        var terminator = original.AsSpan(AstroTestData.TextOffset).IndexOf((byte)0) + AstroTestData.TextOffset;
        var text = Encoding.UTF8.GetString(original, AstroTestData.TextOffset, terminator - AstroTestData.TextOffset);
        var pattern = $@"(<{group}>.*?<{Regex.Escape(key)}>.*?<IsGot>)false(</IsGot>)";
        Assert.Single(Regex.Matches(text, pattern, RegexOptions.Singleline));
        var expected = Regex.Replace(text, pattern, match => match.Groups[1].Value + "true" + match.Groups[2].Value,
            RegexOptions.Singleline);
        Assert.NotEqual(text, expected);
        Assert.Equal(AstroTestData.Bytes(expected), output);
    }
}
