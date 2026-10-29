using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class FaithOpeningPerformanceTests
{
    [Fact]
    public void OpeningBindsUnloadedInventoryWithoutBuildingItsPresentation()
    {
        var file = FaithTestData.File(FaithPerformanceData.CapacitySave(mapped: true));
        var plugin = new FinalFantasy16Plugin();
        var references = new FaithCountingReferences(IReferenceContext.Empty);
        using (var warm = plugin.Open(file, references)) _ = warm.CanEdit;
        references.CatalogCalls.Clear();
        var allocated = AllocationProbe.MeasureOpen(() => plugin.Open(file, references), out var opened);
        using var document = opened;
        allocated.AssertLessThan(4 * 1024 * 1024, $"Opening an unrendered capacity inventory allocated {allocated.MinimumBytes:N0} bytes.");
        Assert.Empty(references.CatalogCalls);
        Assert.Equal("5", document.GetValue("inventory/1199/item").AsString());
        Assert.Equal(1201, document.GetValue("inventory/1199/quantity").AsInt32());
        _ = document.BuildView();
        Assert.NotEmpty(references.CatalogCalls);
    }
}
