using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SampleLiveNewAgeTests
{
    [Fact]
    public void Ps5RuntimeCensusAndSpellGraphsMatchIndependentlyMeasuredStorage()
    {
        var source = Source(Save().ReadAllBytes());
        Assert.Empty(source.Diagnostics);
        Assert.Equal(199, source.Owners.Count);
        Assert.Equal(38, source.Stacks.Select(s => s.GroupRow).Distinct().Count());
        Assert.Equal(39, source.Stacks.Count);
        Assert.Equal(638, source.Slots.Count);
        Assert.Equal(239, source.Slots.Select(s => s.ContainerRow).Distinct().Count());
        Assert.Equal(760, source.Numbers.Count(n => n.Family == "Health"));
        Assert.Equal(1536, source.Numbers.Count(n => n.Family == "Stats"));
        Assert.Equal(500, source.Stacks.Max(s => s.Original));
        Assert.Contains(source.Stacks, stack => source.EntityLabel(stack.EntityRow).Contains("OBJ_", StringComparison.Ordinal));
        var books = LsmfSpellBooks.Read(source.Access);
        Assert.Equal(291, books.Count);
        Assert.Equal(2242, books.Sum(b => b.Spells.Count));
        using var document = new BaldursGate3Plugin().Open(Save());
        var view = document.ExpandNode(source.Prefix);
        Assert.Equal(7, view.OfType<QueryableTableNode>().Count());
        Assert.Equal(256, source.Classes.Count);
        var book = books.First(b => b.Spells.Count > 0);
        var graph = Assert.Single(document.ExpandNode(source.Prefix + $"/book/{book.Row}").OfType<GraphNode>());
        SpatialViewValidation.Validate(graph);
        Assert.Empty(graph.Edges);
        Assert.Equal(book.Spells.Count, graph.Items.Count);
        Assert.All(graph.Items, item =>
        {
            Assert.Equal(GraphState.Unknown, item.State);
            Assert.NotNull(item.Target);
            Assert.Equal(item.Label, document.GetValue(item.Target.Id).AsString());
        });
    }

    [Theory]
    [InlineData("Stacks")]
    [InlineData("Health")]
    [InlineData("Stats")]
    public void RuntimeScalarFamiliesPersistWithOnlyIntendedByteChangesAndSessionHistory(string family)
    {
        var file = Save();
        var original = file.ReadAllBytes();
        var source = Source(original);
        var number = source.Numbers.First(n => n.Family == family && n.Original > n.Min);
        var plugin = new BaldursGate3Plugin();
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(file));
        using var session = new DocumentSession("live-values", file, plugin, document);
        _ = session.BuildView();
        var page = session.QueryCollection(source.Prefix + "/values", new QueryableCollectionQuery { Text = family, Limit = 128 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var field = Assert.IsType<FieldNode>(page.Rows.First(row => row.Key == source.Id(number)).Cells["value"]);
        Assert.False(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Unknown, field.Confidence);
        Assert.Contains("Integrity unknown", field.Warning, StringComparison.Ordinal);
        var next = field.Value.AsInt64() - 1;
        Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(number.Max + 1))]).Succeeded);
        Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(next)),
            new ValueEdit("missing", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var numbers = family == "Stats" ? source.Numbers.Where(n => n.Family == "Stats").Take(6).ToArray() : [number];
        Assert.True(session.ApplyEdits(numbers.Select(n => new IncomingEdit(source.Id(n),
            JsonSerializer.SerializeToElement(n.Original - 1))).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        var after = Source(edited);
        Assert.Equal(next, after.Numbers.Single(n => n.Key == number.Key).Original);
        Assert.All(numbers, n => Assert.Equal(n.Original - 1, after.Numbers.Single(changed => changed.Key == n.Key).Original));
        OnlyIntendedBytes(original, edited, numbers.SelectMany(n => n.Offsets).SelectMany(offset => Enumerable.Range(offset, 4)).ToHashSet());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
    }

    [Fact]
    public void StructuralMapKeySwapPlansTwoRowsAndUndoesAlongsideAnExistingScalarEdit()
    {
        var file = Save();
        var original = file.ReadAllBytes();
        var source = Source(original);
        var slots = source.Slots.GroupBy(slot => slot.ContainerRow).First(group => group.Count() >= 2).Take(2).ToArray();
        var plugin = new BaldursGate3Plugin();
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(file));
        using var session = new DocumentSession("live-slots", file, plugin, document);
        _ = session.BuildView();
        var index = source.Slots.ToList().IndexOf(slots[0]);
        var rows = session.QueryCollection(source.Prefix + "/slots", new QueryableCollectionQuery { Offset = index, Limit = 2 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows;
        var quantity = source.Numbers.First(n => n.Family == "Stacks" && n.Original > n.Min);
        _ = session.QueryCollection(source.Prefix + "/values", new QueryableCollectionQuery { Text = "Stacks", Limit = 128 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([new IncomingEdit(source.Id(quantity), JsonSerializer.SerializeToElement(quantity.Original - 1))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var scalarOnly = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        var preview = session.PlanAction(LsmfLiveActions.Swap, new Dictionary<string, JsonElement>(), new ActionScope(rows.Select(r => r.Key).ToArray()), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.NotEmpty(preview.Plan.Warnings);
        Assert.Equal(scalarOnly, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var swapped = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        OnlyIntendedBytes(scalarOnly, swapped, slots.SelectMany(s => Enumerable.Range(s.KeyOffset, 2)).ToHashSet());
        Assert.True(session.Undo().Ok);
        Assert.Equal(scalarOnly, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(swapped, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void QueryScopedQuantityPresetCoversUnloadedRowsAndAppliesAsOneUndoGroup()
    {
        var file = Save();
        var plugin = new BaldursGate3Plugin();
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(file));
        using var session = new DocumentSession("live-restore", file, plugin, document);
        _ = session.BuildView();
        var source = Source(file.ReadAllBytes());
        _ = session.Expand(source.Prefix);
        var page = session.QueryCollection(source.Prefix + "/values", new QueryableCollectionQuery { Text = "Stacks", Limit = 128 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var fields = page.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["value"])).Where(field => field.Value.AsInteger() > Assert.IsType<IntegerSpec>(field.Spec).Min).Take(2).ToArray();
        Assert.True(session.ApplyEdits(fields.Select(field => new IncomingEdit(field.ValueId,
            JsonSerializer.SerializeToElement(field.Value.AsInt64() - 1))).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var lowered = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        var query = new ActionQueryScope(source.Prefix + "/values", new QueryableCollectionQuery { Text = "Stacks", Limit = 1 }, session.State.Revision)
        { ExcludedRowKeys = [fields[1].ValueId] };
        var preview = session.PlanAction(LsmfLiveActions.Restore, new Dictionary<string, JsonElement>(), new ActionScope { QueryScope = query }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(38, preview.Scope.RowKeys!.Count);
        Assert.Equal(fields[0].ValueId, Assert.Single(preview.Plan.Changes).ValueId);
        Assert.Equal(37, preview.Plan.SkippedRows.Count);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(fields[0].Value, document.GetValue(fields[0].ValueId));
        Assert.True(session.Undo().Ok);
        Assert.Equal(lowered, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(fields[0].Value, document.GetValue(fields[0].ValueId));
    }

    internal static DiskSaveFile Save() => Assert.IsType<DiskSaveFile>(SampleSaves.Load("PPSA18463").AllDiskFiles.First(file =>
        !file.Directory.IsBackup && file.FileName == "AutoSave_0.lsv"));

    internal static LsmfLiveSource Source(byte[] bytes)
    {
        var package = LspkPackage.Read(bytes);
        var entry = package.Entries.ToList().FindIndex(e => e.Name == "Globals.lsf");
        var resource = LsfReader.Read(package.Extract(package.Entries[entry]));
        var attribute = resource.Nodes.Single(n => n.Name == "NewAge").Attributes.Single();
        return new LsmfLiveSource(entry, attribute, resource.Attributes[attribute].Data, resource);
    }

    private static void OnlyIntendedBytes(byte[] before, byte[] after, HashSet<int> allowed)
    {
        var original = Source(before).Access.Model.Bytes;
        var changed = Source(after).Access.Model.Bytes;
        var diff = ByteDiffer.Compare(original.ToArray(), changed.ToArray(), options: new ByteDiffOptions { MergeGap = 0, ContextBytes = 0 });
        Assert.NotEmpty(diff.Ranges);
        Assert.All(diff.Ranges.SelectMany(range => Enumerable.Range((int)range.Offset, (int)range.Length)), offset => Assert.Contains(offset, allowed));
        var oldPackage = LspkPackage.Read(before);
        var newPackage = LspkPackage.Read(after);
        var oldGlobals = LsfReader.Read(oldPackage.Extract(oldPackage.Entries.Single(entry => entry.Name == "Globals.lsf")));
        var newGlobals = LsfReader.Read(newPackage.Extract(newPackage.Entries.Single(entry => entry.Name == "Globals.lsf")));
        Assert.Equal(oldGlobals.Attributes.Count, newGlobals.Attributes.Count);
        for (var index = 0; index < oldGlobals.Attributes.Count; index++)
        {
            if (oldGlobals.Attributes[index].Name == "NewAge") continue;
            Assert.True(oldGlobals.Attributes[index].Data.Span.SequenceEqual(newGlobals.Attributes[index].Data.Span));
        }
        foreach (var entry in oldPackage.Entries.Where(entry => entry.Name != "Globals.lsf"))
        {
            var next = newPackage.Entries.Single(e => e.Name == entry.Name);
            Assert.Equal(oldPackage.Extract(entry), newPackage.Extract(next));
            Assert.Equal(before.AsSpan(entry.Offset, entry.StoredSize).ToArray(), after.AsSpan(next.Offset, next.StoredSize).ToArray());
        }
    }
}
