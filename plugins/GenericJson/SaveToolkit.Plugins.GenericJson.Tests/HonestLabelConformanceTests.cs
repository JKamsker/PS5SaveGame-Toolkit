using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using Xunit;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class HonestLabelConformanceTests
{
    [Fact]
    public void DefaultAdvisory_WalksAliasesLazyPagesAndInspectionParametersWithoutFailing()
    {
        using var document = new AuditDocument();
        HonestLabelConformance.Check(document, "synthetic", PluginMaturity.ReadOnly, 1, 1);
        Assert.Equal(1, document.Expansions);
        Assert.Equal(1, document.Queries);
    }

    [Fact]
    public void StrictMode_DoesNotHideLaterAliasesLazyPagesOrInspectionParameters()
    {
        using var document = new AuditDocument();
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            HonestLabelConformance.Check(document, "synthetic", PluginMaturity.ReadOnly, 1, 1, enforce: true));
        Assert.Contains("'alias'", error.Message);
        Assert.Contains("'paged'", error.Message);
        Assert.Contains("'inspect/parameter/input'", error.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    [InlineData("Storage evidence is documented at the enclosing table in this synthetic case.", true)]
    public void StrictExceptions_RequireAnExplicitReason(string? reason, bool accepted)
    {
        using var document = new AuditDocument();
        var error = Record.Exception(() => HonestLabelConformance.Check(document, "synthetic",
            PluginMaturity.ReadOnly, 1, 1, _ => reason, enforce: true));
        if (accepted) Assert.Null(error);
        else Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(error);
    }

    private sealed class AuditDocument : ISaveDocument, IQueryableDocument
    {
        private static readonly QueryableColumn[] Columns = [new("value", "Raw stored value")];
        public string Title => "Mechanical label fixture";
        public bool CanEdit => false;
        public int Expansions { get; private set; }
        public int Queries { get; private set; }
        public IReadOnlyList<QueryableCollection> Collections => [new("rows", "Rows", Columns, 1)];

        public ViewNode BuildView() => new SectionNode("Fixture", [
            Verified("alias") with { Description = "Decoded signed int32 storage." },
            SectionNode.Lazy("Later alias", "lazy"),
            new QueryableTableNode("rows", "Rows", Columns, 1),
            new ActionNode("inspect", "Inspect") { Mutates = false, Parameters = [Verified("input")] },
        ]);

        public IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            Assert.Equal("lazy", lazyId);
            Expansions++;
            return [Verified("alias")];
        }

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Assert.Equal("rows", collectionId);
            Queries++;
            return new(collectionId, Columns,
                [new TableRow("row", new Dictionary<string, ViewNode> { ["value"] = Verified("paged") })],
                1, 1, query.Offset, query.Limit);
        }

        public FieldValue GetValue(string valueId) => FieldValue.Of(1);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => throw new NotSupportedException();
        public IReadOnlyList<FileOutput> Serialize() => throw new NotSupportedException();
        public void Dispose() { }

        private static FieldNode Verified(string id) => new(id, "Stored value",
            IntegerSpec.Int32 with { ReadOnly = true }, FieldValue.Of(1)) { Confidence = Confidence.Verified };
    }
}
