using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

/// <summary>
/// The metadata cache revalidates only touched entries and the cached aliases of touched targets
/// (a reverse index), instead of scanning every resolved field per resolution.
/// </summary>
public sealed class FieldMetadataCacheTests
{
    [Fact]
    public void RefreshingACanonicalTargetRevalidatesItsPreviouslyCachedAliasAndKeepsTheCacheOnFailure()
    {
        using var document = new AliasDocument();
        var index = new FieldMetadataIndex(document);
        index.ResolveAll(["alias"]);
        Assert.Equal("primary", index.Cached("alias")!.CanonicalValueId);
        var primary = index.Cached("primary");
        Assert.NotNull(primary);

        document.PrimaryMaximum = 20; // The cached alias (maximum 10) no longer agrees with its target.
        var error = Assert.Throws<InvalidOperationException>(() => index.ResolveAll(["primary"]));
        Assert.Equal("Invalid canonical alias 'alias'.", error.Message);
        Assert.Same(primary, index.Cached("primary"));
    }

    [Fact]
    public void AnAliasThatMovesToAnotherTargetIsNoLongerRevalidatedForItsOldTarget()
    {
        using var document = new AliasDocument();
        var index = new FieldMetadataIndex(document);
        index.ResolveAll(["alias"]);
        document.AliasTarget = "other";
        index.ResolveAll(["alias"]);
        Assert.Equal("other", index.Cached("alias")!.CanonicalValueId);

        document.PrimaryMaximum = 20;
        index.ResolveAll(["primary"]);
        Assert.Equal(new IntegerSpec(0, 20), index.Cached("primary")!.Spec);
    }

    [Fact]
    public void ClearedEntriesLeaveNoStaleAlias()
    {
        using var document = new AliasDocument();
        var index = new FieldMetadataIndex(document);
        index.ResolveAll(["alias"]);
        index.Clear();
        Assert.Null(index.Cached("alias"));
        document.PrimaryMaximum = 20;
        index.ResolveAll(["primary"]);
        Assert.Equal(new IntegerSpec(0, 20), index.Cached("primary")!.Spec);
    }

    private sealed class AliasDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public int PrimaryMaximum { get; set; } = 10;
        public string AliasTarget { get; set; } = "primary";
        public override string Title => "Aliases";
        public override ViewNode BuildView() => new TextNode("Metadata only");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public AliasDocument()
        {
            RegisterField("primary", new IntegerSpec(0, 100), () => FieldValue.Of(1), _ => { });
            RegisterField("other", new IntegerSpec(0, 10), () => FieldValue.Of(1), _ => { });
            RegisterField("alias", new IntegerSpec(0, 10) with { ReadOnly = true }, () => FieldValue.Of(1));
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
            valueIds.Select(id => id switch
            {
                "alias" => new FieldMetadata(id, "Alias", new IntegerSpec(0, 10) with { ReadOnly = true })
                    { BindingKind = FieldBindingKind.Derived, CanonicalValueId = AliasTarget },
                "primary" => new FieldMetadata(id, "Primary", new IntegerSpec(0, PrimaryMaximum)),
                _ => new FieldMetadata(id, "Other", new IntegerSpec(0, 10)),
            }).ToArray();
    }
}
