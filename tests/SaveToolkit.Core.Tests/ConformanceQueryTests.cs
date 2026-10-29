using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using Xunit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class ConformanceQueryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void QueryOnlyFields_AreWalkedAcrossPages_AndAvailableForEdits(bool cursor, bool lazy)
    {
        using var document = new ConformanceQueryDocument(cursor, lazy);
        var suite = new Conformance();

        var fields = suite.Inspect(document);

        Assert.Equal(102, fields.Count);
        Assert.Equal("item/101", fields[^1].ValueId);
        Assert.Equal(3, document.Queries.Count);
        Assert.All(document.Queries, query =>
        {
            Assert.True(query.IncludeEmpty);
            Assert.Equal(QueryableCollectionLimits.DefaultPageSize, query.Limit);
        });
        var edit = Assert.Single(suite.Edits(document, fields));
        Assert.Equal("item/0", edit.ValueId);
        Assert.Equal(FieldValue.Of(1L), edit.Value);
        Assert.True(document.ApplyEdits([edit]).Succeeded);
        Assert.Equal(edit.Value, document.GetValue(edit.ValueId));
    }

    [Fact]
    public void DuplicateCollectionViews_AreQueriedOnce()
    {
        using var document = new ConformanceQueryDocument { DuplicateTable = true };

        Assert.Equal(102, new Conformance().Inspect(document).Count);
        Assert.Equal(3, document.Queries.Count);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("duplicate-row")]
    [InlineData("unknown-column")]
    [InlineData("stalled")]
    [InlineData("cursor")]
    public void MalformedPages_AreRejected(string failure)
    {
        using var document = new ConformanceQueryDocument(cursor: true)
        {
            TransformPage = page => failure switch
            {
                "metadata" => page with { CollectionId = "wrong" },
                "duplicate-row" when page.Offset > 0 => page with { Rows = [page.Rows[0] with { Key = "0" }, .. page.Rows.Skip(1)] },
                "unknown-column" => page with { Rows = [new("bad", new Dictionary<string, ViewNode> { ["unknown"] = new TextNode("bad") })] },
                "stalled" => page with { Rows = [] },
                "cursor" => page with { NextCursor = "50" },
                _ => page,
            },
        };

        Assert.ThrowsAny<XunitException>(() => new Conformance().Inspect(document));
    }

    [Fact]
    public void QueryPageBudget_BoundsTheNumberOfQueries()
    {
        using var document = new ConformanceQueryDocument();

        Assert.Equal(100, new Conformance(2).Inspect(document).Count);
        Assert.Equal(2, document.Queries.Count);
    }

    [Fact]
    public void QueryPageBudget_IsSharedAcrossCollections()
    {
        using var document = new ConformanceQueryDocument { SecondCollection = true };

        _ = new Conformance(4).Inspect(document);

        Assert.Equal(["items", "items", "items", "other"], document.QueriedCollections);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyCollections_AreStillQueriedToValidateTheirPage(int budget)
    {
        using var document = new ConformanceQueryDocument(count: 0);

        Assert.Empty(new Conformance(budget).Inspect(document));
        Assert.Equal(budget, document.Queries.Count);
    }

    private sealed class Conformance(int budget = 12) : PluginConformanceTests<CounterPlugin>
    {
        protected override IReadOnlyList<string> SampleTitleIds => [];
        protected override int QueryPageBudget => budget;

        public IReadOnlyList<FieldNode> Inspect(ISaveDocument document) =>
            Walk(document, MemorySaveSet.SingleFile("save.dat", CounterPlugin.Create(5)));

        public IReadOnlyList<ValueEdit> Edits(ISaveDocument document, IReadOnlyList<FieldNode> fields) => ChooseEdits(document, fields);
    }
}
