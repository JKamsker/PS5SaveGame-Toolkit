using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroRestoreSampleTests
{
    private const string ActionId = "collectibles/restore-imported";
    private const string CollectionId = "astro.collectibles";
    private const string Flag = "/IsGot[0]/text[0]";

    [Fact]
    public void EverySampleRestoresOnlyOpeningOwnershipAndReturnsExactOriginalAllocation()
    {
        foreach (var file in AstroTestData.Samples())
        {
            using var document = new AstroPlayroomPlugin().Open(file);
            var original = file.ReadAllBytes();
            Assert.Equal(original, AstroTestData.Output(document));
            var fields = AstroTestData.Fields(document).Where(field => field.ValueId.StartsWith('/')).ToArray();
            var owned = Ownership(fields).Where(field => field.Value.AsBool()).ToArray();
            Assert.Equal(file.Directory.Generation == 0 ? 0 : 13, owned.Length);
            Assert.True(document.ApplyEdits(owned.Select(field => new ValueEdit(field.ValueId, FieldValue.Of(false))).ToArray()).Succeeded);
            var staged = AstroTestData.Output(document);
            var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
            var parameters = new Dictionary<string, FieldValue>();
            var plan = planner.PlanAction(ActionId, parameters, new());
            Assert.Empty(plan.Errors);
            Assert.Equal(owned.Length, plan.Changes.Count);
            Assert.Equal(164 - owned.Length, plan.SkippedRows.Count);
            Assert.Equal(owned.Select(field => field.ValueId).Order(StringComparer.Ordinal),
                plan.Changes.Select(change => change.ValueId).Order(StringComparer.Ordinal));
            Assert.Equal(staged, AstroTestData.Output(document));
            Assert.True(planner.ApplyActionPlan(ActionId, parameters, new(), plan).Succeeded);
            // Independent oracle: restore must reproduce every imported byte, including the
            // unknown suffix, original whitespace, preamble, terminator and zero padding.
            Assert.Equal(original, AstroTestData.Output(document));
            using var reopened = AstroTestData.Open(AstroTestData.Output(document));
            Assert.All(fields, field => Assert.Equal(field.Value, reopened.GetValue(field.ValueId)));
            Assert.Empty(planner.PlanAction(ActionId, parameters, new()).Changes);
            using var isolated = new AstroPlayroomPlugin().Open(file);
            Assert.Empty(Assert.IsAssignableFrom<IActionPlanningDocument>(isolated).PlanAction(ActionId, parameters, new()).Changes);
        }
    }

    [Theory]
    [InlineData("Artifacts", "Artifact")]
    [InlineData("Puzzle pieces", "Puzzle")]
    [InlineData("Props", "Prop")]
    public void TableDetailsPublishRestoreAndSelectionDoesNotRestoreUnselectedRows(string title, string group)
    {
        var file = AstroTestData.Samples().Single(file => file.Directory.Generation == 1 && !file.Directory.IsBackup);
        using var document = new AstroPlayroomPlugin().Open(file);
        var owned = Ownership(AstroTestData.Fields(document)).Where(field => field.Value.AsBool()).ToArray();
        Assert.True(document.ApplyEdits(owned.Select(field => new ValueEdit(field.ValueId, FieldValue.Of(false))).ToArray()).Succeeded);
        var table = Assert.Single(AstroCatalogueTestData.Group(document, title).Children.OfType<TableNode>());
        var selected = table.Rows.First(row => owned.Any(field => field.ValueId == row.Key + Flag));
        var action = Assert.Single(Assert.IsType<SectionNode>(selected.Cells["details"]).Children.OfType<ActionNode>(),
            action => action.ActionId == ActionId);
        Assert.True(action.SupportsPlanning);
        Assert.Contains("opening", action.Description, StringComparison.OrdinalIgnoreCase);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope([selected.Key]);
        var plan = planner.PlanAction(ActionId, new Dictionary<string, FieldValue>(), scope);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(selected.Key + Flag, change.ValueId);
        Assert.StartsWith(group + "/", change.Label, StringComparison.Ordinal);
        Assert.True(planner.ApplyActionPlan(ActionId, new Dictionary<string, FieldValue>(), scope, plan).Succeeded);
        Assert.All(owned, field => Assert.Equal(FieldValue.Of(field.ValueId == change.ValueId), document.GetValue(field.ValueId)));
    }

    [Fact]
    public void QuerySelectionCancelApplyUndoRedoAndExportRetainTheOpeningBaseline()
    {
        var plugin = new AstroPlayroomPlugin();
        var set = SampleSaves.Load("PPSA01325");
        using var workspace = new Workspace("astro-restore", "Restore ownership review", set, new PluginCatalog([plugin]));
        var primary = Assert.Single(set.AllDiskFiles, file => file.PathInDirectory == "memory.dat" &&
            file.Directory.Generation == 1 && !file.Directory.IsBackup);
        var backup = Assert.Single(set.AllDiskFiles, file => file.PathInDirectory == "memory.dat" &&
            file.Directory.Generation == 1 && file.Directory.IsBackup);
        var original = primary.ReadAllBytes();
        var session = workspace.Open(primary.Id, plugin.Info.Id);
        using var opening = plugin.Open(primary);
        var fields = AstroTestData.Fields(opening).Where(field => field.ValueId.StartsWith('/')).ToArray();
        _ = session.BuildView();
        var owned = Ownership(fields).Where(field => field.Value.AsBool()).ToArray();
        Assert.Equal(13, owned.Length);
        var raw = new Queue<string>();
        raw.Enqueue("/PreInstall[0]");
        while (raw.TryDequeue(out var lazyId))
        {
            foreach (var section in session.Expand(lazyId).OfType<SectionNode>())
            {
                if (section.LazyId is { } child) raw.Enqueue(child);
            }
        }
        Assert.True(session.ApplyEdits(owned.Select(field => new IncomingEdit(field.ValueId,
            JsonSerializer.SerializeToElement(false))).ToArray()).Ok);
        var reduced = Assert.Single(session.Serialize()).Content.ToArray();
        var excluded = owned[0].ValueId[..^Flag.Length];
        var scope = new ActionScope
        {
            QueryScope = new(CollectionId, new QueryableCollectionQuery { Limit = 2 }, session.State.Revision)
            { ExcludedRowKeys = [excluded] }
        };
        var parameters = new Dictionary<string, JsonElement>();
        var cancelled = session.PlanAction(ActionId, parameters, scope, session.State.Revision);
        Assert.Empty(cancelled.Plan.Errors);
        Assert.Equal(12, cancelled.Plan.Changes.Count);
        Assert.Equal(163, cancelled.Scope.RowKeys!.Count);
        Assert.Equal(reduced, Assert.Single(session.Serialize()).Content.ToArray());
        var preview = session.PlanAction(ActionId, parameters, scope, session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.False(session.InvokeAction(ActionId, parameters).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var restored = Assert.Single(session.Serialize()).Content.ToArray();
        using (var reopened = AstroTestData.Open(restored))
        {
            Assert.All(owned, field => Assert.Equal(FieldValue.Of(field.ValueId != excluded + Flag), reopened.GetValue(field.ValueId)));
        }
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Assert.Single(session.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(restored, Assert.Single(session.Serialize()).Content.ToArray());
        Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);

        var export = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.ChangedFiles });
        Assert.Equal(new[] { primary.RelativePath, backup.RelativePath }.Order(StringComparer.Ordinal),
            export.Overrides.Keys.Order(StringComparer.Ordinal));
        using var zipBytes = new MemoryStream();
        WorkspaceExporter.WriteZip(export, zipBytes);
        zipBytes.Position = 0;
        using var archive = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            Assert.Equal(restored, output.ToArray());
            using var reopened = AstroTestData.Open(output.ToArray());
            Assert.All(fields, field => Assert.Equal(field.ValueId == excluded + Flag ? FieldValue.Of(false) : field.Value,
                reopened.GetValue(field.ValueId)));
        }
        Assert.Equal(original, primary.ReadAllBytes());
        Assert.Equal(original, backup.ReadAllBytes());
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Empty(WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.ChangedFiles }).Overrides);
    }

    private static IEnumerable<FieldNode> Ownership(IEnumerable<FieldNode> fields) => fields.Where(field =>
        field.ValueId.EndsWith(Flag, StringComparison.Ordinal) &&
        (field.Description!.StartsWith("PreInstall/Artifact/", StringComparison.Ordinal) ||
         field.Description.StartsWith("PreInstall/Puzzle/", StringComparison.Ordinal) ||
         field.Description.StartsWith("PreInstall/Prop/", StringComparison.Ordinal)));
}
