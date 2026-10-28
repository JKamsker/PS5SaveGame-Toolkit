using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

internal static class ReferencePackFixture
{
    public static (Cyberpunk2077Plugin Plugin, ReferencePackStore Store, ReferenceContext Context) Embedded()
    {
        var plugin = new Cyberpunk2077Plugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return (plugin, store, store.Pin(plugin, "PPSA04027"));
    }

    public static byte[] UserPack(string catalog, object[] records, string build = "2210")
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(new { records });
        var manifest = new
        {
            schema = 1, id = "cyberpunk2077.test-user", version = "1.0.0", title = "Synthetic reference test data",
            plugins = new[] { "cyberpunk2077" }, titleIds = new[] { "PPSA04027" }, gameBuilds = new[] { build }, locale = "en",
            datasets = new[] { new { id = catalog, kind = "records", files = new[] {
                new { path = "records.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(data)) } },
                attribution = new { sourceUrl = "https://example.org/synthetic", author = "Test fixture", license = "CC0-1.0",
                    licenseUrl = "https://creativecommons.org/publicdomain/zero/1.0/", retrieved = "2026-10-28", modifications = "Original synthetic test labels" } } },
        };
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "records.json", data);
            Write(zip, "pack.json", JsonSerializer.SerializeToUtf8Bytes(manifest));
        }

        return buffer.ToArray();
    }

    public static IEnumerable<ReferenceRecord> All(IReferenceCatalog catalog)
    {
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                yield return record;
            }
        }
    }

    private static void Write(ZipArchive zip, string path, byte[] content)
    {
        using var stream = zip.CreateEntry(path).Open();
        stream.Write(content);
    }
}
