using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SerialCodecTests
{
    // Fixed synthetic vectors generated independently with Python stdlib binascii.crc32/struct.
    [Theory]
    [InlineData(false, "030000000042fb80be8123456789", 0)]
    [InlineData(false, "04ffffffff85f6086ce4770181df", -1)]
    [InlineData(false, "04800000003ea56d5d4bdb8c40d7", int.MinValue)]
    [InlineData(false, "047fffffff48811e4f17f74555bd", int.MaxValue)]
    [InlineData(true, "05075bcd15c03e4929780647a255", 123456789)]
    public void OriginalSeedVectors_MatchIndependentPython(bool wonderlands, string hex, int seed)
    {
        var bytes = Convert.FromHexString(hex);
        var envelope = SerialEnvelope.Decode(bytes, wonderlands);
        Assert.Equal(seed, envelope.Seed);
        Assert.Equal(Convert.FromHexString("80be8123456789"), envelope.Payload.ToArray());
        Assert.Equal(bytes, envelope.EncodeOriginalSeed());
        Assert.Equal(bytes, IndependentSerialProbe.Encode(envelope.Payload.ToArray(), envelope.Version, seed));
    }

    [Fact]
    public void IndependentCrc_MatchesThePublishedCheckVector() =>
        Assert.Equal(0xCBF43926u, IndependentSerialProbe.Crc32("123456789"u8.ToArray()));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BadLengthChecksumOrGame_StayPerItemDiagnostics(bool wonderlands)
    {
        var reader = new SerialReader(wonderlands);
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[7], new byte[SerialEnvelope.MaximumLength + 1], new byte[30] })
            Assert.False(reader.Read(bytes).ChecksumValid);
        var valid = IndependentSerialProbe.Encode([128, 0, 0, 0, 0], wonderlands ? 5 : 4, -31);
        var damaged = valid.ToArray();
        damaged[^1] ^= 1;
        Assert.False(reader.Read(damaged).ChecksumValid);
        Assert.Throws<SaveFormatException>(() => SerialEnvelope.Decode(valid, !wonderlands));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownVersionsMarkersAndTruncation_KeepValidatedEnvelope(bool wonderlands)
    {
        var reader = new SerialReader(wonderlands);
        foreach (var payload in new byte[][] { [128, 127], [127, 1], [128, 1] })
        {
            var bytes = IndependentSerialProbe.Encode(payload, wonderlands ? 5 : 4, int.MinValue);
            var info = reader.Read(bytes);
            Assert.True(info.ChecksumValid);
            Assert.False(info.Complete);
            Assert.Equal(bytes, SerialEnvelope.Decode(bytes, wonderlands).EncodeOriginalSeed());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownCustomizationsTailsAndPartIndices_AreReadOnly(bool wonderlands)
    {
        var version = wonderlands ? 5 : 4;
        var reader = new SerialReader(wonderlands);
        var payload = SyntheticItem.Payload(wonderlands);
        Assert.True(reader.Read(IndependentSerialProbe.Encode(payload, version, -1)).Complete);
        foreach (var unknown in new[] { SyntheticItem.Payload(wonderlands, customizations: 1),
            SyntheticItem.Payload(wonderlands, invalidPart: true), payload.Concat(new byte[] { 1 }).ToArray() })
        {
            var bytes = IndependentSerialProbe.Encode(unknown, version, -1);
            var info = reader.Read(bytes);
            Assert.True(info.ChecksumValid);
            Assert.NotNull(info.Level);
            Assert.False(info.Complete);
            Assert.Null(info.Rerolls);
            Assert.Null(info.Chaos);
            Assert.Equal(bytes, SerialEnvelope.Decode(bytes, wonderlands).EncodeOriginalSeed());
        }
        for (var length = 1; length < payload.Length; length++)
            Assert.False(reader.Read(IndependentSerialProbe.Encode(payload[..length], version, 17)).Complete);
    }

    [Fact]
    public void CrcValidRandomPayloads_StayBoundedDiagnostics()
    {
        var random = new Random(731);
        foreach (var wonderlands in new[] { false, true })
        {
            var reader = new SerialReader(wonderlands);
            for (var i = 0; i < 500; i++)
            {
                var payload = new byte[random.Next(1, 256)];
                random.NextBytes(payload);
                var serial = IndependentSerialProbe.Encode(payload, wonderlands ? 5 : 4, random.Next());
                Assert.True(reader.Read(serial).ChecksumValid);
            }
        }
    }
}
