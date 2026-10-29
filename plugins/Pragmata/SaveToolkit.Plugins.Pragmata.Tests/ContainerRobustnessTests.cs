using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ContainerRobustnessTests
{
    private const int MaximumPlaintext = 64 * 1024 * 1024;
    private const long RejectionAllocationBudget = 1024 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingBlocksRejectBeforeAllocatingDeclaredPlaintextOrDecodingMetadata(bool hasFirstBlock)
    {
        var envelope = SyntheticDsss.EmptyEnvelope(MaximumPlaintext);
        var bytes = hasFirstBlock
            ? new byte[envelope.Length + 8 * SyntheticDsss.CapacityUnit + DsssContainer.MetadataSize]
            : envelope;
        if (hasFirstBlock)
        {
            envelope.AsSpan(0, DsssContainer.HeaderSize).CopyTo(bytes);
            envelope.AsSpan(DsssContainer.HeaderSize).CopyTo(bytes.AsSpan(bytes.Length - SyntheticDsss.TrailerSize));
            SampleEvidenceTests.RepairOuterHash(bytes);
        }
        // Warm the exception/assertion paths without the large declared length.
        Assert.Throws<SaveFormatException>(() => new DsssContainer(SyntheticDsss.EmptyEnvelope(1)));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<SaveFormatException>(() => new DsssContainer(bytes));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Contains("Truncated Mandarin block", error.Message, StringComparison.Ordinal);
        Assert.True(allocated < RejectionAllocationBudget, $"Rejection allocated {allocated:N0} bytes.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(131072)]
    [InlineData(131073)]
    public void AuthoredContainersPreserveExactCapacityBoundaries(int length)
    {
        var plaintext = Enumerable.Range(0, length).Select(index => (byte)(index % 251)).ToArray();
        var bytes = SyntheticDsss.Create(plaintext);
        var container = new DsssContainer(bytes);
        Assert.Equal(plaintext, container.Plaintext);
        Assert.Equal(length <= 131072 ? 1 : 2, container.BlockCount);
        Assert.Equal(bytes, container.Rebuild(plaintext));
    }
}
