using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithReferenceTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData("en")]
    [InlineData("de-DE")]
    public void PackResolvesEnglishFallbackWithPerRecordAttribution(string locale)
    {
        var store = new ReferencePackStore([_plugin], new ReferencePackOptions { PreferredLocales = [locale] });
        Assert.Empty(store.LoadErrors);
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(store.Pin(_plugin, "PPSA10665").Catalog(FaithCatalog.ItemsId));
        Assert.Equal(879, catalog.Count);
        Assert.Equal("Defender", catalog.Find("100029")?.Name);
        Assert.Equal("weapon", catalog.Find("100029")?.Category);
        Assert.Equal("Gil", catalog.Find("1")?.Name);
        Assert.Null(catalog.Find("900000"));
        Assert.Null(catalog.Find("999999"));
        Assert.Equal("Rune Blade +1", catalog.Find("100027")?.Name);
        Assert.Equal("Iron Filings", catalog.Find("110")?.Name);
        var records = Enumerable.Range(0, (catalog.Count + 199) / 200)
            .SelectMany(page => catalog.Search(new ReferenceQuery { Offset = page * 200, Limit = 200 }).Items).ToArray();
        Assert.Equal(catalog.Count, records.Length);
        Assert.All(records, record =>
        {
            Assert.Equal("No licence published; reused with attribution per owner decision 2026-10-28", record.Dataset.Attribution.License);
            Assert.Contains("4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03", record.Source);
            Assert.Equal("Inferred", record.Attributes["confidence"]);
            Assert.NotEmpty(record.Attributes["identifierSource"]);
            Assert.NotEmpty(record.Description!);
            Assert.Null(record.Limit); // No sourced current gameplay caps.
        });
    }

    [Fact]
    public void FallbackAndHostCatalogAgreeAndIdentityReplacementPersists()
    {
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()));
        var file = FaithTestData.File(bytes);
        var context = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        using var withPack = _plugin.Open(file, context);
        using var withoutPack = _plugin.Open(file);
        var actual = FaithViewFields.All(withPack.BuildView()).First(field => field.ValueId == "inventory/0/item");
        var fallback = FaithViewFields.All(withoutPack.BuildView()).First(field => field.ValueId == actual.ValueId);
        Assert.Equal(actual.Label, fallback.Label);
        Assert.Equal(actual.Description, fallback.Description);
        Assert.IsType<ReferenceSpec>(actual.Spec);
        Assert.False(actual.Spec.ReadOnly);
        Assert.Equal("1", actual.Value.AsString());
        Assert.Contains("CC-BY-SA", actual.Description);
        Assert.True(withPack.ApplyEdits([new ValueEdit(actual.ValueId, FieldValue.Of("5"))]).Succeeded);
        Assert.Equal("5", withPack.GetValue(actual.ValueId).AsString());
        Assert.True(withPack.ApplyEdits([new ValueEdit(actual.ValueId, FieldValue.Of("1"))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(withPack.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("<i>A Rose Is a Rose</i>", "A Rose Is a Rose")]
    [InlineData("<color=h_phoenix>The Will of Fire</color>", "The Will of Fire")]
    [InlineData("<script>literal</script>&amp;", "<script>literal</script>&amp;")]
    public void KnownGamePresentationIsPlainTextAndUnknownMarkupRemainsLiteral(string raw, string expected) =>
        Assert.Equal(expected, FaithCatalog.PlainText(raw));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackRejectsMissingAndDuplicateIds(bool duplicate)
    {
        var records = duplicate
            ? "{\"records\":[{\"id\":\"5\",\"name\":\"Potion\"},{\"id\":\"5\",\"name\":\"Potion\"}]}"
            : "{\"records\":[{\"name\":\"Potion\"}]}";
        var assembly = typeof(FinalFantasy16Plugin).Assembly;
        using var manifestStream = assembly.GetManifestResourceStream("refpacks/ffxvi.wiki.en/pack.json")!;
        using var reader = new StreamReader(manifestStream);
        var manifest = JsonNode.Parse(reader.ReadToEnd())!;
        manifest["id"] = "ffxvi.invalid";
        manifest["datasets"]![0]!["files"]![0]!["sha256"] =
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(records)));
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "pack.json", manifest.ToJsonString());
            Write(zip, "items.json", records);
        }
        var error = Assert.Throws<ReferencePackException>(() => new ReferencePackStore([_plugin]).Import(memory.ToArray(), "invalid.zip"));
        Assert.Contains(duplicate ? "duplicate id '5'" : "id is required", error.Message);
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
