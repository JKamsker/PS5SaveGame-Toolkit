using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedReferencePolicyTests
{
    [Fact]
    public void BuildLocaleVersionAndIdPrecedence_MatchHostPolicy()
    {
        var assembly = Combine(
            PackBuilder.WithItems("Neutral", "test.neutral", locale: null),
            PackBuilder.WithItems("English", "test.en", "en"),
            PackBuilder.WithItems("German", "test.de", "de"),
            PackBuilder.WithItems("Old build", "test.old", "en", ["1.01"]),
            PackBuilder.WithItems("New build", "test.new", "en", ["1.01"]) with { Version = "2.0.0" },
            PackBuilder.WithItems("Id tie", "test.aaa", "en", ["1.01"]) with { Version = "2.0.0" });
        var context = EmbeddedReferenceContext.Load(assembly, "test.items", null);
        Assert.Equal("English", Name(context));
        Assert.Equal("Id tie", Name(context, "1.01"));
        Assert.Equal("English", Name(context, "wrong-build"));
        Assert.Equal("German", Name(context.Pin("test.items", null, ["de-DE"])));
        Assert.Equal("Neutral", Name(context.Pin("test.items", null, ["fr"])));
        Assert.Equal("Neutral", Name(context.Pin("test.items", null, [])));
        Assert.Equal("Id tie", Name(context.Pin("test.items", null, ["de"]), "1.01"));
    }

    [Fact]
    public void ExactByteAndRecordLimits_AreAccepted_WithoutCountingManifestInDatasetBudget()
    {
        var assembly = EmbeddedReferenceRejectionTests.Resources(PackBuilder.WithItems());
        var files = assembly.Files;
        var context = EmbeddedReferenceContext.Load(assembly, "test.items", null, new()
        {
            MaxManifestBytes = files["refpacks/fixture/pack.json"].Length,
            MaxRecordsFileBytes = files["refpacks/fixture/items.json"].Length,
            MaxAssetBytes = files["refpacks/fixture/icons/a.png"].Length,
            MaxTotalBytes = files.Where(f => !f.Key.EndsWith("/pack.json", StringComparison.Ordinal)).Sum(f => f.Value.Length),
            MaxFiles = 2,
            MaxRecordsPerCatalog = 2,
            MaxImageDimension = 8,
        });
        Assert.Equal(2, context.Catalog("items")!.Count);
    }

    [Fact]
    public void EmptyAssembly_ReturnsEmptyContext_AndMalformedUnrelatedPackStillRejects()
    {
        var empty = EmbeddedReferenceContext.Load(new EmbeddedReferenceRejectionTests.ResourceAssembly([]), "test.items", null);
        Assert.Empty(empty.Packs);
        Assert.Null(empty.Catalog("items"));
        Assert.Null(empty.GetAsset("test.items:icons/a.png"));
        var malformed = new PackBuilder { Plugins = ["other.plugin"] }.Records("items.json", "{");
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(
            EmbeddedReferenceRejectionTests.Resources(malformed), "test.items", null));
    }

    [Fact]
    public void RecordHashNormalizesCrLf_AndAttributionAndAttributesSurvive()
    {
        var rows = "{\r\n\"records\":[{\"id\":\"1\",\"name\":\" Trimmed \",\"attributes\":{\"kind\":\"test\"}}]}\r\n";
        var lf = System.Text.Encoding.UTF8.GetBytes(rows.Replace("\r\n", "\n", StringComparison.Ordinal));
        var modified = new PackBuilder
        {
            Mutate = m => m["datasets"]![0]!["files"]![0]!["sha256"] = PackBuilder.Sha256(lf),
        }.Records("items.json", rows);
        var context = EmbeddedReferenceContext.Load(EmbeddedReferenceRejectionTests.Resources(modified), "test.items", null);
        var record = context.Catalog("items")!.Find("1")!;
        Assert.Equal("Trimmed", record.Name);
        Assert.Equal("test", record.Attributes["kind"]);
        Assert.Equal("Example wiki contributors", record.Dataset.Attribution.Author);
    }

    private static string? Name(IReferenceContext context, string? build = null) => context.Catalog("items", build)?.Find("1001")?.Name;

    private static EmbeddedReferenceRejectionTests.ResourceAssembly Combine(params PackBuilder[] packs) => new(
        packs.SelectMany(pack => EmbeddedReferenceRejectionTests.Resources(pack).Files.Select(kv =>
            KeyValuePair.Create(kv.Key.Replace("/fixture/", "/" + pack.Id + "/", StringComparison.Ordinal), kv.Value))).ToDictionary());
}
