using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GroupSelectionTests
{
    private const string ActionId = "global/json/bulk-set";
    private static readonly IReadOnlyDictionary<string, FieldValue> Parameters =
        new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("49") };
    private static readonly IReadOnlyDictionary<string, JsonElement> IncomingParameters =
        new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("49") };

    public static IEnumerable<object[]> MalformedSelections()
    {
        foreach (var xor in new[] { false, true })
        foreach (var key in new[]
        {
            "{volume}/extra", "global/{volume}", "foreign/value/2", "Global/value/2",
            "global/json/value/2", "global/", "global//value/2", "global/value/",
            "global/value//2", "global/value/999999", "global/value/0002", "global", "{level}",
        })
            yield return [xor, key];
    }

    [Theory]
    [MemberData(nameof(MalformedSelections))]
    public void MixedSelectionRejectsEveryMalformedKeyWithoutTokenOrWrites(bool xor, string keyTemplate)
    {
        using var fixture = new GroupIdentityFixture(xor);
        var fields = ViewTraversal.Fields(fixture.Group);
        var volume = fields.Single(field => field.Label == "volume");
        var level = fields.Single(field => field.Label == "Level");
        var badKey = keyTemplate.Replace("{volume}", volume.ValueId, StringComparison.Ordinal)
            .Replace("{level}", level.ValueId, StringComparison.Ordinal);
        using var scratch = new WorkspaceScratch();
        using var store = new WorkspaceStore(new PluginCatalog([fixture.Plugin]), new() { DataRoot = scratch.Root });
        var (workspace, session) = OpenSession(store, fixture);

        // Test both orders: a valid row must not hide an invalid row later in the selection.
        foreach (var keys in new[] { new[] { volume.ValueId, badKey }, new[] { badKey, volume.ValueId } })
        {
            var scope = new ActionScope(keys);
            var preview = session.PlanAction(ActionId, IncomingParameters, scope);
            AssertRejected(preview.Plan, badKey);
            Assert.Null(preview.PlanId);
            AssertOriginalBytes(fixture, workspace);
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);

            var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Group);
            AssertRejected(planner.PlanAction(ActionId, Parameters, scope), badKey);
            // Even a valid plan cannot be applied with a newly malformed scope.
            var valid = planner.PlanAction(ActionId, Parameters, new([volume.ValueId]));
            var rejected = planner.ApplyActionPlan(ActionId, Parameters, scope, valid);
            Assert.False(rejected.Succeeded);
            Assert.Empty(rejected.Changes);
            fixture.AssertOriginalBytes(fixture.Group);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateKeysRetainDirectDeduplicationAndCoreRejection(bool xor)
    {
        using var fixture = new GroupIdentityFixture(xor);
        var volume = ViewTraversal.Fields(fixture.Group).Single(field => field.Label == "volume");
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Group);
        var unique = planner.PlanAction(ActionId, Parameters, new([volume.ValueId]));
        var duplicated = planner.PlanAction(ActionId, Parameters, new([volume.ValueId, volume.ValueId]));
        Assert.Empty(duplicated.Errors);
        Assert.Equal(unique.Changes, duplicated.Changes);
        Assert.Single(duplicated.Changes);
        Assert.Empty(duplicated.SkippedRows);
        fixture.AssertOriginalBytes(fixture.Group);

        using var scratch = new WorkspaceScratch();
        using var store = new WorkspaceStore(new PluginCatalog([fixture.Plugin]), new() { DataRoot = scratch.Root });
        var (workspace, session) = OpenSession(store, fixture);
        var preview = session.PlanAction(ActionId, IncomingParameters, new([volume.ValueId, volume.ValueId]));
        Assert.Null(preview.PlanId);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("distinct row keys", StringComparison.Ordinal));
        AssertOriginalBytes(fixture, workspace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidSelectionStillPreviewsAppliesAndUndoesExactly(bool xor)
    {
        using var fixture = new GroupIdentityFixture(xor);
        using var scratch = new WorkspaceScratch();
        using var store = new WorkspaceStore(new PluginCatalog([fixture.Plugin]), new() { DataRoot = scratch.Root });
        var (workspace, session) = OpenSession(store, fixture);
        var volume = ViewTraversal.Nodes(session).OfType<FieldNode>().Single(field => field.Label == "volume");
        var preview = session.PlanAction(ActionId, IncomingParameters, new([volume.ValueId]));
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        var change = Assert.Single(preview.Plan.Changes);
        Assert.Equal(volume.ValueId, change.ValueId);
        Assert.Equal(FieldValue.ExactNumber("50"), change.OldValue);
        Assert.Equal(FieldValue.ExactNumber("49"), change.NewValue);
        AssertOriginalBytes(fixture, workspace);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(change.NewValue, ViewTraversal.Nodes(session).OfType<FieldNode>().Single(field => field.ValueId == volume.ValueId).Value);
        Assert.True(session.Undo().Ok);
        AssertOriginalBytes(fixture, workspace);
    }

    private static void AssertRejected(ActionPlan plan, string badKey)
    {
        Assert.NotEmpty(plan.Errors);
        Assert.Contains(plan.Errors, error => error.Message.Contains(badKey, StringComparison.Ordinal));
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.SkippedRows);
    }

    private static void AssertOriginalBytes(GroupIdentityFixture fixture, Workspace workspace)
    {
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });
        using var bytes = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, bytes);
        bytes.Position = 0;
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        Assert.Equal(fixture.Input.Members.Count, zip.Entries.Count);
        foreach (var member in fixture.Input.Members)
        {
            using var entry = Assert.Single(zip.Entries, entry => entry.FullName == member.File.RelativePath).Open();
            using var output = new MemoryStream();
            entry.CopyTo(output);
            Assert.Equal(member.File.ReadAllBytes(), output.ToArray());
        }
    }

    private static (Workspace Workspace, DocumentSession Session) OpenSession(WorkspaceStore store, GroupIdentityFixture fixture)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var member in fixture.Input.Members)
            {
                using var entry = zip.CreateEntry(member.File.RelativePath).Open();
                entry.Write(member.File.ReadAllBytes());
            }
        var workspace = store.Create("selection", [new ImportSource("fixture.zip", () => new MemoryStream(bytes.ToArray()))]);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "hero.sav");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, fixture.Plugin.Info.Id));
        var session = workspace.OpenGroup(offer.Token).Session;
        _ = session.BuildView();
        return (workspace, session);
    }
}
