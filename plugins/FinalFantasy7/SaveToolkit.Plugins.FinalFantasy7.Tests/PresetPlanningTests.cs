using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class PresetPlanningTests
{
    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void RestockPreviewIsPureScopedReversibleAndOnlyPatchesSelectedCountWords(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        var plugin = new FinalFantasy7Plugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("restock", file, plugin, document);
        _ = session.BuildView();
        var original = file.ReadAllBytes();
        var scope = new ActionScope
        {
            QueryScope = new("inventory", new() { Filters = new Dictionary<string, string> { ["category"] = "Items" } }, session.State.Revision),
        };
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(99) };
        var preview = session.PlanAction(InventoryPlanning.ActionId, parameters, scope);
        Assert.NotNull(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Changes);
        Assert.Equal(session.QueryCollection("inventory", new() { Filters = new Dictionary<string, string> { ["category"] = "Items" } }).FilteredCount,
            preview.Plan.Changes.Count + preview.Plan.SkippedRows.Count);
        Assert.Contains(preview.Plan.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.All(preview.Plan.Changes, change => Assert.True(change.NewValue.AsInt32() > change.OldValue.AsInt32()));
        var result = session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        var layout = GameplayLayout.For(schema);
        var offsets = preview.Plan.Changes.Select(change => int.Parse(change.ValueId.Split('/')[1], CultureInfo.InvariantCulture))
            .Select(slot => layout.InventoryOffset + slot * layout.InventoryStride + GameplayLayout.QuantityDelta).ToArray();
        Assert.All(ByteDiffer.Compare(original, edited, options: new() { MergeGap = 0 }).Ranges,
            range => Assert.Contains(offsets, offset => range.Offset >= offset && range.End <= offset + sizeof(uint)));
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, title));
        foreach (var change in preview.Plan.Changes)
        {
            _ = reopened.ExpandNode($"inventory/{int.Parse(change.ValueId.Split('/')[1], CultureInfo.InvariantCulture) / GameplayFields.RowsPerPage}");
            Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId));
        }
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo); // Entire query-scoped operation is one history entry.
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void StarPresetUsesPickerDerivedApAndOneUndoGroup(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        var plugin = new FinalFantasy7Plugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, title);
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("star-preset", file, plugin, document, references: context);
        _ = session.BuildView();
        var row = session.QueryCollection("materia", new() { Limit = 200 }).Rows
            .First(row => row.Cells["level"] is FieldNode { Spec.ReadOnly: false, Value: var value } && value.AsInt32() > 1);
        var type = ((FieldNode)row.Cells["id"]).Value.AsString();
        Assert.True(session.FindReference("type", type, actionId: MateriaPlanning.StarActionId).Eligibility.Selectable);
        var preview = session.PlanAction(MateriaPlanning.StarActionId, new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement(type), ["stars"] = JsonSerializer.SerializeToElement(1),
        }, new()
        {
            QueryScope = new("materia", new() { Filters = new Dictionary<string, string> { ["id"] = $"0x{uint.Parse(type, CultureInfo.InvariantCulture):X8}" } }, session.State.Revision),
        });
        Assert.NotNull(preview.PlanId);
        Assert.Contains(preview.Plan.Changes, change => change.ValueId.EndsWith("/level", StringComparison.Ordinal) && !change.Derived);
        Assert.Contains(preview.Plan.Changes, change => change.ValueId.EndsWith("/ap", StringComparison.Ordinal) && change.Derived);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, title));
        foreach (var slot in preview.Plan.Changes.Select(change => change.ValueId.Split('/')[1]).Distinct(StringComparer.Ordinal))
            _ = ((IQueryableDocument)reopened).Query("materia", new() { Filters = new Dictionary<string, string> { ["slot"] = slot } });
        Assert.All(preview.Plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
        var ranges = preview.Plan.Changes.Select(change =>
        {
            var slot = int.Parse(change.ValueId.Split('/')[1], CultureInfo.InvariantCulture);
            return change.ValueId.EndsWith("/ap", StringComparison.Ordinal)
                ? (Offset: (schema.IsRemake ? 0x1848 : 0xBFA70) + slot * 32, Length: 4)
                : (Offset: (schema.IsRemake ? 0x1840 : 0xBFA78) + slot * 32, Length: 1);
        }).ToArray();
        Assert.All(ByteDiffer.Compare(file.ReadAllBytes(), edited, options: new() { MergeGap = 0 }).Ranges,
            range => Assert.Contains(ranges, allowed => range.Offset >= allowed.Offset && range.End <= allowed.Offset + allowed.Length));
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void InvalidParametersForeignScopesAndTamperedPlansNeverMutate()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file);
        _ = document.BuildView();
        var planner = (IActionPlanningDocument)document;
        foreach (var value in new[] { FieldValue.Of(-1L), FieldValue.Of(0L), FieldValue.Of(100L), FieldValue.Of("no"), FieldValue.Of(true) })
            Assert.NotEmpty(planner.PlanAction(InventoryPlanning.ActionId, new Dictionary<string, FieldValue> { ["quantity"] = value }, new()).Errors);
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(99L) };
        Assert.NotEmpty(planner.PlanAction(InventoryPlanning.ActionId, parameters, new() { QueryScope = new("materia", new(), 0) }).Errors);
        var plan = planner.PlanAction(InventoryPlanning.ActionId, parameters, new());
        Assert.Contains(plan.SkippedRows, row => row.Reason == "Not a mapped ordinary consumable.");
        Assert.False(planner.ApplyActionPlan(InventoryPlanning.ActionId, parameters, new(), plan with { Changes = [] }).Succeeded);
        var stars = new Dictionary<string, FieldValue> { ["type"] = FieldValue.Of("10020"), ["stars"] = FieldValue.Of(255L) };
        Assert.NotEmpty(planner.PlanAction(MateriaPlanning.StarActionId, stars, new()).Errors);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
