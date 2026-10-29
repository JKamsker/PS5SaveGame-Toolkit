using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartInspectionTests
{
    [Fact]
    public void PickerRetainsFirstRecordAndExactPresentation()
    {
        var catalog = new Catalog();
        var action = CatalogueInspection.Action(catalog);
        Assert.Equal("references/inspect/items", action.ActionId);
        Assert.Equal("Inspect catalogue item", action.Label);
        Assert.Equal("Read-only inspection of the pinned, attributed item catalogue. It does not edit the save or resolve gameplay ownership.", action.Description);
        Assert.Equal(Tone.Normal, action.Tone);
        Assert.False(action.Mutates);
        Assert.False(action.SupportsPlanning);
        Assert.Null(action.Confirm);
        var parameter = Assert.Single(action.Parameters);
        Assert.Equal("item", parameter.ValueId);
        Assert.Equal("Catalogue item", parameter.Label);
        Assert.Equal(new ReferenceSpec("items"), parameter.Spec);
        Assert.Equal("z-first", parameter.Value.AsString());
        Assert.Equal(new ReferenceQuery { Limit = 1 }, catalog.LastQuery);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Weapon", "Sourced description")]
    public void InspectionRetainsExactMessageWarningsAndNoChanges(string? category, string? description)
    {
        var catalog = new Catalog(category: category, description: description);
        var outcome = CatalogueInspection.Inspect(catalog, new Dictionary<string, FieldValue> { ["item"] = FieldValue.Of("z-first") });
        Assert.Equal($"First · z-first · {category ?? "Unknown category"}\n{description}\nsecond: 2; first: 1\n{catalog.Record.AttributionText}\nInspection only; the save was not changed.", outcome.Message);
        Assert.Equal(["Catalogue metadata is attributed reference material; it does not prove PS5 ownership, availability or gameplay effects."], outcome.Warnings);
        Assert.Empty(outcome.Changes);
        Assert.True(outcome.Undoable);
        Assert.False(outcome.StructureChanged);
        Assert.Null(outcome.Revert);
    }

    [Fact]
    public void EmptyAndAbsentCatalogsKeepTheirDistinctErrors()
    {
        Assert.Equal("The Atomic Heart item catalogue is empty.",
            Assert.Throws<InvalidOperationException>(() => CatalogueInspection.Action(new Catalog(empty: true))).Message);
        Assert.Equal("No pinned Atomic Heart item catalogue is available.",
            Assert.Throws<InvalidOperationException>(() => CatalogueInspection.Inspect(null, new Dictionary<string, FieldValue>())).Message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-name")]
    [InlineData("wrong-kind")]
    [InlineData("extra")]
    [InlineData("unknown")]
    [InlineData("wrong-case")]
    [InlineData("empty")]
    public void InvalidSelectionsRetainArgumentNameAndMessage(string scenario)
    {
        var parameters = new Dictionary<string, FieldValue> { ["item"] = FieldValue.Of("z-first") };
        if (scenario == "missing") parameters.Clear();
        if (scenario == "wrong-name") { parameters.Clear(); parameters["other"] = FieldValue.Of("z-first"); }
        if (scenario == "wrong-kind") parameters["item"] = FieldValue.Of(1);
        if (scenario == "extra") parameters["extra"] = FieldValue.Of("z-first");
        if (scenario == "unknown") parameters["item"] = FieldValue.Of("unknown");
        if (scenario == "wrong-case") parameters["item"] = FieldValue.Of("Z-FIRST");
        if (scenario == "empty") parameters["item"] = FieldValue.Of("");
        var expected = scenario is "unknown" or "wrong-case" or "empty"
            ? "The selected catalogue item is not in the pinned catalogue." : "Choose one catalogue item.";
        var error = Assert.Throws<ArgumentException>(() => CatalogueInspection.Inspect(new Catalog(), parameters));
        Assert.Equal($"{expected} (Parameter 'parameters')", error.Message);
        Assert.Equal("parameters", error.ParamName);
    }

    private sealed class Catalog(bool empty = false, string? category = null, string? description = null) : IReferenceCatalog
    {
        public string Id => "items";
        public ReferencePackInfo Pack { get; } = new("test", "1.0.0", "Test", ReferencePackOrigin.Embedded);
        public ReferenceDatasetInfo Dataset { get; } = new("test", "items", ReferenceDatasetKind.Records,
            new("https://example.org", "Test authors", "CC0-1.0", "https://example.org/license", new(2026, 10, 29), "Authored fixture"));
        public int Count => empty ? 0 : 1;
        public IReadOnlyList<string> Categories => [];
        internal ReferenceQuery? LastQuery { get; private set; }
        internal ReferenceRecord Record => new("z-first", "First")
        {
            Dataset = Dataset, Category = category, Description = description,
            Attributes = new Dictionary<string, string> { ["second"] = "2", ["first"] = "1" },
        };
        public ReferenceRecord? Find(string key) => !empty && key == Record.Key ? Record : null;
        public ReferencePage Search(ReferenceQuery query)
        {
            LastQuery = query;
            return new(empty ? [] : [Record], Count, query.Offset);
        }
    }
}
