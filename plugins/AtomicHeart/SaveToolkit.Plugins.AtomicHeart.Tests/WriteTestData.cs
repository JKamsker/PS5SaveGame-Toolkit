using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

internal static class WriteTestData
{
    internal static readonly AtomicHeartPlugin Plugin = new();
    internal static IReferenceContext References() => new ReferencePackStore([Plugin]).Pin(Plugin, "PPSA10695");
    internal static ISaveFile Sample() => SampleSaves.Load("PPSA10695").AllDiskFiles.First(f => !f.IsSystemFile);
    internal static byte[] Output(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
    internal static ISaveDocument Open(byte[] bytes) => Plugin.Open(MemorySaveSet.SingleFile("slot", bytes, "PPSA10695"), References());
    internal static QueryableCollectionPage Table(ISaveDocument document) => Assert.IsAssignableFrom<IQueryableDocument>(document)
        .Query(InventoryCollection.Id, new() { IncludeEmpty = true, Limit = QueryableCollectionLimits.MaxPageSize });
    internal static FieldNode Field(TableRow row, string key) => Assert.IsType<FieldNode>(row.Cells[key]);

    /// <summary>
    /// Independent literal-block oracle: no shared decoder or production container parser for output.
    /// Unchanged compressed streams must equal the original stream exactly; their original decoded
    /// bytes are the baseline. Every newly emitted stream must be CC 06 followed by exactly its bytes.
    /// This verifies new stored output, not independent decoding of the original compressed corpus.
    /// </summary>
    internal static byte[] IndependentDecode(byte[] original, byte[] output)
    {
        var file = SaveExtensionFile.Parse(original);
        var baseline = SlotDataDecoder.Decode(original, file).Plain;
        using var stream = new MemoryStream(output, writable: false);
        using var reader = new BinaryReader(stream);
        stream.Position = file.DataOffset - sizeof(int);
        var framedLength = reader.ReadInt32();
        var end = stream.Position + framedLength;
        Assert.Equal(output.Length - 20, end);
        using var decoded = new MemoryStream();
        var ordinal = 0;
        var baselinePosition = 0;
        while (stream.Position < end)
        {
            Assert.Equal(0x9E2A83C1UL, reader.ReadUInt64());
            Assert.Equal(131072UL, reader.ReadUInt64());
            var compressed = checked((int)reader.ReadUInt64());
            var length = checked((int)reader.ReadUInt64());
            Assert.Equal((ulong)compressed, reader.ReadUInt64());
            Assert.Equal((ulong)length, reader.ReadUInt64());
            Assert.InRange(length, 1, 131072);
            var bytes = reader.ReadBytes(compressed);
            Assert.Equal(compressed, bytes.Length);
            if (ordinal < file.Chunks.Count && length == file.Chunks[ordinal].UncompressedBytes &&
                bytes.AsSpan().SequenceEqual(original.AsSpan(file.Chunks[ordinal].Offset + 48, file.Chunks[ordinal].CompressedBytes)))
                decoded.Write(baseline.AsSpan(baselinePosition, length));
            else
            {
                Assert.Equal(length + 2, compressed);
                Assert.Equal(0xCC, bytes[0]);
                Assert.Equal(6, bytes[1]);
                decoded.Write(bytes.AsSpan(2));
            }
            if (ordinal < file.Chunks.Count) baselinePosition += file.Chunks[ordinal].UncompressedBytes;
            ordinal++;
        }
        Assert.Equal(end, stream.Position);
        Assert.Equal(SlotFixture.Digest(output.AsSpan(0, output.Length - 20)), output[^20..]);
        var result = decoded.ToArray();
        Assert.Equal(result.Length - sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(result));
        return result;
    }
}
