namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseCipherAllocationTests
{
    private const int KiB = 1024;
    private const int MiB = KiB * KiB;

    [Fact]
    public void CipherSetupAllocationsDoNotGrowWithTheNumberOfFeedbackChunks()
    {
        var small = new byte[64];
        var large = new byte[64 * KiB];
        var output = new byte[large.Length];
        foreach (var encrypt in new[] { false, true })
        {
            Transform(small, output, encrypt);
            Transform(large, output, encrypt);
            var smallBytes = Allocated(() => Transform(small, output, encrypt));
            var largeBytes = Allocated(() => Transform(large, output, encrypt));
            Assert.True(largeBytes <= smallBytes + 2 * KiB,
                $"Cipher allocated {largeBytes:N0} B for 1,024 chunks versus {smallBytes:N0} B for one chunk.");
        }
    }

    [Fact]
    public void DecodeAndVerifiedEncodeAvoidRedundantWholePayloadCopies()
    {
        var bytes = new AriseTestContainer(reversePhysicalOrder: true).Encrypt();
        var save = AriseFormat.Decode(bytes);
        _ = AriseFormat.Encode(save);
        var decodeBytes = Allocated(() => { _ = AriseFormat.Decode(bytes); });
        var encodeBytes = Allocated(() => { _ = AriseFormat.Encode(save); });
        Assert.True(decodeBytes < 1600 * KiB, $"Decode allocated {decodeBytes:N0} B.");
        Assert.True(encodeBytes < 3 * MiB, $"Verified encode allocated {encodeBytes:N0} B.");
        Assert.Equal(bytes, AriseFormat.Encode(save));
    }

    private static void Transform(byte[] input, byte[] output, bool encrypt)
    {
        using var cipher = new AriseCipher();
        cipher.Transform(input, output, encrypt);
    }

    private static long Allocated(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
