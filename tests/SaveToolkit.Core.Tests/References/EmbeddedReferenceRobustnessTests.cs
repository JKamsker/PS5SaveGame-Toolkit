using System.Text.Json.Nodes;
using SaveToolkit.Sdk.References;
using static SaveToolkit.Core.Tests.References.EmbeddedReferenceRejectionTests;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedReferenceRobustnessTests
{
    [Theory]
    [InlineData("manifest")]
    [InlineData("records-file")]
    [InlineData("asset")]
    [InlineData("total")]
    [InlineData("files")]
    [InlineData("records")]
    [InlineData("width")]
    [InlineData("height")]
    public void EachLimitAcceptsExactlyTheBoundAndRejectsOneUnitPastIt(string bound)
    {
        var assembly = Resources(PackBuilder.WithItems().Asset("icons/wide.png", Images.Png(9, 8)));
        if (bound == "height") assembly = Resources(PackBuilder.WithItems().Asset("icons/tall.png", Images.Png(8, 9)));
        var files = assembly.Files;
        var datasetBytes = files.Where(pair => !pair.Key.EndsWith("/pack.json", StringComparison.Ordinal)).Sum(pair => pair.Value.LongLength);
        var limit = bound switch
        {
            "manifest" => files["refpacks/fixture/pack.json"].LongLength,
            "records-file" => files["refpacks/fixture/items.json"].LongLength,
            "asset" => files["refpacks/fixture/icons/a.png"].LongLength,
            "total" => datasetBytes,
            "files" => 3,
            "records" => 2,
            "width" or "height" => 9,
            _ => throw new ArgumentOutOfRangeException(nameof(bound)),
        };
        EmbeddedReferencePackLimits Limits(long value) => bound switch
        {
            "manifest" => new() { MaxManifestBytes = value },
            "records-file" => new() { MaxRecordsFileBytes = value },
            "asset" => new() { MaxAssetBytes = value },
            "total" => new() { MaxTotalBytes = value },
            "files" => new() { MaxFiles = (int)value },
            "records" => new() { MaxRecordsPerCatalog = (int)value },
            "width" or "height" => new() { MaxImageDimension = (int)value },
            _ => throw new ArgumentOutOfRangeException(nameof(bound)),
        };
        Assert.Equal(2, EmbeddedReferenceContext.Load(assembly, "test.items", null, Limits(limit)).Catalog("items")!.Count);
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(assembly, Limits(limit - 1)));
    }

    [Theory]
    [InlineData("manifest", 0)]
    [InlineData("manifest", 1)]
    [InlineData("manifest", -1)]
    [InlineData("records", 0)]
    [InlineData("records", 1)]
    [InlineData("records", -1)]
    [InlineData("image", 0)]
    [InlineData("image", 7)]
    [InlineData("image", 16)]
    [InlineData("image", 23)]
    public void TruncatedPackComponentsRejectTheWholeLoad(string component, int length)
    {
        var builder = PackBuilder.WithItems();
        if (component == "records")
        {
            const string json = """{"records":[{"id":"1","name":"One"}]}""";
            builder = new PackBuilder().Records("items.json", json[..(length < 0 ? json.Length - 1 : length)]);
        }
        else if (component == "image") builder = new PackBuilder().Asset("icons/a.png", Images.Png(8, 8)[..length]);
        var assembly = Resources(builder);
        if (component == "manifest")
        {
            var files = assembly.Files.ToDictionary();
            var manifest = files["refpacks/fixture/pack.json"];
            files["refpacks/fixture/pack.json"] = manifest[..(length < 0 ? manifest.Length - 1 : length)];
            assembly = new(files);
        }
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(assembly));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateRecordKeysAcrossFilesAndDuplicateManifestPathsReject(bool duplicatePath)
    {
        var builder = new PackBuilder()
            .Records("a.json", """{"records":[{"id":"1","name":"One"}]}""")
            .Records("b.json", """{"records":[{"id":"1","name":"Other"}]}""");
        if (duplicatePath) builder = builder with
        {
            Mutate = manifest => manifest["datasets"]![0]!["files"]![1]!["path"] = "A.JSON",
        };
        var error = Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(Resources(builder)));
        Assert.Contains(duplicatePath ? "listed twice" : "duplicate id", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DatasetByteBudgetCountsCrLfBeforeNormalizationAcrossFiles()
    {
        var builder = new PackBuilder()
            .Records("a.json", "{\r\n\"records\":[{\"id\":\"1\",\"name\":\"One\"}]}\r\n")
            .Records("b.json", "{\r\n\"records\":[{\"id\":\"2\",\"name\":\"Two\"}]}\r\n");
        var assembly = Resources(builder);
        var rawBytes = assembly.Files.Where(pair => !pair.Key.EndsWith("/pack.json", StringComparison.Ordinal)).Sum(pair => pair.Value.LongLength);
        Assert.Equal(2, EmbeddedReferenceContext.Load(assembly, new() { MaxTotalBytes = rawBytes }).Pin("test.items", null).Catalog("items")!.Count);
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(assembly, new() { MaxTotalBytes = rawBytes - 1 }));
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(assembly, new() { MaxRecordsPerCatalog = 1 }));
    }

    [Theory]
    [InlineData("id", 256)]
    [InlineData("name", 512)]
    [InlineData("category", 128)]
    [InlineData("description", 8192)]
    [InlineData("attribute-count", 32)]
    [InlineData("attribute-key", 64)]
    [InlineData("attribute-value", 1024)]
    public void RecordShapeLimitsAcceptExactLengthAndRejectOnePast(string property, int bound)
    {
        PackBuilder Pack(int size)
        {
            var record = new JsonObject { ["id"] = "one", ["name"] = "One" };
            switch (property)
            {
                case "attribute-count":
                    var attributes = new JsonObject();
                    for (var i = 0; i < size; i++) attributes.Add("key" + i, "value");
                    record["attributes"] = attributes;
                    break;
                case "attribute-key": record["attributes"] = new JsonObject { [new string('k', size)] = "value" }; break;
                case "attribute-value": record["attributes"] = new JsonObject { ["key"] = new string('v', size) }; break;
                default: record[property] = new string('x', size); break;
            }
            return new PackBuilder().Records("items.json", new JsonObject { ["records"] = new JsonArray(record) }.ToJsonString());
        }
        Assert.Equal(1, EmbeddedReferenceContext.Load(Resources(Pack(bound)), "test.items", null).Catalog("items")!.Count);
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(Resources(Pack(bound + 1))));
    }

    [Fact]
    public void CaseDistinctRecordKeysRemainIndependent()
    {
        var assembly = Resources(new PackBuilder().Records("items.json",
            """{"records":[{"id":"A","name":"Upper"},{"id":"a","name":"Lower"}]}"""));
        var catalog = EmbeddedReferenceContext.Load(assembly, "test.items", null).Catalog("items")!;
        Assert.Equal(2, catalog.Count);
        Assert.Equal("Upper", catalog.Find("A")!.Name);
        Assert.Equal("Lower", catalog.Find("a")!.Name);
    }

    [Fact]
    public void RepeatedPinSharesCatalogAndAssetsWithoutWideningTheSelection()
    {
        var context = EmbeddedReferenceContext.Load(Resources(PackBuilder.WithItems() with { TitleIds = ["PPSA12345"] }));
        var first = context.Pin("TEST.ITEMS", "ppsa12345");
        var second = first.Pin("test.items", "PPSA12345");
        Assert.Single(second.Packs);
        Assert.Same(first.Catalog("items"), second.Catalog("items"));
        var assetId = first.Catalog("items")!.Find("1001")!.AssetId!;
        Assert.Same(first.GetAsset(assetId), second.GetAsset(assetId));
        Assert.Empty(first.Pin("other.plugin", "PPSA12345").Pin("test.items", "PPSA12345").Packs);
        Assert.Empty(first.Pin("test.items", null).Packs);
        Assert.Single(context.Packs);
    }

    [Fact]
    public async Task ConcurrentLoadsUseIndependentStreamsAndLeavePinnedContextsStable()
    {
        const int workers = 16;
        var assembly = Resources(PackBuilder.WithItems());
        var shared = EmbeddedReferenceContext.Load(assembly);
        var expected = shared.Pin("test.items", null).Catalog("items")!;
        using var start = new ManualResetEventSlim();
        var jobs = Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
        {
            start.Wait();
            var loaded = EmbeddedReferenceContext.Load(assembly, "test.items", null);
            var pinned = shared.Pin("test.items", null);
            Assert.Same(expected, pinned.Catalog("items"));
            Assert.Equal(expected.Find("1001")!.Name, loaded.Catalog("items")!.Find("1001")!.Name);
            var assetId = expected.Find("1001")!.AssetId!;
            Assert.Equal(pinned.GetAsset(assetId)!.Content.ToArray(), loaded.GetAsset(assetId)!.Content.ToArray());
            return loaded;
        })).ToArray();
        start.Set();
        var contexts = await Task.WhenAll(jobs);
        Assert.All(contexts, context => Assert.Single(context.Packs));
        Assert.NotSame(contexts[0].Catalog("items"), contexts[1].Catalog("items"));
    }

    [Fact]
    public void NormalizedResourceNameCollisionRejectsInsteadOfSilentlyChoosingOne()
    {
        var files = Resources(PackBuilder.WithItems()).Files.ToDictionary();
        const string path = "refpacks/fixture/pack.json";
        var otherManifest = JsonNode.Parse(files[path])!;
        otherManifest["id"] = "other.items";
        files.Add(path.Replace('/', '\\'), System.Text.Encoding.UTF8.GetBytes(otherManifest.ToJsonString()));
        EmbeddedReferenceAmbiguityTests.AssertRejected(new ResourceAssembly(files), path, path.Replace('/', '\\'));
    }

    [Fact]
    public void DuplicateJsonPropertiesCannotSilentlyDiscardEarlierRecords()
    {
        var builder = new PackBuilder().Records("items.json",
            """{"records":[{"id":"1","name":"Lost"}],"records":[{"id":"2","name":"Kept"}]}""");
        EmbeddedReferenceAmbiguityTests.AssertRejected(Resources(builder), "records", "items.json", "$.records");
    }
}
