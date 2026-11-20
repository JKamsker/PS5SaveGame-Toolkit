using K4os.Compression.LZ4;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

/// <summary>
/// Edited chunks are encoded by the SDK's managed port of K4os' 64-bit fast encoder (K4os runs its
/// 32-bit engine in the browser, which writes other bytes for chunks over 64 KiB). The written save
/// must stay byte-identical to what K4os' default level writes in this 64-bit process.
/// </summary>
public sealed class CsavChunkEncodingTests
{
    [Fact]
    public void EditedFactReencodesTheLargeChunkLikeK4os()
    {
        Assert.True(Environment.Is64BitProcess && !LZ4Codec.Enforce32, "K4os is the 64-bit oracle only in a 64-bit process.");
        var input = SyntheticFactsSave.Create();
        using var document = new Cyberpunk2077Plugin().Open(MemorySaveSet.SingleFile("sav.dat", input, "PPSA04027"));
        _ = document.BuildView();
        Assert.Equal(SyntheticFactsSave.FirstFactValueId, SyntheticFactsSave.FirstFactField(document).ValueId);
        var applied = document.ApplyEdits([new(SyntheticFactsSave.FirstFactValueId, FieldValue.Of(0))]);
        Assert.True(applied.Succeeded, string.Join("; ", applied.Errors.Select(error => error.Message)));
        var output = Assert.Single(document.Serialize()).Content.ToArray();

        var before = CsavContainer.Parse(input);
        var after = CsavContainer.Parse(output);
        Assert.Equal([SyntheticFactsSave.FirstChunkSize, before.Chunks[1].ExpandedSize], after.Chunks.Select(chunk => chunk.ExpandedSize));
        var first = after.Chunks[0];
        var raw = after.Payload.AsSpan(0, first.ExpandedSize);
        var buffer = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
        var length = LZ4Codec.Encode(raw, buffer);
        Assert.True(length > 0 && length < raw.Length * 3 / 4, $"The authored chunk should compress ({length} bytes).");
        Assert.True(output.AsSpan(first.Offset + 8, first.StoredSize - 8).SequenceEqual(buffer.AsSpan(0, length)),
            "Re-encoded chunk differs from K4os L00_FAST.");
        var second = before.Chunks[1];
        Assert.True(output.AsSpan(after.Chunks[1].Offset, after.Chunks[1].StoredSize).SequenceEqual(input.AsSpan(second.Offset, second.StoredSize)));
    }

    [Theory]
    [InlineData(70_000)]
    [InlineData(1024 * 1024 + 7)]
    public void RebuiltChunksOfEverySizeMatchK4os(int size)
    {
        var payload = new byte[size];
        var random = new Random(size);
        for (var i = 0; i < size; i++) payload[i] = random.Next(4) == 0 ? (byte)random.Next(256) : (byte)"<gameItemID>\0\x01"[i % 14];
        CsavCorruptionFixture.SetWord(payload, 0, 0);
        var container = CsavContainer.Parse(CsavCorruptionFixture.Create(payload, [new("x", -1, -1, 0, size)], compressed: true));
        var edited = payload.ToArray();
        edited[^1] ^= 0x55;
        var output = container.Rebuild(edited);
        var buffer = new byte[LZ4Codec.MaximumOutputSize(size)];
        var length = LZ4Codec.Encode(edited, buffer);
        var chunk = CsavContainer.Parse(output).Chunks[0];
        Assert.True(output.AsSpan(chunk.Offset + 8, chunk.StoredSize - 8).SequenceEqual(buffer.AsSpan(0, length)));
    }
}
