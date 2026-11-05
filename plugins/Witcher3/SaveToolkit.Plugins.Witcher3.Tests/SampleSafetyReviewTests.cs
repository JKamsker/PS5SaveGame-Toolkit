using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class SampleSafetyReviewTests(ITestOutputHelper output)
{
    [Fact]
    public void SyntheticFullTraversalAndNumericBatchPreserveBytes() => Check(MemorySaveSet.SingleFile("Player.sav", SyntheticPlayerSave.Create(), "PPSA04018"));

    [Fact]
    public void EverySampleFullTraversalAndNumericBatchPreserveBytes()
    {
        var files = EnabledWriteFixture.Files().ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files) Check(file);
        output.WriteLine($"Sample records checked: {files.Length}.");
    }

    private void Check(ISaveFile file)
    {
        var plugin = new Witcher3Plugin();
        var original = file.ReadAllBytes();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        using var document = plugin.Open(file);
        var open = watch.Elapsed;
        var fields = Walk(document);
        var walk = watch.Elapsed - open;
        Assert.True(original.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        var serialize = watch.Elapsed - open - walk;
        var edits = fields.Values.Where(field => !field.Spec.ReadOnly && field.Spec is IntegerSpec)
            .Select(field => (Field: field, Spec: (IntegerSpec)field.Spec))
            .Where(pair => pair.Spec.Min < pair.Spec.Max)
            .Select(pair => new ValueEdit(pair.Field.ValueId, FieldValue.Of(
                pair.Field.Value.AsInteger() > pair.Spec.Min ? pair.Field.Value.AsInteger() - 1 : pair.Field.Value.AsInteger() + 1)))
            .ToArray();
        if (edits.Length > 0)
        {
            var applied = document.ApplyEdits(edits);
            Assert.True(applied.Succeeded, string.Join("; ", applied.Errors.Select(error => error.ValueId + ": " + error.Message)));
            var changed = Assert.Single(document.Serialize()).Content.ToArray();
            var replacement = MemorySaveSet.SingleFile(file.FileName, changed, file.Directory.TitleId, file.Directory.Name);
            using var reopened = plugin.Open(replacement);
            _ = Walk(reopened, expandRaw: false);
            foreach (var edit in edits) Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
            _ = Walk(document, expandRaw: false);
            Assert.True(document.ApplyEdits(edits.Select(edit => new ValueEdit(edit.ValueId, fields[edit.ValueId].Value)).ToArray()).Succeeded);
            Assert.True(original.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        }
        if (file is DiskSaveFile disk && document.CanEdit) CheckSession(disk);
        output.WriteLine($"Open {open.TotalMilliseconds:F1} ms; full walk {walk.TotalMilliseconds:F1} ms; no-op export {serialize.TotalMilliseconds:F1} ms; numeric fields {edits.Length}; total allocation {GC.GetAllocatedBytesForCurrentThread() - allocated:N0} B.");
    }

    private static void CheckSession(DiskSaveFile file)
    {
        var plugin = new Witcher3Plugin();
        using var session = new DocumentSession("sample-review", file, plugin, plugin.Open(file));
        _ = session.BuildView();

        var preview = session.PlanAction("inventory/repair", new Dictionary<string, JsonElement>());
        Assert.Empty(preview.Plan.Errors);
        if (preview.Plan.Changes.Count == 0) return;
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        _ = session.GetChanges(TestContext.Current.CancellationToken);
        var changed = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize(cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(session.Redo().Ok);
        Assert.True(changed.AsSpan().SequenceEqual(Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.Span));
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, changed, file.Directory.TitleId, file.Directory.Name));
        Assert.True(changed.AsSpan().SequenceEqual(Assert.Single(reopened.Serialize()).Content.Span));
    }

    private static Dictionary<string, FieldNode> Walk(ISaveDocument document, bool expandRaw = true)
    {
        var fields = new Dictionary<string, FieldNode>(StringComparer.Ordinal);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<ViewNode>();
        pending.Push(document.BuildView());
        if (document is IQueryableDocument queryable)
            foreach (var collection in queryable.Collections)
                for (var offset = 0; ;)
                {
                    var page = queryable.Query(collection.Id, new() { Offset = offset, Limit = 200, IncludeEmpty = true });
                    foreach (var node in page.Rows.SelectMany(row => row.Cells.Values)) pending.Push(node);
                    if (offset + page.Rows.Count >= page.FilteredCount) break;
                    Assert.NotEmpty(page.Rows);
                    offset += page.Rows.Count;
                }
        while (pending.TryPop(out var node))
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            if (node is FieldNode field)
            {
                Assert.Equal(field.Value, document.GetValue(field.ValueId));
                fields[field.ValueId] = field;
            }
            IEnumerable<ViewNode> children = node switch
            {
                TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
                SectionNode section => section.Children.Concat(section.LazyId is { } id && expanded.Add(id) && (expandRaw || !id.StartsWith("words/", StringComparison.Ordinal)) ? document.ExpandNode(id) : []),
                TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
                _ => [],
            };
            foreach (var child in children) pending.Push(child);
        }
        return fields;
    }
}
