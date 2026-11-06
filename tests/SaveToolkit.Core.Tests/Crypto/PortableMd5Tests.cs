using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.Crypto;

namespace SaveToolkit.Core.Tests.Crypto;

/// <summary>
/// The managed MD5 is what runs in the browser; these tests pin it to RFC 1321 and to the platform MD5.
/// </summary>
public sealed class PortableMd5Tests
{
    // RFC 1321 appendix A.5 test suite.
    public static TheoryData<string, string> Rfc1321Suite => new()
    {
        { "", "D41D8CD98F00B204E9800998ECF8427E" },
        { "a", "0CC175B9C0F1B6A831C399E269772661" },
        { "abc", "900150983CD24FB0D6963F7D28E17F72" },
        { "message digest", "F96B697D7CB7938D525A2F31AAF161D0" },
        { "abcdefghijklmnopqrstuvwxyz", "C3FCD3D76192E4007DFB496CCA67E13B" },
        { "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", "D174AB98D277D9F5A5611C2C9F419D9F" },
        { "12345678901234567890123456789012345678901234567890123456789012345678901234567890", "57EDF4A22BE3C955AC49DA2E2107B67A" },
    };

    [Theory]
    [MemberData(nameof(Rfc1321Suite))]
    public void ManagedAndPortableDigestsMatchRfc1321(string message, string expected)
    {
        var bytes = Encoding.ASCII.GetBytes(message);
        Assert.Equal(expected, Convert.ToHexString(PortableMd5.HashDataManaged(bytes)));
        Assert.Equal(expected, Convert.ToHexString(PortableMd5.HashData(bytes)));
        using var incremental = IncrementalMd5.CreateManaged();
        foreach (var value in bytes)
        {
            incremental.AppendData([value]);
        }

        Assert.Equal(expected, Convert.ToHexString(incremental.GetHashAndReset()));
    }

    [Fact]
    public void ManagedDigestOfOneMillionAsMatchesTheWellKnownVector()
    {
        var bytes = new byte[1_000_000];
        bytes.AsSpan().Fill((byte)'a');
        Assert.Equal("7707D6AE4E027C70EEA2A935C2296F21", Convert.ToHexString(PortableMd5.HashDataManaged(bytes)));
    }

    [Fact]
    public void ManagedDigestMatchesPlatformForEveryLengthAroundThePaddingBoundaries()
    {
        var random = new Random(0x1321);
        var data = new byte[1100];
        random.NextBytes(data);
        for (var length = 0; length <= data.Length; length++)
        {
            var input = data.AsSpan(0, length);
            Assert.Equal(PlatformMd5(input), PortableMd5.HashDataManaged(input));
        }
    }

    [Fact]
    public void IncrementalManagedDigestMatchesPlatformForRandomSplits()
    {
        var random = new Random(0x0A55);
        using var managed = IncrementalMd5.CreateManaged();
        using var portable = IncrementalMd5.Create();
        Assert.True(managed.IsManaged);
        Assert.Equal(OperatingSystem.IsBrowser(), portable.IsManaged);
        for (var trial = 0; trial < 200; trial++)
        {
            var data = new byte[random.Next(0, 5000)];
            random.NextBytes(data);
            var offset = 0;
            while (offset < data.Length)
            {
                var piece = Math.Min(data.Length - offset, random.Next(0, 200));
                managed.AppendData(data.AsSpan(offset, piece));
                portable.AppendData(data.AsSpan(offset, piece));
                offset += piece;
            }

            var expected = PlatformMd5(data);
            Assert.Equal(expected, managed.GetHashAndReset());
            Assert.Equal(expected, portable.GetHashAndReset());
        }
    }

    [Fact]
    public void SpanOverloadsWriteSixteenBytesAndRejectShortDestinations()
    {
        var destination = new byte[20];
        Assert.Equal(PortableMd5.HashSizeInBytes, PortableMd5.HashData("abc"u8, destination));
        Assert.Equal("900150983CD24FB0D6963F7D28E17F72", Convert.ToHexString(destination, 0, 16));
        Assert.All(destination[16..], value => Assert.Equal(0, value));
        Assert.Throws<ArgumentException>(() => PortableMd5.HashData("abc"u8, new byte[15]));
        using var incremental = IncrementalMd5.CreateManaged();
        Assert.Throws<ArgumentException>(() => incremental.GetHashAndReset(new byte[15]));
    }

    [Fact]
    public void BothIncrementalPathsRejectUseAfterDispose()
    {
        foreach (var md5 in new[] { IncrementalMd5.Create(), IncrementalMd5.CreateManaged() })
        {
            md5.Dispose();
            Assert.Throws<ObjectDisposedException>(() => md5.AppendData("abc"u8));
            Assert.Throws<ObjectDisposedException>(() => md5.GetHashAndReset());
        }
    }

    [Fact]
    public void PortableMd5UsesThePlatformOutsideTheBrowser()
    {
        Assert.Equal(OperatingSystem.IsBrowser(), PortableMd5.UsesManagedImplementation);
    }

#pragma warning disable CA5351 // Reference implementation for comparison only.
    private static byte[] PlatformMd5(ReadOnlySpan<byte> data) => MD5.HashData(data);
#pragma warning restore CA5351
}
