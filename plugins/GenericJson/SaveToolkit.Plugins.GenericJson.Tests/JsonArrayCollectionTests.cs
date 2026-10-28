using System.Diagnostics;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonArrayCollectionTests(ITestOutputHelper output)
{
    [Fact]
    public void ArrayPagesAreDirectStableLiveAndSupportWholeQueryReset()
    {
        var source = "[" + string.Join(',', Enumerable.Range(0, 401)) + "]";
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Session.BuildView();
        var query = Assert.IsAssignableFrom<IQueryableDocument>(fixture.Document);
        Assert.Equal(401, query.Collections.Single(c => c.Id == "json-array-0").TotalCount);
        var page = fixture.Session.QueryCollection("json-array-0", new() { Offset = 198, Limit = 3 });
        Assert.Equal(["value/199", "value/200", "value/201"], page.Rows.Select(r => r.Key));
        Assert.True(fixture.Edit("value/200", JsonEditingTests.Number("1.234567890123456789e-99")).Ok);
        _ = fixture.Session.QueryCollection("json-array-0", new() { Offset = 399, Limit = 1 });
        Assert.True(fixture.Edit("value/400", JsonEditingTests.Number("-0")).Ok);
        var scope = new ActionScope
        {
            QueryScope = new("json-array-0", new() { Filters = new Dictionary<string, string> { ["state"] = "modified" } }, fixture.Session.State.Revision)
                { ExcludedRowKeys = ["value/400"] },
        };
        var plan = fixture.Session.PlanAction("reset-imported", JsonArrayEditingTests.Parameters(), scope, fixture.Session.State.Revision);
        Assert.NotNull(plan.PlanId);
        Assert.Equal("value/200", Assert.Single(plan.Plan.Changes).ValueId);
        var beforeReset = fixture.Bytes;
        Assert.True(fixture.Session.ApplyActionPlan(plan.PlanId, plan.Revision).Ok);
        Assert.Equal(JsonEditingTests.Number("199"), fixture.Document.GetValue("value/200"));
        Assert.Equal(JsonEditingTests.Number("-0"), fixture.Document.GetValue("value/400"));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(beforeReset, fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Single(fixture.Session.QueryCollection("json-array-0", new() { Text = "399" }).Rows);
    }

    [Fact]
    public void NestedArrayCollectionRetiresOnRemovalAndReturnsWithUndo()
    {
        var source = "{\"items\":[" + string.Join(',', Enumerable.Repeat("null", 101)) + "]}";
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "object/0/remove", ("at", FieldValue.Of(0L)));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(fixture.Document);
        Assert.DoesNotContain(query.Collections, c => c.Id == "json-array-1");
        Assert.Throws<KeyNotFoundException>(() => query.Query("json-array-1", new()));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(101, query.Query("json-array-1", new()).TotalCount);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Fact]
    public void MaximumNodeArrayHasBoundedViewsPagesAndExactRoundTrip()
    {
        const int Entries = 249_999;
        var source = Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat("-0.12345678901234567890e-999", Entries)) + "]");
        var timer = Stopwatch.StartNew();
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("large.json", source));
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var raw = Assert.IsType<SectionNode>(Assert.Single(view.Tabs.Single(t => t.Title == "JSON").Children));
        Assert.Contains(raw.Children, n => n is QueryableTableNode);
        var entries = raw.Children.OfType<SectionNode>().Single(n => n.LazyId is not null);
        Assert.InRange(document.ExpandNode(entries.LazyId!).Count, 1, 128);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = query.Query("json-array-0", new() { Offset = Entries - 1, Limit = int.MaxValue });
        Assert.Single(page.Rows);
        Assert.Equal(Entries, page.TotalCount);
        Assert.Equal(source, document.Serialize()[0].Content.ToArray());
        output.WriteLine($"Maximum-node array ({source.Length} bytes): open, bounded view/page and verified export in {timer.ElapsedMilliseconds} ms. No timing assertion.");
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void DepthBoundaryAndTruncationFailClearly(int depth, bool valid)
    {
        var source = new string('[', depth) + "1e-999999" + new string(']', depth);
        var plugin = new GenericJsonPlugin();
        var file = MemorySaveSet.SingleFile("deep.json", Encoding.UTF8.GetBytes(source));
        Assert.Equal(valid, plugin.Probe(file).IsMatch);
        if (valid)
        {
            using var document = plugin.Open(file);
            Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
        }
        else Assert.Throws<SaveFormatException>(() => plugin.Open(file));
        Assert.Throws<SaveFormatException>(() => plugin.Open(MemorySaveSet.SingleFile("truncated.json", Encoding.UTF8.GetBytes(source[..^1]))));
    }
}
