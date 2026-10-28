using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokPlanningTests
{
    private const string ActionId = RagnarokWrites.RestoreActionId;

    [Theory]
    [InlineData("time")]
    [InlineData("currency")]
    [InlineData("xp")]
    [InlineData("raven")]
    [InlineData("unknown")]
    public void ScalarWritesPersistWithCoreUndoRedoAndUnrelatedBytesUnchanged(string kind)
    {
        var file = RagnarokWriteTests.Sample();
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = (RagnarokDocument)plugin.Open(file);
        using var session = new DocumentSession(kind, file, plugin, document);
        var root = session.BuildView();
        FieldNode field;
        if (kind == "time")
            field = Assert.Single(RagnarokFormatTests.Flatten(root).OfType<FieldNode>(), node => node.ValueId == "metadata/play-seconds");
        else
        {
            var query = kind switch
            {
                "currency" => new QueryableCollectionQuery { Text = "Hacksilver" },
                "xp" => new() { Text = "Kratos XP" },
                "raven" => new() { Text = "Raven" },
                "unknown" => new() { Filters = new Dictionary<string, string> { ["category"] = "Unknown" }, SortColumn = "quantity", Descending = true },
                _ => new() { SortColumn = "quantity", Descending = true },
            };
            var page = session.QueryCollection("resources", query with { Limit = 200 });
            field = page.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"])).First(node => node.Value.AsInteger() > 0);
        }

        var original = file.ReadAllBytes();
        var result = session.ApplyEdits([Incoming(field.ValueId, field.Value.AsInteger() - 1)]);
        Assert.True(result.Ok);
        Assert.Contains(RagnarokWrites.IntegrityWarning, result.Warnings);
        var changed = Assert.Single(session.Serialize()).Content.ToArray();
        var offset = kind == "time" ? RagnarokFormat.PlaySecondsOffset : CounterOffset(original, field.ValueId);
        RagnarokWriteTests.AssertOnlyRangesChanged(original, changed, (offset, sizeof(uint)));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, Assert.Single(session.Serialize()).Content.ToArray());
        using var reopened = (RagnarokDocument)plugin.Open(SaveToolkit.Testing.MemorySaveSet.SingleFile(file.FileName, changed, "PPSA08330"));
        Assert.Equal(field.Value.AsInteger() - 1, reopened.CounterField(offset).Value.AsInteger());
    }

    [Fact]
    public void NonHeroContainerWriterUsesDecodedOffsetAndSupportsCoreHistory()
    {
        var file = RagnarokWriteTests.Sample();
        var original = file.ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(original, false);
        var offset = index.Nested.Wallets[1].ResourcesOffset + sizeof(ulong);
        // The imported non-hero counters in this fixture are zero. Simulate a positive existing
        // counter for software coverage only; this establishes no console or gameplay evidence.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(offset), 2);
        var fixture = SaveToolkit.Testing.MemorySaveSet.SingleFile(file.FileName, original, "PPSA08330");
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = (RagnarokDocument)plugin.Open(fixture);
        using var session = new DocumentSession("non-hero", file, plugin, document, openOriginal: () => plugin.Open(fixture));
        _ = session.BuildView();
        var field = document.CounterField(offset);
        // Register the cell with Core through the same paged query the UI uses.
        _ = session.QueryCollection("resources", new() { SortColumn = "quantity", Descending = true, Limit = 200 });
        Assert.True(session.ApplyEdits([Incoming(field.ValueId, 1)]).Ok);
        var changed = Assert.Single(session.Serialize()).Content.ToArray();
        RagnarokWriteTests.AssertOnlyRangesChanged(original, changed, (offset, sizeof(uint)));
        using var reopened = (RagnarokDocument)plugin.Open(SaveToolkit.Testing.MemorySaveSet.SingleFile(file.FileName, changed, "PPSA08330"));
        Assert.Equal(1, reopened.CounterField(offset).Value.AsInt32());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, Assert.Single(session.Serialize()).Content.ToArray());
    }

    [Fact]
    public void F6PreviewSelectionWarningsSkippedRowsAndGroupedUndoWorkEndToEnd()
    {
        var file = RagnarokWriteTests.Sample();
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = (RagnarokDocument)plugin.Open(file);
        using var session = new DocumentSession("bulk", file, plugin, document);
        _ = session.BuildView();
        var rows = session.QueryCollection("resources", new() { SortColumn = "quantity", Descending = true, Limit = 200 }).Rows;
        var selected = rows.Take(3).ToArray();
        var fields = selected.Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"])).ToArray();
        Assert.All(fields, field => Assert.True(field.Value.AsInteger() > 0));
        Assert.All(selected, row => Assert.True(Assert.IsType<ActionNode>(row.Cells["restore"]).SupportsPlanning));
        Assert.True(session.ApplyEdits(fields.Select(field => Incoming(field.ValueId, field.Value.AsInteger() - 1)).ToArray()).Ok);
        var reduced = Assert.Single(session.Serialize()).Content.ToArray();
        Assert.False(session.InvokeAction(ActionId, new Dictionary<string, JsonElement>()).Ok);

        var scope = new ActionScope([selected[0].Key, selected[1].Key, rows[^1].Key]);
        var preview = session.PlanAction(ActionId, new Dictionary<string, JsonElement>(), scope, session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Single(preview.Plan.SkippedRows);
        Assert.All(preview.Plan.Changes, change => Assert.False(change.Derived));
        Assert.Contains(RagnarokWrites.IntegrityWarning, preview.Plan.Warnings);
        Assert.Equal(reduced, Assert.Single(session.Serialize()).Content.ToArray());
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        Assert.True(applied.Ok);
        Assert.Equal(2, applied.Changes.Count);
        Assert.Contains(RagnarokWrites.IntegrityWarning, applied.Warnings);
        var restoredSelection = Assert.Single(session.Serialize()).Content.ToArray();
        RagnarokWriteTests.AssertOnlyRangesChanged(reduced, restoredSelection,
            (CounterOffset(file.ReadAllBytes(), fields[0].ValueId), sizeof(uint)),
            (CounterOffset(file.ReadAllBytes(), fields[1].ValueId), sizeof(uint)));
        using var reopened = plugin.Open(SaveToolkit.Testing.MemorySaveSet.SingleFile(file.FileName, restoredSelection, "PPSA08330"));
        var reopenedRows = ((IQueryableDocument)reopened).Query("resources", new() { SortColumn = "quantity", Descending = true, Limit = 200 }).Rows;
        foreach (var restored in fields.Take(2))
            Assert.Contains(reopenedRows, row => row.Cells["quantity"] is FieldNode cell && cell.ValueId == restored.ValueId && cell.Value == restored.Value);
        Assert.Equal(fields[0].Value, document.GetValue(fields[0].ValueId));
        Assert.Equal(fields[1].Value, document.GetValue(fields[1].ValueId));
        Assert.Equal(fields[2].Value.AsInteger() - 1, document.GetValue(fields[2].ValueId).AsInteger());
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Assert.Single(session.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(restoredSelection, Assert.Single(session.Serialize()).Content.ToArray());

        var all = session.PlanAction(ActionId, new Dictionary<string, JsonElement>());
        Assert.Single(all.Plan.Changes);
        Assert.True(session.ApplyActionPlan(all.PlanId!, all.Revision).Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void QueryScopeUsesResolvedRowsAndInvalidatesStalePlans()
    {
        var file = RagnarokWriteTests.Sample();
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("query", file, plugin, document);
        _ = session.BuildView();
        var row = session.QueryCollection("resources", new() { Text = "Hacksilver", Limit = 200 }).Rows[0];
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.True(session.ApplyEdits([Incoming(field.ValueId, field.Value.AsInteger() - 1)]).Ok);
        var scope = new ActionScope { QueryScope = new("resources", new() { Text = "Hacksilver" }, session.State.Revision) };
        var preview = session.PlanAction(ActionId, new Dictionary<string, JsonElement>(), scope);
        Assert.NotNull(preview.PlanId);
        Assert.Single(preview.Plan.Changes);
        Assert.True(session.ApplyEdits([Incoming(field.ValueId, field.Value.AsInteger() - 2)]).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        scope = scope with { QueryScope = scope.QueryScope! with { Revision = session.State.Revision } };
        preview = session.PlanAction(ActionId, new Dictionary<string, JsonElement>(), scope);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EmptyInvalidAndTamperedPlansCannotApplyUnreviewedChanges()
    {
        var file = RagnarokWriteTests.Sample();
        using var document = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(file);
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue>();
        var empty = planner.PlanAction(ActionId, parameters, new([]));
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new(["missing"])).Errors);
        Assert.NotEmpty(planner.PlanAction("missing", parameters, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["unknown"] = FieldValue.Of(1L) }, new()).Errors);
        var row = ((IQueryableDocument)document).Query("resources", new() { Text = "Hacksilver" }).Rows[0];
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(field.Value.AsInteger() - 1))]).Succeeded);
        var bytes = Assert.Single(document.Serialize()).Content.ToArray();
        var scope = new ActionScope([row.Key]);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new([row.Key, row.Key])).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new() { QueryScope = new("resources", new(), 0) }).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, scope with { QueryScope = new("flags", new(), 0) }).Errors);
        var plan = planner.PlanAction(ActionId, parameters, scope);
        foreach (var tampered in new[] { plan with { Warnings = [] }, plan with { SkippedRows = [new("fake", "fake")] },
            plan with { Changes = [plan.Changes[0] with { NewValue = FieldValue.Of(0L) }] } })
        {
            Assert.False(planner.ApplyActionPlan(ActionId, parameters, scope, tampered).Succeeded);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }

        Assert.Empty(document.InvokeAction(ActionId, parameters).Changes);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static IncomingEdit Incoming(string id, Int128 value) => new(id, JsonSerializer.SerializeToElement(value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static int CounterOffset(byte[] bytes, string id)
    {
        var model = new RagnarokWrites(bytes, RagnarokRecordIndex.Parse(bytes, false),
            new RagnarokReferences(SaveToolkit.Sdk.References.IReferenceContext.Empty));
        return Assert.Single(model.Counters, counter => counter.ValueId == id).Offset;
    }
}
