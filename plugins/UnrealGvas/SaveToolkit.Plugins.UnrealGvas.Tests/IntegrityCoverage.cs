using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

internal sealed record IntegrityCapture(string Hash, string Role, uint Stored, IReadOnlyDictionary<string, byte[]> Ranges)
{
    public static IntegrityCapture Read(ISaveFile file, string checksumName)
    {
        var bytes = file.ReadAllBytes();
        var parsed = GvasFile.Parse(bytes);
        var checksum = GvasFile.Descendants(parsed.Properties).Single(node => node.Name == checksumName);
        var start = TagStart(bytes, checksum);
        var end = checksum.Offset + checksum.Length;
        var headerEnd = TagStart(bytes, parsed.Properties[0]);
        var zeroed = bytes.ToArray();
        zeroed.AsSpan(checksum.Offset, sizeof(uint)).Clear();
        var omittedValue = Replace(bytes, checksum.Offset, sizeof(uint), []);
        var omittedTag = Replace(bytes, start, end - start, []);
        var ranges = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["file/original"] = bytes,
            ["file/zero-value"] = zeroed,
            ["file/omit-value"] = omittedValue,
            ["file/omit-tag"] = omittedTag,
            ["properties/original"] = bytes[headerEnd..],
            ["properties/zero-value"] = zeroed[headerEnd..],
            ["properties/omit-value"] = omittedValue[headerEnd..],
            ["properties/omit-tag"] = omittedTag[headerEnd..],
            ["before-value"] = bytes[..checksum.Offset],
            ["after-value"] = bytes[end..],
            ["payloads/omit-checksum"] = parsed.Properties.Where(node => node != checksum)
                .SelectMany(node => bytes.AsSpan(node.Scalar?.Offset ?? node.Offset, node.Scalar?.Length ?? node.Length).ToArray()).ToArray(),
        };
        foreach (var root in parsed.Properties.Where(node => node != checksum))
            ranges.Add("root/" + root.Name, bytes.AsSpan(root.Offset, root.Length).ToArray());
        if (checksumName == "Crc32") AddActorRanges(parsed, bytes, ranges);
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), file.Directory.Name,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(checksum.Offset)), ranges);
    }

    private static void AddActorRanges(GvasFile parsed, byte[] bytes, Dictionary<string, byte[]> ranges)
    {
        var actor = parsed.Properties.Single(node => node.Name == "CompressedActorRecords");
        var compressed = bytes.AsSpan(actor.Offset + sizeof(int), actor.Length - sizeof(int)).ToArray();
        using var input = new MemoryStream(compressed, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output); // Only immutable, known test samples; production decoding is bounded separately.
        var plain = output.ToArray();
        Assert.Equal((Int128)plain.Length, parsed.Properties.Single(node => node.Name == "UncompressedActorRecordsSize").Scalar!.Value);
        ranges.Add("actors/zlib", compressed);
        ranges.Add("actors/deflate", compressed[2..^4]);
        ranges.Add("actors/decoded", plain);
        ranges.Add("actors/decoded-without-prefix", plain[sizeof(int)..]);
        ranges.Add("file/decoded-array-substitution", Replace(bytes, actor.Offset, actor.Length, plain));
    }

    private static int TagStart(byte[] bytes, GvasNode node)
    {
        var signature = GvasFixture.Body(writer => { GvasFixture.String(writer, node.Name); GvasFixture.String(writer, node.Type); });
        var start = bytes.AsSpan(0, node.Offset).LastIndexOf(signature);
        Assert.True(start >= 0, $"Missing tag for {node.Name}.");
        return start;
    }

    private static byte[] Replace(byte[] bytes, int offset, int length, byte[] replacement) =>
        [.. bytes.AsSpan(0, offset), .. replacement, .. bytes.AsSpan(offset + length)];
}
