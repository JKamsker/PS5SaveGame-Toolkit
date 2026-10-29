using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedReferenceRejectionTests
{
    [Theory]
    [InlineData("hash", "hash mismatch")]
    [InlineData("path", "unsafe path")]
    [InlineData("attribution", "attribution is required")]
    [InlineData("json", "is not valid")]
    [InlineData("missing", "listed but missing")]
    [InlineData("record", "duplicate id")]
    [InlineData("image", "not a PNG")]
    public void InvalidEmbeddedPacks_RejectWholeLoad(string kind, string reason)
    {
        var builder = kind switch
        {
            "hash" => PackBuilder.WithItems() with { Mutate = m => m["datasets"]![0]!["files"]![0]!["sha256"] = new string('0', 64) },
            "path" => PackBuilder.WithItems() with { Mutate = m => m["datasets"]![0]!["files"]![0]!["path"] = "../items.json" },
            "attribution" => PackBuilder.WithItems() with { Mutate = m => m["datasets"]![0]!.AsObject().Remove("attribution") },
            "json" => new PackBuilder().Records("items.json", "{"),
            "missing" => PackBuilder.WithItems() with { Mutate = m => m["datasets"]![0]!["files"]![0]!["path"] = "missing.json" },
            "record" => new PackBuilder().Records("items.json", """{"records":[{"id":"1","name":"A"},{"id":"1","name":"B"}]}"""),
            "image" => new PackBuilder().Asset("icons/a.png", Images.Svg()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Assert.Contains(reason, Assert.Throws<EmbeddedReferencePackException>(() =>
            EmbeddedReferenceContext.Load(Resources(builder), "test.items", null)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("records-file")]
    [InlineData("asset")]
    [InlineData("total")]
    [InlineData("records")]
    [InlineData("files")]
    [InlineData("dimensions")]
    public void EveryEmbeddedBudget_IsEnforced(string budget)
    {
        var limits = budget switch
        {
            "manifest" => new EmbeddedReferencePackLimits { MaxManifestBytes = 1 },
            "records-file" => new EmbeddedReferencePackLimits { MaxRecordsFileBytes = 1 },
            "asset" => new EmbeddedReferencePackLimits { MaxAssetBytes = 1 },
            "total" => new EmbeddedReferencePackLimits { MaxTotalBytes = 1 },
            "records" => new EmbeddedReferencePackLimits { MaxRecordsPerCatalog = 1 },
            "files" => new EmbeddedReferencePackLimits { MaxFiles = 1 },
            "dimensions" => new EmbeddedReferencePackLimits { MaxImageDimension = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(budget)),
        };
        Assert.Throws<EmbeddedReferencePackException>(() =>
            EmbeddedReferenceContext.Load(Resources(PackBuilder.WithItems()), "test.items", null, limits));
    }

    [Fact]
    public void Discovery_NormalizesWindowsResourceNames_AndRejectsDuplicateIds()
    {
        var original = Resources(PackBuilder.WithItems());
        var windows = new ResourceAssembly(original.Files.ToDictionary(kv => kv.Key.Replace('/', '\\'), kv => kv.Value));
        Assert.Equal("User Potion", EmbeddedReferenceContext.Load(windows, "test.items", null).Catalog("items")!.Find("1001")!.Name);
        var duplicates = original.Files.Concat(original.Files.Select(kv =>
            KeyValuePair.Create(kv.Key.Replace("refpacks/fixture/", "refpacks/second/", StringComparison.Ordinal), kv.Value))).ToDictionary();
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(new ResourceAssembly(duplicates), "test.items", null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MalformedOrAbsentManifest_RejectsDiscoveredFolder(bool malformed)
    {
        var resources = Resources(PackBuilder.WithItems()).Files.ToDictionary();
        if (malformed) resources["refpacks/fixture/pack.json"] = Encoding.UTF8.GetBytes("{");
        else resources.Remove("refpacks/fixture/pack.json");
        var error = Assert.Throws<EmbeddedReferencePackException>(() =>
            EmbeddedReferenceContext.Load(new ResourceAssembly(resources), "test.items", null));
        Assert.Contains(malformed ? "is not valid" : "has no pack.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogRecordBudget_AppliesAcrossFiles()
    {
        var builder = new PackBuilder()
            .Records("first.json", """{"records":[{"id":"1","name":"A"}]}""")
            .Records("second.json", """{"records":[{"id":"2","name":"B"}]}""");
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(
            Resources(builder), "test.items", null, new() { MaxRecordsPerCatalog = 1 }));
    }

    internal static ResourceAssembly Resources(PackBuilder builder)
    {
        using var zip = new ZipArchive(new MemoryStream(builder.Zip()));
        return new ResourceAssembly(zip.Entries.ToDictionary(e => "refpacks/fixture/" + e.FullName, e =>
        {
            using var source = e.Open();
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            return buffer.ToArray();
        }));
    }

    // An Assembly test double supplies real manifest-resource streams without disk fixtures or compilation.
    internal sealed class ResourceAssembly(Dictionary<string, byte[]> files) : Assembly
    {
        public IReadOnlyDictionary<string, byte[]> Files => files;
        public override AssemblyName GetName() => new("SyntheticReferences");
        public override string[] GetManifestResourceNames() => [.. files.Keys];
        public override Stream? GetManifestResourceStream(string name) => files.TryGetValue(name, out var bytes) ? new MemoryStream(bytes) : null;
    }
}
