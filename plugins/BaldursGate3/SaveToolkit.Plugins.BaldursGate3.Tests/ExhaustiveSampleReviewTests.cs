using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

/// <summary>Exhaustive sample walk: no conformance lazy/page/file sampling limits.</summary>
public sealed class ExhaustiveSampleReviewTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryClaimedFileTraversesAllViewsAndPagesAndPreservesHistoryAndBytes()
    {
        var plugin = new BaldursGate3Plugin();
        var files = SampleSaves.Load("PPSA18463").AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            using var document = Measure("open", () => plugin.Open(file));
            using var session = new DocumentSession("exhaustive-review", file, plugin, document);
            var fields = new Dictionary<string, FieldNode>(StringComparer.Ordinal);
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            Measure("full-view", () => { Visit(session.BuildView()); return true; });
            if (document is IQueryableDocument queryable)
                Measure("all-pages", () =>
                {
                    foreach (var collection in queryable.Collections)
                    {
                        var keys = new HashSet<string>(StringComparer.Ordinal);
                        var offset = 0;
                        while (true)
                        {
                            var page = session.QueryCollection(collection.Id,
                                new() { Offset = offset, Limit = 200, IncludeEmpty = true },
                                expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
                            foreach (var row in page.Rows)
                            {
                                Assert.True(keys.Add(row.Key), "Duplicate row identity.");
                                foreach (var cell in row.Cells.Values) Visit(cell);
                            }
                            offset += page.Rows.Count;
                            if (offset >= page.TotalCount) break;
                            Assert.NotEmpty(page.Rows);
                        }
                    }
                    return true;
                });
            AssertBytes(original, Measure("no-op-export", () => Assert.Single(document.Serialize()).Content.ToArray()));
            output.WriteLine($"fields={fields.Count}, editable={fields.Values.Count(field => !field.Spec.ReadOnly)}, lazy={expanded.Count}");
            var field = fields.Values.FirstOrDefault(field => !field.Spec.ReadOnly && field.Spec is IntegerSpec);
            if (field is null) continue;
            var spec = (IntegerSpec)field.Spec;
            var next = FieldValue.Of(field.Value.AsInteger() < spec.Max ? field.Value.AsInteger() + 1 : field.Value.AsInteger() - 1);
            Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(next))],
                expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.NotEmpty(Measure("semantic-review", () => session.GetChanges(TestContext.Current.CancellationToken)).Changes);
            var edited = Measure("edited-export", () => Assert.Single(session.Serialize(TestContext.Current.CancellationToken)).Content.ToArray());
            using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, file.Directory.TitleId));
            // Register reopened bindings before reading the stable edited identities.
            VisitReopened(reopened.BuildView());
            Assert.Equal(next, reopened.GetValue(field.ValueId));
            Assert.True(session.Undo().Ok);
            Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
            AssertBytes(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            AssertBytes(edited, Assert.Single(session.Serialize(TestContext.Current.CancellationToken)).Content.ToArray());

            void Visit(ViewNode node)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                if (node is FieldNode value)
                {
                    Assert.Equal(value.Value, document.GetValue(value.ValueId));
                    fields[value.ValueId] = value;
                }
                IEnumerable<ViewNode> children = node switch
                {
                    SectionNode { LazyId: { } id } when expanded.Add(id) => session.Expand(id),
                    SectionNode { LazyId: null } section => section.Children,
                    TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
                    TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
                    _ => [],
                };
                foreach (var child in children) Visit(child);
            }
            void VisitReopened(ViewNode node)
            {
                IEnumerable<ViewNode> children = node switch
                {
                    SectionNode { LazyId: { } id } => reopened.ExpandNode(id),
                    SectionNode { LazyId: null } section => section.Children,
                    TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
                    TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
                    _ => [],
                };
                foreach (var child in children) VisitReopened(child);
                if (reopened is IQueryableDocument query && node is QueryableTableNode tableNode)
                    _ = query.Query(tableNode.CollectionId, new() { Limit = 200 });
            }
            T Measure<T>(string phase, Func<T> operation)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                var result = operation();
                output.WriteLine($"{file.FileName}: {phase} {watch.Elapsed.TotalMilliseconds:F1} ms; {GC.GetAllocatedBytesForCurrentThread() - allocated} B");
                return result;
            }
        }
    }

    private static void AssertBytes(byte[] expected, byte[] actual) =>
        Assert.True(expected.AsSpan().SequenceEqual(actual), "Serialization changed unintended bytes.");
}
