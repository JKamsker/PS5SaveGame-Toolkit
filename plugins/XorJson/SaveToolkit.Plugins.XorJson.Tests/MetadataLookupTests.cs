using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class MetadataLookupTests
{
    private const int NodeCount = 20_000;
    private const int LookupBudget = 512 * 1024;

    [Theory]
    [InlineData("fresh")]
    [InlineData("removed")]
    [InlineData("restored")]
    public void ColdWarmValidAndRetiredLookupsUseTheCurrentIndex(string state)
    {
        var bytes = XorJsonTests.Encode("[" + string.Join(',', Enumerable.Range(0, NodeCount)) + "]");
        ISaveDocument? document = null;
        IFieldMetadataDocument? resolver = null;
        var measurement = AllocationProbe.Measure(() =>
        {
            Assert.Empty(resolver!.ResolveFieldMetadata(["value/2147483647"]));
            Assert.Single(resolver.ResolveFieldMetadata(["value/2"]));
            Assert.Single(resolver.ResolveFieldMetadata(["value/2"])); // A warm request has the same bound.
            Assert.Equal(state == "removed" ? 0 : 1, resolver.ResolveFieldMetadata(["value/1"]).Count);
            Assert.Empty(resolver.ResolveFieldMetadata([$"value/{NodeCount + 1}"])); // Undone addition is retired.
        }, beforeRun: () =>
        {
            document = XorJsonTests.Open(bytes);
            if (state == "removed") document.InvokeAction("json/remove/1", new Dictionary<string, FieldValue>());
            if (state == "restored")
            {
                var addition = document.InvokeAction("json/add/0", new Dictionary<string, FieldValue> { ["json"] = FieldValue.Of("2") });
                document.RestoreMemento(addition.Revert!);
            }
            resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        }, afterRun: () =>
        {
            if (state != "removed") Assert.Equal(bytes, document!.Serialize().Single().Content.ToArray());
            document!.Dispose();
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{state}: {measurement}");
        measurement.AssertLessThan(LookupBudget, "Five targeted lookups must not enumerate or materialize the 20,000-node model.");
    }
}
