using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class BinaryDataCompatibilityTests
{
    private const string RangeError = "BG3 data is truncated or contains an invalid offset.";

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(9, 0)]
    [InlineData(8, 1)]
    [InlineData(1, int.MaxValue)]
    [InlineData(int.MaxValue, 1)]
    public void InvalidRangesRetainTheExactDiagnostic(int offset, int length)
    {
        var error = Assert.Throws<SaveFormatException>(() => BinaryData.Slice(new byte[8], offset, length).ToArray());
        Assert.Equal(RangeError, error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void EveryIncompletePrimitiveRetainsTheExactDiagnostic()
    {
        for (var length = 0; length < sizeof(ulong); length++)
        {
            var bytes = new byte[length];
            Assert.Equal(RangeError, Assert.Throws<SaveFormatException>(() => BinaryData.U64(bytes, 0)).Message);
            if (length < sizeof(uint))
            {
                Assert.Equal(RangeError, Assert.Throws<SaveFormatException>(() => BinaryData.U32(bytes, 0)).Message);
                Assert.Equal(RangeError, Assert.Throws<SaveFormatException>(() => BinaryData.I32(bytes, 0)).Message);
            }
            if (length < sizeof(ushort))
                Assert.Equal(RangeError, Assert.Throws<SaveFormatException>(() => BinaryData.U16(bytes, 0)).Message);
        }
    }

    [Fact]
    public void ValidRangesAreZeroCopyAndPrimitivesKeepTheirEndianAndSignedness()
    {
        byte[] bytes = [0, 0x98, 0xba, 0xdc, 0xfe, 0x10, 0x32, 0x54, 0xf6];
        Assert.Equal((ushort)0xba98, BinaryData.U16(bytes, 1));
        Assert.Equal(0xfedcba98u, BinaryData.U32(bytes, 1));
        Assert.Equal(unchecked((int)0xfedcba98), BinaryData.I32(bytes, 1));
        Assert.Equal(0xf6543210fedcba98ul, BinaryData.U64(bytes, 1));
        Assert.True(BinaryData.Slice(bytes, bytes.Length, 0).IsEmpty);
        var slice = BinaryData.Slice(bytes, 1, 1);
        bytes[1] = 42;
        Assert.Equal((byte)42, slice[0]);
    }

    [Fact]
    public void BufferBudgetAndCallerDiagnosticsRemainLocal()
    {
        Assert.Equal(BinaryData.MaxBufferSize, BinaryData.Size((ulong)BinaryData.MaxBufferSize));
        var error = Assert.Throws<SaveFormatException>(() => BinaryData.Size((ulong)BinaryData.MaxBufferSize + 1));
        Assert.Equal("BG3 buffer exceeds the supported 256 MiB limit.", error.Message);
        Assert.Equal("caller-specific error", Assert.Throws<SaveFormatException>(() =>
            BinaryData.Require(false, "caller-specific error")).Message);
    }
}
