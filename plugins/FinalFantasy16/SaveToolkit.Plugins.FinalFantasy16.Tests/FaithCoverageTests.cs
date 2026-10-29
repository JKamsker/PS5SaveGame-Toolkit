using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithCoverageTests
{
    private const string Restore = "restore-imported-main-stacks";
    private readonly FinalFantasy16Plugin _plugin = new();
    private static readonly Dictionary<string, FieldValue> NoParameters = [];
    private static readonly int[] OpeningTargets = [567, 1234];

    [Theory]
    [InlineData("set-gil", 1, "Gil")]
    [InlineData("set-ap", 3, "AP")]
    [InlineData("set-renown", 2, "Renown")]
    public void NamedBalancesPreviewCancelApplyAndUndoRedo(string action, int id, string label)
    {
        var original = Save(id, 1234, 101, 567);
        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("coverage-resource", fixture.File, _plugin, document);
        _ = session.BuildView();
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(42) };
        var cancelled = session.PlanAction(action, parameters);
        Assert.NotNull(cancelled.PlanId);
        Assert.Contains(cancelled.Plan.Changes, change => !change.Derived && change.Label.Contains(label, StringComparison.Ordinal));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        // Dismissing a preview is deliberately mutation-free; a later preview replaces its token.
        var preview = session.PlanAction(action, parameters);
        Assert.True(session.ApplyActionPlan(cancelled.PlanId, cancelled.Revision).Stale);
        preview = session.PlanAction(action, parameters);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        FaithWriteTests.AssertOnlyRanges(original, output, [(32 + sizeof(int), sizeof(int))]);
        _ = FaithContainer.Parse(output);
        Assert.Equal(42, document.GetValue("inventory/0/quantity").AsInt32());
        Assert.Contains(preview.Plan.Changes, change => change.ValueId == "integrity/crc32" && change.Derived);
        Assert.Contains(preview.Plan.Warnings, warning => warning.StartsWith("Not validated on console", StringComparison.Ordinal));
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RestoreUsesOpeningQuantitiesWithDifferentTargetsAndPreservesHigherValues()
    {
        using var fixture = new FaithSessionFile(Save(5, 1234, 101, 567));
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("coverage-restore", fixture.File, _plugin, document);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([Edit(0, 1), Edit(1, 2)]).Ok);
        Assert.True(session.ApplyEdits([Edit(0, 3)]).Ok);
        var reduced = Assert.Single(document.Serialize()).Content.ToArray();
        var scope = new ActionScope(["inventory/1", "inventory/0"]);
        var preview = session.PlanAction(Restore, new Dictionary<string, JsonElement>(), scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(OpeningTargets, preview.Plan.Changes.Where(change => !change.Derived).Select(change => change.NewValue.AsInt32()));
        Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var restored = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(fixture.File.ReadAllBytes(), restored);
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(restored, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyEdits([Edit(0, 1235)]).Ok);
        var planner = (IActionPlanningDocument)document;
        var unchanged = planner.PlanAction(Restore, NoParameters, scope);
        Assert.Empty(unchanged.Changes);
        Assert.Equal(2, unchanged.SkippedRows.Count);
        // Reopening an edited export starts a new baseline.
        using var reopened = _plugin.Open(FaithTestData.File(reduced));
        var fresh = (IActionPlanningDocument)reopened;
        Assert.Empty(fresh.PlanAction(Restore, NoParameters, scope).Changes);
    }

    [Theory]
    [InlineData(1, 1234, 1)] // Currency.
    [InlineData(11, 1234, 1)] // Last Elixir / special consumable.
    [InlineData(506, 1234, 1)] // Upgrade.
    [InlineData(201, 1234, 1)] // Unique/progression material.
    [InlineData(900000, 1234, 1)] // Unmapped.
    [InlineData(5, 0, 1)] // Opening zero is not ownership.
    [InlineData(5, 1234, 0)] // Current zero is not a vacancy or proven active stack.
    [InlineData(5, -1, -1)] // Unknown negative semantics.
    [InlineData(5, 127812, 1)] // Above the conservative F6 ceiling.
    public void RestoreSkipsIneligibleRecords(int id, int imported, int current)
    {
        using var document = _plugin.Open(FaithTestData.File(Save(id, imported, 101, 567)));
        Assert.True(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(current))]).Succeeded || current < 0);
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        var plan = ((IActionPlanningDocument)document).PlanAction(Restore, NoParameters, new ActionScope(["inventory/0"]));
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Single(plan.SkippedRows);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void DuplicateAndChangedIdentitiesAreNeverRestored()
    {
        foreach (var duplicate in new[] { false, true })
        {
            using var document = _plugin.Open(FaithTestData.File(Save(5, 1234, duplicate ? 5 : 101, 567)));
            Assert.True(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(1))]).Succeeded);
            if (!duplicate) Assert.True(document.ApplyEdits([new("inventory/0/item", FieldValue.Of("6"))]).Succeeded);
            var plan = ((IActionPlanningDocument)document).PlanAction(Restore, NoParameters, new ActionScope(["inventory/0"]));
            Assert.Empty(plan.Changes);
            Assert.Single(plan.SkippedRows);
        }
    }

    [Fact]
    public void InvalidScopesParametersAndForgedPlansCannotChangeBytes()
    {
        var original = Save(1, 1234, 1, 567);
        using var document = _plugin.Open(FaithTestData.File(original));
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(42) };
        Assert.NotEmpty(planner.PlanAction("set-gil", parameters, new()).Errors); // Duplicate resource.
        Assert.NotEmpty(planner.PlanAction("set-ap", parameters, new()).Errors); // Missing resource.
        foreach (var keys in new[] { new[] { "secondary/0" }, new[] { "inventory/99" }, new[] { "inventory/0", "inventory/0" } })
            Assert.NotEmpty(planner.PlanAction(Restore, NoParameters, new(keys)).Errors);
        Assert.NotEmpty(planner.PlanAction(Restore, parameters, new()).Errors);
        Assert.Empty(planner.PlanAction(Restore, NoParameters, new([])).Changes);
        var plan = planner.PlanAction(Restore, NoParameters, new());
        Assert.False(planner.ApplyActionPlan(Restore, NoParameters, new(), plan with { Warnings = [] }).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    internal static byte[] Save(int firstId, int firstQuantity, int secondId, int secondQuantity)
    {
        var payload = FaithTestData.Items();
        BinaryPrimitives.WriteInt32LittleEndian(payload, firstId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int)), firstQuantity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), secondId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16 + sizeof(int)), secondQuantity);
        return FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload), (99, 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    private static IncomingEdit Edit(int slot, int value) => new($"inventory/{slot}/quantity", JsonSerializer.SerializeToElement(value));
}
