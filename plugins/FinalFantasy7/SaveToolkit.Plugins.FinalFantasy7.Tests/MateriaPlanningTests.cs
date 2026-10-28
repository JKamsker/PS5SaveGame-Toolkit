using System.Buffers.Binary;
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

public sealed class MateriaPlanningTests
{
    [Theory]
    [InlineData("PPSA01911", 10004)]
    [InlineData("PPSA08664", 10020)]
    public void F6QueryPlanIsPureAndApplyUndoRedoHaveExactF1ByteScope(string title, uint type)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var plugin = new FinalFantasy7Plugin();
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        var original = file.ReadAllBytes();
        var context = new ReferencePackStore([plugin]).Pin(plugin, title);
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("ff7", file, plugin, document, references: context);
        _ = session.BuildView();
        var initialAp = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(schema.IsRemake ? 0x1848 : 0xBFA70));
        var parameters = Parameters(type.ToString(), initialAp + 1);
        var scope = new ActionScope
        {
            QueryScope = new("materia", new()
            {
                Filters = new Dictionary<string, string> { ["id"] = $"0x{type:X8}" }, Limit = 1,
            }, session.State.Revision),
        };
        var preview = session.PlanAction(MateriaPlanning.ActionId, parameters, scope);
        Assert.NotNull(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Changes);
        if (!schema.IsRemake)
        {
            Assert.True(preview.Scope.RowKeys!.Count > 1); // The client requested one row per page, not one target.
            Assert.True(preview.Plan.Changes.Count > 1);
        }
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.InvokeAction(MateriaPlanning.ActionId, parameters).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        var ranges = preview.Plan.Changes.Select(change =>
        {
            var slot = int.Parse(change.ValueId.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture);
            return (schema.IsRemake ? 0x1848 : 0xBFA70) + slot * 32;
        }).ToArray();
        var diff = ByteDiffer.Compare(original, edited, options: new() { MergeGap = 0, MaxRanges = 2000 });
        Assert.True(diff.ChangedBytes > 0);
        Assert.All(diff.Ranges, range => Assert.Contains(ranges, start => range.Offset >= start && range.End <= start + 4));
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, title));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(reopened);
        foreach (var change in preview.Plan.Changes)
        {
            var slot = change.ValueId.Split('/')[1];
            _ = queryable.Query("materia", new() { Filters = new Dictionary<string, string> { ["slot"] = slot } });
            Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId));
        }

        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo); // The complete apply is one history step.
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PlanningSkipsTierCrossingsForeignRowsAndEmptySelection()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var values = new Dictionary<string, FieldValue> { ["type"] = FieldValue.Of("10020"), ["ap"] = FieldValue.Of(0L) };
        var plan = planner.PlanAction(MateriaPlanning.ActionId, values, new(["materia/0", "inventory/0", "materia/1000"]));
        Assert.Empty(plan.Changes);
        Assert.Equal(3, plan.SkippedRows.Count);
        Assert.Contains(plan.SkippedRows, row => row.Reason.Contains("star tier", StringComparison.Ordinal));
        Assert.Empty(planner.PlanAction(MateriaPlanning.ActionId, values, new([])).Changes);
        Assert.NotEmpty(planner.PlanAction(MateriaPlanning.ActionId, values, new()
        {
            QueryScope = new("materia", new(), 0),
        }).Errors);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void DirectEditsRejectStarCrossingsAndSummonsAtomically()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth009");
        var bytes = file.ReadAllBytes();
        using var document = new FinalFantasy7Plugin().Open(file);
        var rows = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("materia/0"))).Rows;
        var titan = rows.Single(row => ((FieldNode)row.Cells["id"]).Value.AsUInt32() == 14011);
        Assert.True(((FieldNode)titan.Cells["quantity"]).Spec.ReadOnly);
        var ordinary = rows.First(row => row.Cells["quantity"] is FieldNode { Spec.ReadOnly: false } ap &&
            ((IntegerSpec)ap.Spec).Min > 0);
        var apField = (FieldNode)ordinary.Cells["quantity"];
        var reject = document.ApplyEdits([new(apField.ValueId, FieldValue.Of(((IntegerSpec)apField.Spec).Min - 1))]);
        Assert.False(reject.Succeeded);
        reject = document.ApplyEdits([new(apField.ValueId, apField.Value),
            new(((FieldNode)titan.Cells["quantity"]).ValueId, FieldValue.Of(1L))]);
        Assert.False(reject.Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CoreRejectsStalePreviewAndReportsOriginalChanges()
    {
        var plugin = new FinalFantasy7Plugin();
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08664");
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("ff7", file, plugin, document, references: context);
        _ = session.BuildView();
        _ = session.QueryCollection("materia", new() { Filters = new Dictionary<string, string> { ["slot"] = "0" } });
        var preview = session.PlanAction(MateriaPlanning.ActionId, Parameters("10020", 301), new(["materia/0"]));
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyEdits([new("materia/0/ap", JsonSerializer.SerializeToElement("302"))]).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
        var changes = session.GetChanges();
        Assert.Contains(changes.Changes, change => change.ValueId == "materia/0/ap" && change.OriginalValue.AsUInt32() == 300);
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static Dictionary<string, JsonElement> Parameters(string type, uint ap) => new()
    {
        ["type"] = JsonSerializer.SerializeToElement(type), ["ap"] = JsonSerializer.SerializeToElement(ap.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    };
}
