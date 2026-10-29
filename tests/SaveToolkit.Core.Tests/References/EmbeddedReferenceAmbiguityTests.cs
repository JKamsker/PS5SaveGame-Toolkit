using System.IO.Compression;
using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using static SaveToolkit.Core.Tests.References.EmbeddedReferenceRejectionTests;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedReferenceAmbiguityTests
{
    private const string ResourceRoot = "refpacks/fixture/";

    [Theory]
    [InlineData("refpacks/fixture/PACK.JSON")]
    [InlineData("REFPACKS/fixture/pack.json")]
    [InlineData("refpacks/FIXTURE/pack.json")]
    [InlineData("refpacks\\fixture\\pack.json")]
    [InlineData("refpacks\\FIXTURE\\PACK.JSON")]
    [InlineData("refpacks/fixture/ITEMS.JSON")]
    [InlineData("refpacks/fixture/icons/A.PNG")]
    public void CollidingManifestRecordsAndAssetsRejectInEitherDiscoveryOrder(string duplicate)
    {
        var files = Resources(PackBuilder.WithItems()).Files.ToDictionary();
        var original = duplicate.Replace('\\', '/').ToLowerInvariant();
        files.Add(duplicate, files[original]);
        AssertRejected(new(files), original, duplicate, "same path");
        AssertRejected(new(files.Reverse().ToDictionary()), original, duplicate, "same path");
    }

    [Fact]
    public void UnlistedResourceCollisionRejectsOnlyAffectedFoldersInCore()
    {
        var files = Resources(PackBuilder.WithItems()).Files.ToDictionary();
        const string first = ResourceRoot + "unused.txt";
        const string second = ResourceRoot + "UNUSED.TXT";
        files.Add(first, "first"u8.ToArray());
        files.Add(second, "second"u8.ToArray());
        foreach (var (name, bytes) in Resources(PackBuilder.WithItems(id: "other.items")).Files)
            files.Add(name.Replace("/fixture/", "/other/", StringComparison.Ordinal), bytes);

        var assembly = new ResourceAssembly(files);
        var sdk = Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(assembly));
        var store = new ReferencePackStore(null, [assembly]);
        Assert.Equal("other.items", Assert.Single(store.Packs).Id);
        Assert.Equal(sdk.Message, Assert.Single(store.LoadErrors).Message);
        Assert.Contains(first, sdk.Message, StringComparison.Ordinal);
        Assert.Contains(second, sdk.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id", "id", "$.id")]
    [InlineData("id", "ID", "$.ID")]
    [InlineData("datasets", "datasets", "$.datasets")]
    [InlineData("path", "path", "$.datasets[0].files[0].path")]
    [InlineData("license", "license", "$.datasets[0].attribution.license")]
    public void DuplicateManifestPropertiesRejectAtEveryNestingLevel(string property, string duplicate, string location)
    {
        var files = Resources(PackBuilder.WithItems()).Files.ToDictionary();
        var manifest = Encoding.UTF8.GetString(files[ResourceRoot + "pack.json"]);
        // Keep the later, valid value, which previously hid the earlier value without an error.
        manifest = manifest.Replace($"\"{property}\":", $"\"{property}\":null,\"{duplicate}\":", StringComparison.Ordinal);
        files[ResourceRoot + "pack.json"] = Encoding.UTF8.GetBytes(manifest);
        var assembly = new ResourceAssembly(files);
        AssertRejected(assembly, duplicate, "pack.json", location);
        AssertImportRejected(assembly, duplicate, "pack.json", location);
    }

    [Theory]
    [InlineData("""{"records":[],"Records":[{"id":"1","name":"One"}]}""", "Records", "$.Records")]
    [InlineData("""{"records":[{"id":"1","name":"Lost","name":"Kept"}]}""", "name", "$.records[0].name")]
    [InlineData("""{"records":[{"id":"1","name":"Lost","n\u0061me":"Kept"}]}""", "name", "$.records[0].name")]
    [InlineData("""{"records":[{"id":"1","name":"One","attributes":{"kind":"Lost","kind":"Kept"}}]}""", "kind", "$.records[0].attributes.kind")]
    public void DuplicateCatalogPropertiesRejectIncludingEscapedNamesAndDictionaryKeys(string json, string property, string location)
    {
        var assembly = Resources(new PackBuilder().Records("items.json", json));
        AssertRejected(assembly, property, "items.json", location);
        AssertImportRejected(assembly, property, "items.json", location);
    }

    [Fact]
    public void SeparateObjectsCaseDistinctAttributeKeysBomCommentsAndTrailingCommasRemainValid()
    {
        const string json = "\uFEFF" + """
            { /* allowed comment */ "records": [
              {"id":"A","name":"Upper","attributes":{"kind":"lower","Kind":"upper"}},
              {"id":"a","name":"Lower","attributes":{"kind":"other"}},
            ], }
            """;
        var assembly = Resources(new PackBuilder().Records("items.json", json));
        var sdk = EmbeddedReferenceContext.Load(assembly, "test.items", null).Catalog("items")!;
        var store = new ReferencePackStore(null, [assembly]);
        Assert.Empty(store.LoadErrors);
        var core = Assert.Single(store.Packs).Catalog("items")!;
        var imported = new ReferencePackStore([]).Import(Zip(assembly), "valid.zip").Catalog("items")!;
        Assert.Equal(2, sdk.Count);
        Assert.Equal("lower", sdk.Find("A")!.Attributes["kind"]);
        Assert.Equal("upper", sdk.Find("A")!.Attributes["Kind"]);
        foreach (var key in new[] { "A", "a" })
        {
            EmbeddedReferenceEquivalenceTests.EqualJson(core.Find(key), sdk.Find(key));
            // Import changes pack origin, but keeps the decoded records and attribution.
            EmbeddedReferenceEquivalenceTests.EqualJson(core.Find(key), imported.Find(key));
        }
    }

    internal static void AssertRejected(ResourceAssembly assembly, params string[] details)
    {
        var sdk = Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(assembly));
        var store = new ReferencePackStore(null, [assembly]);
        Assert.Empty(store.Packs);
        Assert.NotEmpty(store.LoadErrors);
        Assert.All(store.LoadErrors, error => Assert.Equal(sdk.Message, error.Message));
        Assert.All(details, detail => Assert.True(sdk.Message.Contains(detail, StringComparison.Ordinal), sdk.Message));
    }

    private static void AssertImportRejected(ResourceAssembly assembly, params string[] details)
    {
        var error = Assert.Throws<ReferencePackException>(() => new ReferencePackStore([]).Import(Zip(assembly), "ambiguous.zip"));
        Assert.All(details, detail => Assert.Contains(detail, error.Message, StringComparison.Ordinal));
    }

    private static byte[] Zip(ResourceAssembly assembly)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in assembly.Files)
            {
                using var stream = zip.CreateEntry(name[ResourceRoot.Length..]).Open();
                stream.Write(bytes);
            }
        }
        return buffer.ToArray();
    }
}
