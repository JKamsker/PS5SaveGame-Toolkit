using System.Text;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class TokenAdapterAllocationTests
{
    [Fact]
    public void IdentityLookupSurvivesTransitionFromImportedOrderToStructuralIndex()
    {
        var model = new JsonEditModel(JsonTokenTree.Parse("[1,2,3]"u8.ToArray()));
        var snapshot = model.Snapshot();
        var survivor = model.Find(3);
        Assert.Throws<KeyNotFoundException>(() => model.Find(-1));
        Assert.Throws<KeyNotFoundException>(() => model.Find(4));

        model.Change("remove", 1, null, null);
        model.Change("add", 0, "4", null);
        Assert.Throws<KeyNotFoundException>(() => model.Find(1));
        Assert.Same(survivor, model.Find(3));
        Assert.Equal("4", model.Find(4).Scalar.AsExactNumber());

        model.Restore(snapshot);
        Assert.Equal("1", model.Find(1).Scalar.AsExactNumber());
        Assert.Equal("3", model.Find(3).Scalar.AsExactNumber());
        Assert.Throws<KeyNotFoundException>(() => model.Find(4));
    }

    [Fact]
    public void WideScalarArrayKeepsAdapterBookkeepingBounded()
    {
        const int count = 4000;
        var bytes = Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Repeat("true", count)) + "]");
        var allocated = AllocationProbe.Measure(() => JsonTokenTree.Parse(bytes), out var tree);

        // Includes SDK parsing: prevent per-scalar specs and a second growing parent index
        // from returning even when CI has no private character samples.
        allocated.AssertInRange(1, 2500 * 1024);
        Assert.Equal(count + 1, tree.Nodes.Count);
        Assert.Equal(bytes, JsonTokenWriter.Write(tree.Nodes[0]));
    }
}
