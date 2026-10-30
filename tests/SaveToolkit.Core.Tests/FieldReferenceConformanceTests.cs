using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceConformanceTests
{
    [Fact]
    public void ConformanceDiscoversUnloadedCanonicalTargetsOnceAcrossRepeatedLazyAndQueryProjections()
    {
        using var document = new FieldReferenceDocument { ShowCanonical = false };
        var fields = new Conformance().Inspect(document);
        Assert.Equal(["quantity", "total"], fields.Select(field => field.ValueId).Order(StringComparer.Ordinal));
        Assert.Equal(Confidence.Inferred, fields.Single(field => field.ValueId == "quantity").Confidence);
        Assert.Equal(0, document.Writes);
    }

    private sealed class Conformance : PluginConformanceTests<CounterPlugin>
    {
        protected override IReadOnlyList<string> SampleTitleIds => [];
        public IReadOnlyList<FieldNode> Inspect(ISaveDocument document) => Walk(document,
            MemorySaveSet.SingleFile("save.dat", new byte[] { 2, 1 }));
    }
}
