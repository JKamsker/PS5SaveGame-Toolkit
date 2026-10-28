using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SaveToolkit.Core.Tests.References;

/// <summary>Builds pack zips for tests with correct hashes; <see cref="Mutate"/> breaks them on purpose.</summary>
internal sealed record PackBuilder
{
    private readonly List<(string Path, byte[] Content)> _records = [];
    private readonly List<(string Path, byte[] Content)> _assets = [];
    private readonly List<(string Path, byte[] Content)> _extra = [];

    public string Id { get; init; } = "user.items";
    public string Version { get; init; } = "1.0.0";
    public string? Locale { get; init; } = "en";
    public string[] Plugins { get; init; } = ["test.items"];
    public string[] TitleIds { get; init; } = [];
    public string[] GameBuilds { get; init; } = [];
    public Action<JsonObject>? Mutate { get; init; }

    /// <summary>Default record set: one item with an icon.</summary>
    public static PackBuilder WithItems(string name = "User Potion", string id = "user.items", string? locale = "en", string[]? builds = null) =>
        new PackBuilder { Id = id, Locale = locale, GameBuilds = builds ?? [] }
            .Records("items.json", $$"""{ "records": [ { "id": "1001", "name": "{{name}}", "category": "consumable", "limit": 10, "assetId": "icons/a.png" }, { "id": "3001", "name": "Shield", "category": "armour" } ] }""")
            .Asset("icons/a.png", Images.Png(8, 8));

    public PackBuilder Records(string path, string json)
    {
        _records.Add((path, Encoding.UTF8.GetBytes(json)));
        return this;
    }

    public PackBuilder Asset(string path, byte[] content)
    {
        _assets.Add((path, content));
        return this;
    }

    /// <summary>A zip entry the manifest does not list (or lists under a different hash).</summary>
    public PackBuilder Extra(string path, byte[] content)
    {
        _extra.Add((path, content));
        return this;
    }

    public JsonObject Manifest()
    {
        var datasets = new JsonArray();
        if (_records.Count > 0)
        {
            datasets.Add(Dataset("items", "records", _records));
        }

        if (_assets.Count > 0)
        {
            datasets.Add(Dataset("icons", "assets", _assets));
        }

        var manifest = new JsonObject
        {
            ["schema"] = 1,
            ["id"] = Id,
            ["version"] = Version,
            ["title"] = $"Pack {Id}",
            ["plugins"] = new JsonArray([.. Plugins.Select(p => (JsonNode)p)]),
            ["titleIds"] = new JsonArray([.. TitleIds.Select(p => (JsonNode)p)]),
            ["gameBuilds"] = new JsonArray([.. GameBuilds.Select(p => (JsonNode)p)]),
            ["locale"] = Locale,
            ["datasets"] = datasets,
        };
        Mutate?.Invoke(manifest);
        return manifest;
    }

    public byte[] Zip(string rootFolder = "")
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, rootFolder + "pack.json", Encoding.UTF8.GetBytes(Manifest().ToJsonString()));
            foreach (var (path, content) in _records.Concat(_assets).Concat(_extra))
            {
                Write(zip, rootFolder + path, content);
            }
        }

        return buffer.ToArray();
    }

    public static JsonObject Attribution() => new()
    {
        ["sourceUrl"] = "https://example.org/wiki/items",
        ["author"] = "Example wiki contributors",
        ["license"] = "CC-BY-SA-4.0",
        ["licenseUrl"] = "https://creativecommons.org/licenses/by-sa/4.0/",
        ["retrieved"] = "2026-10-01",
        ["modifications"] = "Extracted id and name columns.",
    };

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static JsonObject Dataset(string id, string kind, List<(string Path, byte[] Content)> files) => new()
    {
        ["id"] = id,
        ["kind"] = kind,
        ["files"] = new JsonArray([.. files.Select(f => (JsonNode)new JsonObject { ["path"] = f.Path, ["sha256"] = Sha256(kind == "records" ? LfOnly(f.Content) : f.Content) })]),
        ["attribution"] = Attribution(),
    };

    // The loader hashes record files with CRLF normalised to LF, so the expected hash must not depend
    // on how git checked out the raw string literals in these tests.
    private static byte[] LfOnly(byte[] content) =>
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content).Replace("\r\n", "\n", StringComparison.Ordinal));

    private static void Write(ZipArchive zip, string path, byte[] content)
    {
        using var stream = zip.CreateEntry(path).Open();
        stream.Write(content);
    }
}

/// <summary>Minimal valid image headers (only what the sniffer reads, plus a body).</summary>
internal static class Images
{
    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    public static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xD9,
    ];

    public static byte[] Svg() => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>");
}
