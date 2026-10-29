namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ByteChangeAssertionTests
{
    [Fact]
    public void NoncontiguousOffsetsAcceptOnlyTheirActualChangedBytes()
    {
        byte[] original = [0, 0, 0, 0, 0];
        byte[] edited = [0, 1, 0, 2, 0];
        ByteChangeAssertions.OnlyOffsetsChanged(original, edited, [1, 3, 3]);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ByteChangeAssertions.OnlyOffsetsChanged(original, edited, [1]));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ByteChangeAssertions.OnlyOffsetsChanged(original, original, [1, 3]));
    }

    [Fact]
    public void RangeIncludesBothEdgesAndRejectsChangesOnEitherSide()
    {
        byte[] original = [0, 0, 0, 0, 0];
        ByteChangeAssertions.OnlyRangeChanged(original, [0, 1, 0, 1, 0], 1, 3);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ByteChangeAssertions.OnlyRangeChanged(original, [1, 0, 0, 0, 0], 1, 3));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ByteChangeAssertions.OnlyRangeChanged(original, [0, 0, 0, 0, 1], 1, 3));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ByteChangeAssertions.OnlyRangeChanged(original, original, 1, 3));
    }
}
