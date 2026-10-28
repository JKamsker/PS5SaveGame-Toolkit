using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class CompactReferenceTests
{
    // Independently computed from c63ba1b's uncompressed records: identities, names and all used facts.
    // Constants/provenance/derivable fields are deliberately not part of these semantic digests.
    [Theory]
    [InlineData(CommunityReferences.ItemsCatalogId, "1694aaaccfafab514808b6616a810e002ad36c1a343bff6cf51e33ae39153742")]
    [InlineData(CommunityReferences.FlagsCatalogId, "98a651207379a3b0d50c0a50dea3cd9ab0fd11955e0ebe454a5daf0fe8dce2a7")]
    [InlineData(CommunityReferences.ParamLayoutsCatalogId, "54eabf3a8b364cb849569ceeb46318eb4a25facbd1256322084a8b7b91dd1d34")]
    public void CompactionPreservesEveryUsedFactFromThePreviousCommit(string catalogId, string expected)
    {
        var catalog = CommunityReferenceTests.Context().Catalog(catalogId)!;
        string[] columns = catalogId switch
        {
            CommunityReferences.ItemsCatalogId => ["groups", "contentGroups", "editorGroups", "editorMembershipOccurrences"],
            CommunityReferences.FlagsCatalogId => ["eventId", "region", "regionId", "sourceCategory", "contentGroup",
                "flagRole", "offset", "bit", "sourceOccurrences"],
            _ => ["paramType", "dataVersion", "formatVersion", "fieldCount"],
        };
        var text = new StringBuilder();
        foreach (var row in CommunityReferenceTests.Read(catalog).OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            text.Append(row.Key).Append('\t').Append(row.Name).Append('\t')
                .Append(catalogId == CommunityReferences.FlagsCatalogId ? "event-flag" : row.Category);
            foreach (var column in columns) text.Append('\t').Append(row.Attributes.GetValueOrDefault(column, ""));
            text.Append('\n');
        }
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
    }

    [Fact]
    public void CompactFlagContractIsExplicitAndMalformedMappingsStillFailClosed()
    {
        var context = CommunityReferenceTests.Context();
        var flags = context.Catalog(CommunityReferences.FlagsCatalogId)!;
        var original = flags.Find("event-flag:76100:668b1d7db931")!;
        Assert.Null(original.Source);
        Assert.Null(original.Category);
        Assert.DoesNotContain("schema", original.Attributes.Keys);
        Assert.NotNull(ProgressReferences.Parse(original));
        var wrongDataset = original with { Dataset = context.Catalog(CommunityReferences.ItemsCatalogId)!.Dataset };
        Assert.Null(ProgressReferences.Parse(wrongDataset));
        var wrongMetadata = original with { Dataset = original.Dataset with
            { Attribution = original.Dataset.Attribution with { Modifications = "No compact contract" } } };
        Assert.Null(ProgressReferences.Parse(wrongMetadata));
        foreach (var column in new[] { "offset", "bit", "eventId", "regionId" })
        {
            var attributes = original.Attributes.ToDictionary();
            attributes[column] = column == "regionId" ? "invalid/path" : "invalid";
            Assert.Null(ProgressReferences.Parse(original with { Attributes = attributes }));
        }
    }

    [Fact]
    public void CompleteSourceHashesRemainInTheSidecarAndOnlyDifferingRecordSourcesRemain()
    {
        var assembly = typeof(EldenRingPlugin).Assembly;
        long totalBytes = 0;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name =>
            name.Replace('\\', '/').StartsWith("refpacks/eldenring.community.en/", StringComparison.Ordinal)))
        {
            using var content = assembly.GetManifestResourceStream(resource)!;
            totalBytes += content.Length;
        }
        Assert.InRange(totalBytes, 1, 5_000_000);
        Assert.DoesNotContain(assembly.GetManifestResourceNames(), name => name.EndsWith("param-rows.json", StringComparison.Ordinal));
        var name = Assert.Single(assembly.GetManifestResourceNames(), name =>
            name.Replace('\\', '/') == "refpacks/eldenring.community.en/community.source.md");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var sidecar = reader.ReadToEnd();
        Assert.Contains("d3cd15912bf2c4ff7c67a1f3ec2aff78ec887e4d8e435e3cab036f80cb8f4c82", sidecar);
        Assert.Contains("38a09ba5bde95f4f67029c5af993514d26cfcd4c78ff11038647e99dccb1ce45", sidecar);
        var context = CommunityReferenceTests.Context();
        foreach (var id in new[] { CommunityReferences.ItemsCatalogId, CommunityReferences.FlagsCatalogId, CommunityReferences.ParamLayoutsCatalogId })
        {
            var catalog = context.Catalog(id)!;
            Assert.Contains(catalog.Dataset.Attribution.SourceUrl, sidecar);
            foreach (var row in CommunityReferenceTests.Read(catalog))
            {
                if (row.Source is { } source)
                {
                    Assert.NotEqual(catalog.Dataset.Attribution.SourceUrl, source);
                    Assert.Contains(source, sidecar);
                }
                Assert.DoesNotContain("lookupSource", row.Attributes.Keys);
                Assert.DoesNotContain("lookupSha256", row.Attributes.Keys);
                Assert.DoesNotContain("groupSources", row.Attributes.Keys);
            }
        }
    }
}
