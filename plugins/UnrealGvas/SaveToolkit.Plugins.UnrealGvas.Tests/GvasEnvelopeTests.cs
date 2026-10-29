using System.Buffers.Binary;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasEnvelopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZlibWrappedGvas_ProbesByInnerMagicAndPreservesOriginalCompression(bool astroneer)
    {
        var original = GvasFixture.Wrap(GvasFixture.Primitives(), astroneer);
        var plugin = new UnrealGvasPlugin();
        var file = MemorySaveSet.SingleFile("unknown.bin", original);
        Assert.Equal(ProbeScore.Engine, plugin.Probe(file).Score);
        Assert.Contains("zlib", plugin.Probe(file).Label, StringComparison.Ordinal);
        using var document = plugin.Open(file);
        Assert.False(document.CanEdit);
        Assert.All(GvasFormatTests.Expand(document), field => Assert.True(field.Spec.ReadOnly));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var envelope = GvasEnvelope.Open(original);
        envelope.File.Properties[0].Scalar!.Value = (Int128)99;
        Assert.Throws<SaveFormatException>(() => envelope.Serialize());
    }

    [Fact]
    public void ZlibContainingOtherFormats_IsNotClaimed()
    {
        var bytes = GvasFixture.Wrap("This is not a GVAS file"u8.ToArray());
        Assert.False(new UnrealGvasPlugin().Probe(MemorySaveSet.SingleFile("ue4savegame.dpx.sav", bytes)).IsMatch);
    }

    [Fact]
    public void TruncatedOrCorruptZlibAndWrongEnvelopeSize_AreRejected()
    {
        var bytes = GvasFixture.Wrap(GvasFixture.Primitives());
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<SaveFormatException>(() => GvasEnvelope.Open(bytes[..length]));
        bytes[^1] ^= 0xFF;
        Assert.Throws<SaveFormatException>(() => GvasEnvelope.Open(bytes));
        var astroneer = GvasFixture.Wrap(GvasFixture.Primitives(), true);
        BinaryPrimitives.WriteInt32LittleEndian(astroneer.AsSpan(12), 1);
        Assert.Contains("size", Assert.Throws<SaveFormatException>(() => GvasEnvelope.Open(astroneer)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RandomForeignAndMutatedInputs_OnlyProduceSaveFormatExceptions()
    {
        var random = new Random(1977);
        var valid = GvasFixture.Primitives();
        var plugin = new UnrealGvasPlugin();
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[random.Next(512)];
            random.NextBytes(bytes);
            Assert.False(plugin.Probe(MemorySaveSet.SingleFile("ue4savegame.ps5.sav", bytes)).IsMatch);
            Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes));
            var mutated = valid.ToArray();
            mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            try { _ = GvasFile.Parse(mutated); }
            catch (SaveFormatException) { }
        }
    }

}
