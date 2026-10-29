using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class EmbeddedReferenceGuardTests
{
    [Theory]
    [InlineData(nameof(EmbeddedReferencePackLimits.MaxTotalBytes))]
    [InlineData(nameof(EmbeddedReferencePackLimits.MaxManifestBytes))]
    [InlineData(nameof(EmbeddedReferencePackLimits.MaxRecordsFileBytes))]
    [InlineData(nameof(EmbeddedReferencePackLimits.MaxAssetBytes))]
    [InlineData(nameof(EmbeddedReferencePackLimits.MaxImageDimension))]
    [InlineData(nameof(EmbeddedReferencePackLimits.MaxFiles))]
    [InlineData(nameof(EmbeddedReferencePackLimits.MaxRecordsPerCatalog))]
    public void NegativeLimitsFailBeforeDiscoveringAnyPacks(string name)
    {
        var limits = name switch
        {
            nameof(EmbeddedReferencePackLimits.MaxTotalBytes) => new EmbeddedReferencePackLimits { MaxTotalBytes = -1 },
            nameof(EmbeddedReferencePackLimits.MaxManifestBytes) => new() { MaxManifestBytes = -1 },
            nameof(EmbeddedReferencePackLimits.MaxRecordsFileBytes) => new() { MaxRecordsFileBytes = -1 },
            nameof(EmbeddedReferencePackLimits.MaxAssetBytes) => new() { MaxAssetBytes = -1 },
            nameof(EmbeddedReferencePackLimits.MaxImageDimension) => new() { MaxImageDimension = -1 },
            nameof(EmbeddedReferencePackLimits.MaxFiles) => new() { MaxFiles = -1 },
            nameof(EmbeddedReferencePackLimits.MaxRecordsPerCatalog) => new() { MaxRecordsPerCatalog = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        var assembly = new EmbeddedReferenceRejectionTests.ResourceAssembly([]);
        Assert.Equal(name, Assert.Throws<ArgumentOutOfRangeException>(() => EmbeddedReferenceContext.Load(assembly, limits)).ParamName);
    }

    [Fact]
    public void PinRejectsNullLocalesAndCopiesCallerOwnedPreferences()
    {
        var packs = EmbeddedReferenceContext.Load(EmbeddedReferenceRejectionTests.Resources(PackBuilder.WithItems()));
        Assert.Throws<ArgumentException>(() => packs.Pin("test.items", null, [null!]));
        var locales = new[] { "en" };
        var pinned = packs.Pin("test.items", null, locales);
        locales[0] = null!;
        Assert.NotNull(pinned.Catalog("items"));
    }

    [Fact]
    public void ZeroBudgetsPermitEmptyAssembliesButRejectPackContent()
    {
        var limits = new EmbeddedReferencePackLimits
        {
            MaxTotalBytes = 0, MaxManifestBytes = 0, MaxRecordsFileBytes = 0, MaxAssetBytes = 0,
            MaxImageDimension = 0, MaxFiles = 0, MaxRecordsPerCatalog = 0,
        };
        Assert.Empty(EmbeddedReferenceContext.Load(new EmbeddedReferenceRejectionTests.ResourceAssembly([]), limits).Packs);
        Assert.Throws<EmbeddedReferencePackException>(() => EmbeddedReferenceContext.Load(
            EmbeddedReferenceRejectionTests.Resources(PackBuilder.WithItems()), limits));
    }
}
