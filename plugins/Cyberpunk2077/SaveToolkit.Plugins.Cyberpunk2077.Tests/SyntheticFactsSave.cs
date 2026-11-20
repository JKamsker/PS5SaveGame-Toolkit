using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

/// <summary>
/// An authored CSAV (no sample bytes) with the verified header versions and one large FactsTable stored
/// in a 256 KiB and a smaller compressed chunk, so a fact edit re-encodes a chunk larger than 64 KiB.
/// </summary>
internal static class SyntheticFactsSave
{
    public const int FactCount = 40_000;
    public const int FirstChunkSize = 256 * 1024;

    /// <summary>The value id of fact 0, whose value lies in the first (256 KiB) chunk.</summary>
    public const string FirstFactValueId = "facts/0/0";

    public static byte[] Create()
    {
        var payload = Payload();
        var bytes = CsavCorruptionFixture.Create(payload, [new("FactsTable", -1, -1, 0, payload.Length)],
            [FirstChunkSize, payload.Length - FirstChunkSize], compressed: true);
        // Save, game and archive versions of the verified schema (offsets 4, 8 and 21), so facts are editable.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 265);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 2210);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(21), 195);
        return bytes;
    }

    /// <summary>Queries the facts collection (which binds its value fields) and returns fact 0's value field.</summary>
    public static FieldNode FirstFactField(ISaveDocument document)
    {
        var page = ((IQueryableDocument)document).Query("facts", new QueryableCollectionQuery { Limit = 1 });
        return (FieldNode)page.Rows[0].Cells["value"];
    }

    public static byte[] Payload()
    {
        // FactsTable node data: node ID, packed count, then every hash followed by every value.
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(0);
        CsavCorruptionFixture.WritePacked(writer, FactCount);
        var random = new Random(2077);
        var pool = Enumerable.Range(0, 300).Select(_ => (uint)random.Next()).ToArray();
        for (var index = 0; index < FactCount; index++)
            writer.Write(random.Next(8) == 0 ? (uint)random.Next() : pool[random.Next(pool.Length)] + (uint)(index % 4));
        for (var index = 0; index < FactCount; index++)
            writer.Write(index == 0 ? 1U : random.Next(8) switch { 0 => (uint)random.Next(2, 6), < 5 => 0U, _ => 1U });
        writer.Flush();
        return output.ToArray();
    }
}
