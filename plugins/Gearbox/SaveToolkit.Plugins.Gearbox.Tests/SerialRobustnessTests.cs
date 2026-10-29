using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SerialRobustnessTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    [InlineData(false, int.MinValue)]
    [InlineData(false, int.MaxValue)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    [InlineData(true, int.MinValue)]
    [InlineData(true, int.MaxValue)]
    public void EverySerialAndCrcRepairedPayloadPrefix_HasAnItemLocalDiagnostic(bool wonderlands, int seed)
    {
        var payload = SyntheticItem.Payload(wonderlands);
        var version = wonderlands ? 5 : 4;
        var serial = IndependentSerialProbe.Encode(payload, version, seed);
        var reader = new SerialReader(wonderlands);
        Assert.True(reader.Read(serial).Complete);
        for (var length = 0; length < serial.Length; length++)
            Rejected(reader, serial[..length]);
        for (var length = 0; length < payload.Length; length++)
        {
            var prefix = payload[..length];
            var repaired = IndependentSerialProbe.Encode(prefix, version, seed);
            Rejected(reader, repaired);
            if (length == 0) continue; // Seven bytes is below the supported envelope minimum.
            Assert.Equal(prefix, SerialEnvelope.Decode(repaired, wonderlands).Payload.ToArray());
        }
        Assert.Equal(serial, SerialEnvelope.Decode(serial, wonderlands).EncodeOriginalSeed());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryEncryptedBodyBitCorruption_FailsChecksumWithoutThrowing(bool wonderlands)
    {
        var serial = IndependentSerialProbe.Encode(SyntheticItem.Payload(wonderlands), wonderlands ? 5 : 4, -1);
        var reader = new SerialReader(wonderlands);
        for (var position = 5; position < serial.Length; position++)
            for (var bit = 0; bit < 8; bit++)
            {
                var corrupt = serial.ToArray();
                corrupt[position] ^= (byte)(1 << bit);
                Assert.False(reader.Read(corrupt).ChecksumValid);
                Rejected(reader, corrupt);
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrcValidExcessivePartAndAdditionalCounts_AreBounded(bool wonderlands)
    {
        var serial = IndependentSerialProbe.Encode(SyntheticItem.Payload(wonderlands), wonderlands ? 5 : 4, int.MinValue);
        var reader = new SerialReader(wonderlands);
        var info = reader.Read(serial);
        var envelope = SerialEnvelope.Decode(serial, wonderlands);
        var partCountOffset = info.LevelOffset!.Value + 7;
        foreach (var (offset, width, value) in new[]
        {
            (partCountOffset, 6, 63), (partCountOffset + 6, 4, 15), (partCountOffset + 10, 8, 255),
        })
        {
            var corrupt = envelope.Patch(offset, width, value);
            Assert.True(reader.Read(corrupt).ChecksumValid);
            Rejected(reader, corrupt);
            Assert.Equal(corrupt, SerialEnvelope.Decode(corrupt, wonderlands).EncodeOriginalSeed());
            Assert.Throws<SaveFormatException>(() => SerialWrites.Set(corrupt, wonderlands, "rerolls", 20));
        }
        if (info.RemainingBits > 0)
        {
            var corrupt = envelope.Patch(envelope.Payload.Length * 8 - info.RemainingBits, 1, 1);
            Assert.Contains("non-padding tail", reader.Read(corrupt).Diagnostic, StringComparison.Ordinal);
            Rejected(reader, corrupt);
        }
    }

    private static void Rejected(SerialReader reader, byte[] bytes)
    {
        var original = bytes.ToArray();
        var info = reader.Read(bytes);
        Assert.False(info.Complete);
        Assert.False(string.IsNullOrWhiteSpace(info.Diagnostic));
        Assert.Null(info.Rerolls);
        Assert.Null(info.Chaos);
        Assert.Equal(original, bytes);
    }
}
