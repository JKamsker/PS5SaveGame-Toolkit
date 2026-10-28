using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceSelectionTests
{
    private static readonly ItemsPlugin Plugin = new();

    private static ReferencePackStore Store(string[] locales, params PackBuilder[] packs)
    {
        var store = new ReferencePackStore([Plugin], new ReferencePackOptions { PreferredLocales = locales });
        foreach (var pack in packs)
        {
            store.Import(pack.Zip(), pack.Id + ".zip");
        }

        return store;
    }

    private static string? NameFor(ReferencePackStore store, string? build = null) =>
        store.Pin(Plugin, ItemsPlugin.TitleId).Catalog("items", build)?.Find("1001")?.Name;

    [Fact]
    public void Locale_PreferredExactTagWins_ThenLanguage_ThenNeutral()
    {
        PackBuilder[] packs =
        [
            PackBuilder.WithItems("Trank", "user.de", "de"),
            PackBuilder.WithItems("Potion", "user.en-us", "en-US"),
            PackBuilder.WithItems("Neutral", "user.neutral", null),
        ];

        Assert.Equal("Trank", NameFor(Store(["de-DE", "en"], packs)));
        Assert.Equal("Potion", NameFor(Store(["en-GB"], packs)));
        Assert.Equal("Neutral", NameFor(Store(["fr"], packs)));
    }

    [Fact]
    public void Build_ExactMatchWins_OtherBuildsAreSkipped_AgnosticIsFallback()
    {
        var store = Store(
            ["en"],
            PackBuilder.WithItems("Build 1", "user.b1", builds: ["1.01"]),
            PackBuilder.WithItems("Any build", "user.any"));

        Assert.Equal("Build 1", NameFor(store, "1.01"));
        Assert.Equal("Any build", NameFor(store, "1.02"));
        Assert.Equal("Any build", NameFor(store));
    }

    [Fact]
    public void Build_WithOnlyBuildSpecificPacks_UnknownBuildStillResolves_WrongBuildDoesNot()
    {
        var store = Store(["en"], PackBuilder.WithItems("Build 1", "user.b1", builds: ["1.01"]));
        LoadedReferencePack[] onlySpecific = [store.Find("user.b1")!];

        Assert.Equal("user.b1", ReferenceSelector.Select(onlySpecific, "items", null, ["en"])?.Id);
        Assert.Null(ReferenceSelector.Select(onlySpecific, "items", "2.00", ["en"]));
        Assert.Equal("Potion", NameFor(store), StringComparer.Ordinal);
        Assert.Equal("Potion", NameFor(store, "2.00"), StringComparer.Ordinal);
    }

    [Fact]
    public void UserPack_BeatsEmbeddedPack_WithSameLocale()
    {
        var store = Store(["en"]);
        Assert.Equal("Potion", NameFor(store));

        store.Import(PackBuilder.WithItems("Better Potion").Zip(), "user.zip");

        Assert.Equal("Better Potion", NameFor(store));
    }

    [Fact]
    public void HigherVersion_WinsTies()
    {
        var store = Store(["en"], PackBuilder.WithItems("Old", "user.a") with { Version = "1.2.0" }, PackBuilder.WithItems("New", "user.b") with { Version = "1.10.0" });

        Assert.Equal("New", NameFor(store));
    }

    [Fact]
    public void Packs_AreScopedToPluginAndTitle()
    {
        var store = Store(
            ["en"],
            PackBuilder.WithItems("Other plugin", "user.other") with { Plugins = ["test.counter"] },
            PackBuilder.WithItems("Other title", "user.title") with { TitleIds = ["PPSA00001"] });

        var context = store.Pin(Plugin, ItemsPlugin.TitleId);

        Assert.DoesNotContain(context.Packs, p => p.Id is "user.other" or "user.title");
        Assert.Contains(context.Packs, p => p.Id == "test.items.en");
    }

    [Fact]
    public void NoMatchingPack_YieldsEmptyContext()
    {
        var store = new ReferencePackStore([new CounterPlugin()]);

        var context = store.Pin(new CounterPlugin(), "PPSA99999");

        Assert.Empty(context.Packs);
        Assert.Null(context.Catalog("items"));
        Assert.Null(context.GetAsset("test.items.en:icons/potion.png"));
        Assert.Null(IReferenceContext.Empty.Catalog("items"));
    }

    [Fact]
    public void ImportingEmbeddedPackId_IsRejected()
    {
        var store = Store(["en"]);

        var error = Assert.Throws<ReferencePackException>(() => store.Import(PackBuilder.WithItems(id: "test.items.en").Zip(), "x.zip"));

        Assert.Contains("built-in pack", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_FiltersByTextAndCategory_AndPages()
    {
        var catalog = Store(["en"]).Pin(Plugin, ItemsPlugin.TitleId).Catalog("items")!;

        Assert.Equal(["1001", "1002"], catalog.Search(new ReferenceQuery { Category = "CONSUMABLE" }).Items.Select(r => r.Key));
        Assert.Equal(["2001"], catalog.Search(new ReferenceQuery { Text = "sword" }).Items.Select(r => r.Key));
        Assert.Equal(["1002"], catalog.Search(new ReferenceQuery { Text = "1002" }).Items.Select(r => r.Key));

        var page = catalog.Search(new ReferenceQuery { Offset = 1, Limit = 1 });
        Assert.Equal(3, page.Total);
        Assert.Equal(1, page.Offset);
        Assert.Equal("1002", Assert.Single(page.Items).Key);
        Assert.Equal(3, catalog.Search(new ReferenceQuery { Limit = 100_000 }).Items.Count);
    }

    [Fact]
    public void Assets_ResolveByQualifiedIdOnly_WithProvenance()
    {
        var context = Store(["en"]).Pin(Plugin, ItemsPlugin.TitleId);

        var asset = context.GetAsset("test.items.en:icons/potion.png");

        Assert.Equal("image/png", asset!.ContentType);
        Assert.Equal("icons", context.AssetProvenance("test.items.en:icons/potion.png")!.Id);
        Assert.Null(context.GetAsset("icons/potion.png"));
        Assert.Null(context.GetAsset("test.items.en:../pack.json"));
        Assert.Null(context.GetAsset("test.items.en:pack.json"));
    }
}
