using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroSchemaTests
{
    [Fact]
    public void FingerprintUsesStructureAndOccurrencesRatherThanValuesWhitespaceOrOpaqueSuffix()
    {
        using var first = AstroTestData.Open(AstroTestData.Bytes("\r\n<PreInstall><Empty/><N>001</N></PreInstall>old"));
        using var second = AstroTestData.Open(AstroTestData.Bytes("\r\n<PreInstall>\n<Empty/> <N>2</N></PreInstall>different"));
        Assert.Equal(AstroCatalogueTestData.SchemaHash(first), AstroCatalogueTestData.SchemaHash(second));
        foreach (var text in new[] { "<Empty/><N>1</N><N>1</N>", "<Other/><N>1</N>", "<Empty/><N x='1'>1</N>" })
        {
            using var changed = AstroTestData.Open(AstroTestData.Bytes($"\r\n<PreInstall>{text}</PreInstall>"));
            Assert.NotEqual(AstroCatalogueTestData.SchemaHash(first), AstroCatalogueTestData.SchemaHash(changed));
        }
    }

    [Fact]
    public void AdditionalUnknownKeyInvalidatesAllSchemaQualifiedAliasesWithoutLosingBytes()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var sample = AstroTestData.Samples()[0].ReadAllBytes();
        var rootEnd = sample.AsSpan().IndexOf("</PreInstall>"u8);
        var addition = "<UnknownSchema/>"u8;
        var bytes = new byte[sample.Length];
        sample.AsSpan(0, rootEnd).CopyTo(bytes);
        addition.CopyTo(bytes.AsSpan(rootEnd));
        sample.AsSpan(rootEnd, sample.Length - rootEnd - addition.Length).CopyTo(bytes.AsSpan(rootEnd + addition.Length));
        using var document = plugin.Open(AstroTestData.File(bytes), context);
        var row = AstroCatalogueTestData.Row(document, "Artifacts", "Artifact/ps1");
        Assert.Equal("Artifact/ps1", Assert.IsType<FieldNode>(row.Cells["name"]).Value.AsString());
        Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(row.Cells["name"]).Confidence);
        Assert.NotEqual(AstroCatalogueTestData.Fingerprint, AstroCatalogueTestData.SchemaHash(document));
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    [Fact]
    public void DuplicateSavedKeysRemainSeparateAndNeverResolveToASingleIdentity()
    {
        var bytes = AstroTestData.Bytes("\r\n<PreInstall><Artifact><ps1><IsGot>true</IsGot></ps1><ps1><IsGot>false</IsGot></ps1></Artifact></PreInstall>");
        using var noPack = AstroTestData.Open(bytes);
        var (_, _, context) = AstroCatalogueTestData.References();
        var wrapped = new OverrideContext(context, AstroCatalogueTestData.SchemaHash(noPack));
        using var document = new AstroPlayroomPlugin().Open(AstroTestData.File(bytes), wrapped);
        var rows = AstroCatalogueTestData.Table(document, "Artifacts").Rows;
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].Key, rows[1].Key);
        Assert.All(rows, row => Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(row.Cells["name"]).Confidence));
        Assert.Contains("0 mapped / 2 unmapped", AstroCatalogueTestData.Group(document, "Artifacts").Badge, StringComparison.Ordinal);
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    [Fact]
    public void UnknownBuildCannotUseBuildSpecificPackEvenWhenItsSchemaMatches()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples()[0], new OverrideContext(context,
            AstroCatalogueTestData.Fingerprint, buildSpecific: true));
        var name = Assert.IsType<FieldNode>(AstroCatalogueTestData.Row(document, "Artifacts", "Artifact/ps1").Cells["name"]);
        Assert.Equal("Artifact/ps1", name.Value.AsString());
        Assert.Equal(Confidence.Unknown, name.Confidence);
    }

    [Theory]
    [InlineData("<IsGot>true</IsGot><IsGot>false</IsGot>")]
    [InlineData("<IsGot>not-a-boolean</IsGot>")]
    [InlineData("<IsCinematicsPlayed>true</IsCinematicsPlayed>")]
    public void AmbiguousOrMissingOwnershipStaysUnknown(string flags)
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes($"\r\n<PreInstall><Artifact><unknown>{flags}</unknown></Artifact></PreInstall>"));
        Assert.Equal("0/1 owned; 0 mapped / 1 unmapped; 1 unknown ownership", AstroCatalogueTestData.Group(document, "Artifacts").Badge);
        var row = AstroCatalogueTestData.Row(document, "Artifacts", "Artifact/unknown");
        Assert.Equal("Unknown", Assert.IsType<TextNode>(row.Cells["status"]).Text);
    }

    private sealed class OverrideContext(IReferenceContext inner, string fingerprint, bool buildSpecific = false) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId) is { } catalog
            ? new OverrideCatalog(catalog, fingerprint, buildSpecific) : null;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }

    private sealed class OverrideCatalog(IReferenceCatalog inner, string fingerprint, bool buildSpecific) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack with { GameBuilds = buildSpecific ? ["unknown-new-patch"] : [] };
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferenceRecord? Find(string key) => inner.Find(key) is { } record ? record with
        { Attributes = new Dictionary<string, string>(record.Attributes) { ["schemaFingerprint"] = fingerprint } } : null;
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
    }
}
