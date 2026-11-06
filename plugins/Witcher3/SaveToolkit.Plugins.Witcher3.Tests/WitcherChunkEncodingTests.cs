using K4os.Compression.LZ4;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3.Tests;

/// <summary>
/// Edited chunks are encoded by the SDK's managed LZ4 HC port (K4os' HC encoder traps the browser
/// runtime). The written save must stay byte-identical to what K4os L12_MAX produced before.
/// </summary>
public sealed class WitcherChunkEncodingTests
{
    [Fact]
    public void EditedPlayerSaveChunksMatchK4osMaximumLevel()
    {
        var input = SyntheticPlayerSave.Create();
        using var document = WitcherTestFiles.Open(input);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("player/level", FieldValue.Of(2))]).Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(1, AssertChunksMatchK4os(input, output));
    }

    [Fact]
    public void LargeEditedChunkMatchesK4osMaximumLevel()
    {
        // About 1.1 MiB of payload in two chunks; the edit re-encodes the first (~560 KiB) one.
        var input = SyntheticSave.Create(valueCount: 70_000);
        var container = WitcherContainer.Parse(input);
        var payload = container.Payload.ToArray();
        var value = payload.AsSpan(SyntheticSave.BlockOffset).IndexOf((ReadOnlySpan<byte>)[42, 0, 0, 0]) + SyntheticSave.BlockOffset;
        payload[value] = 43;
        var output = WitcherWriter.Write(container, payload);
        Assert.Equal(1, AssertChunksMatchK4os(input, output));
    }

    /// <summary>Returns how many chunks were re-encoded; each must equal K4os' L12_MAX encoding.</summary>
    private static int AssertChunksMatchK4os(byte[] input, byte[] output)
    {
        var before = WitcherContainer.Parse(input);
        var after = WitcherContainer.Parse(output);
        var reencoded = 0;
        var position = 0;
        foreach (var chunk in after.Chunks)
        {
            var stored = output.AsSpan(chunk.Offset, chunk.StoredSize).ToArray();
            var raw = after.Payload.AsSpan(position, chunk.ExpandedSize);
            position += chunk.ExpandedSize;
            if (before.Chunks.Any(c => input.AsSpan(c.Offset, c.StoredSize).SequenceEqual(stored))) continue;
            reencoded++;
            var buffer = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
            var length = LZ4Codec.Encode(raw, buffer, LZ4Level.L12_MAX);
            var expected = length < raw.Length ? buffer.AsSpan(0, length) : raw;
            Assert.True(expected.SequenceEqual(stored.AsSpan()), "Re-encoded chunk differs from K4os L12_MAX.");
        }

        return reencoded;
    }
}
