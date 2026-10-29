using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class EmbeddedCatalogCompatibilityTests
{
    [Theory]
    // Captured from the private decoder, including record order and complete dataset attribution.
    [InlineData("PPSA01911", "AB46DFE774435B1D18D0106A1B511ACDC184F478B3C8C9D7A23E70F31FAF539C")]
    [InlineData("PPSA08664", "488B78B9966579D3C48CE4C0A8AD3169747AB1B285BC2EBC630B52CFE8E1CFFB")]
    [InlineData("PPSA18936", "488B78B9966579D3C48CE4C0A8AD3169747AB1B285BC2EBC630B52CFE8E1CFFB")]
    public void CompleteFallbackRecordsKeepTheirPreMigrationFingerprint(string title, string expected)
        => AssertFallbackFingerprint(title, expected);

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("th-TH")]
    public void FallbackFingerprintRestoresAmbientCulture(string cultureName)
    {
        using var ambient = new TestCulture(CultureInfo.GetCultureInfo(cultureName));
        AssertFallbackFingerprint("PPSA01911", "AB46DFE774435B1D18D0106A1B511ACDC184F478B3C8C9D7A23E70F31FAF539C");
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
    }

    private static void AssertFallbackFingerprint(string title, string expected)
    {
        // The recorded payload includes localized attribution display dates; keep its original calendar.
        using var culture = new TestCulture(CultureInfo.InvariantCulture);
        var schema = Ff7Schema.ForTitle(title)!;
        var transcript = new StringBuilder();
        foreach (var id in new[] { "ff7r-items", "ff7r-materia", "ff7r-weapons" })
        {
            transcript.AppendLine(id);
            foreach (var record in EmbeddedCatalogs.Records(schema, id))
            {
                transcript.AppendLine(JsonSerializer.Serialize(record with
                {
                    Attributes = record.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                }));
            }
        }
        AssertFingerprint(expected, transcript);
    }

    [Fact]
    public void EveryEmbeddedImageKeepsItsBytesContentTypeAndProvenance()
    {
        var transcript = new StringBuilder();
        var assembly = typeof(FinalFantasy7Plugin).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".webp", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(22, resources.Length);
        foreach (var resource in resources)
        {
            var parts = resource["refpacks/".Length..].Split('/', 2);
            var assetId = $"{parts[0]}:{parts[1]}";
            var asset = Assert.IsType<AssetContent>(EmbeddedCatalogs.Asset(assetId));
            using var stream = assembly.GetManifestResourceStream(resource)!;
            Assert.Equal(SHA256.HashData(stream), SHA256.HashData(asset.Content.Span));
            var normalizedId = assetId.Replace('\\', '/');
            var normalizedResource = resource.Replace('\\', '/');
            Assert.Equal(assembly.GetManifestResourceNames().Contains(normalizedResource, StringComparer.Ordinal),
                EmbeddedCatalogs.Asset(normalizedId) is not null);
            transcript.AppendLine(normalizedId);
            transcript.AppendLine(asset.ContentType);
            transcript.AppendLine(Convert.ToHexString(SHA256.HashData(asset.Content.Span)));
            transcript.AppendLine(JsonSerializer.Serialize(EmbeddedCatalogs.AssetProvenance(normalizedId)));
        }
        AssertFingerprint("ED016845DA376F2A54FFC52C400BB379AC3C95C8615F11C4F83DE64999E77797", transcript);
    }

    [Theory]
    [InlineData("ff7r.rebirth.en:icons/missing.webp", "icons")]
    [InlineData("ff7r.rebirth.en:maps/missing.webp", "maps")]
    [InlineData("ff7r.remake.en:maps/missing.webp", "maps")]
    [InlineData("unknown:icons/missing.webp", null)]
    [InlineData("ff7r.rebirth.en:missing.webp", null)]
    [InlineData("ff7r.rebirth.en:", null)]
    [InlineData("malformed", null)]
    public void MissingImagesRetainPathBasedProvenance(string assetId, string? datasetId)
    {
        Assert.Null(EmbeddedCatalogs.Asset(assetId));
        Assert.Equal(datasetId, EmbeddedCatalogs.AssetProvenance(assetId)?.Id);
    }

    private static void AssertFingerprint(string expected, StringBuilder transcript)
    {
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            transcript.ToString().ReplaceLineEndings("\n"))));
        Assert.True(expected == actual, $"Fingerprint: expected {expected}; actual {actual}.");
    }
}
