using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class RegulationAllocationTests
{
    [Fact]
    public void ValidSampleDirectory_DoesNotAllocateDiagnosticsForEveryParamRow()
    {
        var bytes = WriteTestSupport.Sample().ReadAllBytes();
        var decoded = RegulationDecoder.Decode(bytes.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize));
        _ = RegulationArchiveReader.Read(decoded); // Exclude first-use initialization from the allocation budget.

        var allocated = AllocationProbe.Measure(() => RegulationArchiveReader.Read(decoded), out var archive);

        Assert.Equal(186, archive.Tables.Count);
        Assert.Equal(37_745_424, archive.DecodedSize);
        // Metadata for 186 tables is small. Validating every PARAM row must not construct
        // two contextual error messages per row (tens of MB on this sample).
        allocated.AssertInRange(1, 2 * 1024 * 1024);
    }
}
