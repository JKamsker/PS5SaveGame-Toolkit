using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DdlAllocationTests
{
    [Fact]
    public void LargePackedArrayDoesNotAllocateGrowingCopiesOfItsValues()
    {
        const int count = 100_003;
        const long allocationBudget = 5_000_000;
        var payload = Enumerable.Range(0, count).Select(index => (byte)(index % 251)).ToArray();
        var fixture = new DdlTestFixture(new DdlTestFixture.Field("Values", DdlValueKind.UInt8, payload, count));
        var container = Dat1Container.Parse(fixture.Bytes);
        var section = Assert.Single(container.Sections);
        _ = new DdlReader(container, section).Read();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var fields = new DdlReader(container, section).Read();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= allocationBudget, $"DDL allocated {allocated:N0} bytes; budget {allocationBudget:N0}.");
        Assert.Equal(count, fields["Values"].Values.Count);
        Assert.Equal(payload.Select(value => (long)value), fields["Values"].Values.Select(value => value.AsInt64()));
    }

    [Fact]
    public void TruncatedLargeArrayDoesNotPreallocateTheDeclaredCount()
    {
        const long allocationBudget = 32_768;
        var fixture = new DdlTestFixture(new DdlTestFixture.Field("Values", DdlValueKind.UInt8, [1], 524_288));
        var container = Dat1Container.Parse(fixture.Bytes);
        var section = Assert.Single(container.Sections);
        _ = Assert.Throws<SaveFormatException>(() => new DdlReader(container, section).Read());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<SaveFormatException>(() => new DdlReader(container, section).Read());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal("DDL section 0x2EE91673: Field exceeds the object bounds.", error.Message);
        Assert.True(allocated <= allocationBudget, $"Truncated DDL allocated {allocated:N0} bytes.");
    }
}
