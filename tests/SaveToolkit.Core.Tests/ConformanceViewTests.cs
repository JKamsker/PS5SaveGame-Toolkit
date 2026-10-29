using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using Xunit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class ConformanceViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedField_WithConflictingValue_IsRejected(bool lazy)
    {
        using var document = new RepeatedFieldDocument(lazy, FieldValue.Of(6L), IntegerSpec.Int32);

        var error = Assert.ThrowsAny<XunitException>(() => new Conformance().Inspect(document));

        Assert.Contains("counter", error.Message, StringComparison.Ordinal);
        Assert.Contains("different values", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedField_WithConflictingSpec_IsRejected()
    {
        using var document = new RepeatedFieldDocument(false, FieldValue.Of(5L), IntegerSpec.UInt32);

        var error = Assert.ThrowsAny<XunitException>(() => new Conformance().Inspect(document));

        Assert.Contains("different specs", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalRepeatedFields_AreReturnedOnce(bool lazy)
    {
        using var document = new RepeatedFieldDocument(lazy, FieldValue.Of(5L), IntegerSpec.Int32);

        var field = Assert.Single(new Conformance().Inspect(document));

        Assert.Equal("counter", field.ValueId);
        Assert.Equal(FieldValue.Of(5L), field.Value);
    }

    private sealed class Conformance : PluginConformanceTests<CounterPlugin>
    {
        protected override IReadOnlyList<string> SampleTitleIds => [];

        public IReadOnlyList<FieldNode> Inspect(ISaveDocument document) =>
            Walk(document, MemorySaveSet.SingleFile("save.dat", CounterPlugin.Create(5)));
    }

    private sealed class RepeatedFieldDocument(bool lazy, FieldValue repeatedValue, FieldSpec repeatedSpec) : BoundSaveDocument
    {
        public override string Title => "Repeated fields";

        public override ViewNode BuildView() => new SectionNode(Title,
        [
            new FieldNode("counter", "Counter", IntegerSpec.Int32, FieldValue.Of(5L)),
            lazy ? SectionNode.Lazy("Other view", "other") : RepeatedField(),
        ]);

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) =>
            lazyId == "other" ? [RepeatedField()] : throw new KeyNotFoundException(lazyId);

        public override IReadOnlyList<FileOutput> Serialize() => [];

        private FieldNode RepeatedField() => new("counter", "Other counter", repeatedSpec, repeatedValue);
    }
}
