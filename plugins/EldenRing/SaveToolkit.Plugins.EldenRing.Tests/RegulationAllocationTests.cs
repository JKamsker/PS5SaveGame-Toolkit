namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class RegulationAllocationTests
{
    [Fact]
    public void ValidSampleDirectory_DoesNotAllocateDiagnosticsForEveryParamRow()
    {
        var bytes = WriteTestSupport.Sample().ReadAllBytes();
        var decoded = RegulationDecoder.Decode(bytes.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize));
        _ = RegulationArchiveReader.Read(decoded); // Exclude first-use initialization from the allocation budget.

        var before = GC.GetAllocatedBytesForCurrentThread();
        var archive = RegulationArchiveReader.Read(decoded);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(186, archive.Tables.Count);
        Assert.Equal(37_745_424, archive.DecodedSize);
        // Metadata for 186 tables is small. Validating every PARAM row must not construct
        // two contextual error messages per row (tens of MB on this sample).
        Assert.InRange(allocated, 1, 2 * 1024 * 1024);
    }
}
