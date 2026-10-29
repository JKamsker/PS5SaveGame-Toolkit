using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class PerkSearchTests
{
    private const string PainkillerId = "Body_Central_Milestone_1";
    private const string OtherPerkId = "Body_Central_Perk_1_2";
    private const string UnknownPerkId = "Unmapped_Modern_Perk";
    private const string FirstRowKey = "development/modern/0/0";
    private const string SecondRowKey = "development/modern/0/1";
    private const string SharedLabel = "Shared synthetic perk";

    [Theory]
    [InlineData("Painkiller")]
    [InlineData("ainkill")]
    [InlineData("pAiNkIlLeR")]
    [InlineData(PainkillerId)]
    [InlineData("bOdY_cEnTrAl_mIlEsToNe_1")]
    public void DisplayedNameAndRawIdentifierFindTheSameAttributedStoredPerk(string text)
    {
        var (_, _, context) = ReferencePackFixture.Embedded();
        var inspection = Development(context, PainkillerId, OtherPerkId, UnknownPerkId);
        var page = inspection.Table.Query(new() { Text = text, Limit = 1 });
        Assert.Equal(4, page.TotalCount); // Three stored perks and their group summary.
        Assert.Equal(1, page.FilteredCount);
        var row = Assert.Single(page.Rows);
        Assert.Equal(FirstRowKey, row.Key);
        var name = Assert.IsType<SectionNode>(row.Cells["name"]);
        Assert.Equal("Painkiller", name.Title);
        Assert.Equal(PainkillerId, Assert.IsType<TextNode>(name.Children[0]).Text);
        Assert.Contains("CD PROJEKT RED", name.Description);
        Assert.Contains("Unlocks slow Health Regen", Cell(row, "description"));
        Assert.Equal("1", Cell(row, "rank"));
        Assert.Contains("unverified", Cell(row, "status"));
        Assert.Equal(Confidence.Inferred.ToString(), Assert.IsType<SectionNode>(row.Cells["rank"]).Badge);

        var nodes = inspection.Expand("perk-graph/0", (id, label, spec, value) => new FieldNode(id, label, spec, FieldValue.Of(value)));
        var graph = Assert.IsType<GraphNode>(nodes[0]);
        Assert.Equal(4, graph.Items.Count);
        Assert.All(graph.Items, item => Assert.Equal(GraphState.Unknown, item.State));
        Assert.Empty(graph.Edges);
        var details = Assert.IsType<SectionNode>(nodes[1]);
        Assert.Equal(4, details.Children.Count);
        Assert.All(details.Children.OfType<FieldNode>(), field => Assert.True(field.Spec.ReadOnly));
    }

    [Fact]
    public void MissingPackAndUnknownIdentifiersKeepRawSearchAndFallback()
    {
        var missing = Development(IReferenceContext.Empty, PainkillerId, UnknownPerkId);
        Assert.Empty(missing.Table.Query(new() { Text = "Painkiller" }).Rows);
        var raw = Assert.Single(missing.Table.Query(new() { Text = PainkillerId }).Rows);
        Assert.Equal(FirstRowKey, raw.Key);
        Assert.Equal(PainkillerId, Cell(raw, "name"));
        Assert.Equal(ScriptableReader.Source, Assert.IsType<SectionNode>(raw.Cells["name"]).Description);
        Assert.Equal("", Cell(raw, "description"));

        var (_, _, context) = ReferencePackFixture.Embedded();
        var enriched = Development(context, PainkillerId, UnknownPerkId);
        var unknown = Assert.Single(enriched.Table.Query(new() { Text = "unmapped_modern_perk" }).Rows);
        Assert.Equal(SecondRowKey, unknown.Key);
        Assert.Equal(UnknownPerkId, Cell(unknown, "name"));
        Assert.Equal(ScriptableReader.Source, Assert.IsType<SectionNode>(unknown.Cells["name"]).Description);
        Assert.Empty(enriched.Table.Query(new() { Text = "Definitely absent label" }).Rows);
    }

    [Fact]
    public void DuplicateLabelsKeepDistinctKeysAndRawFiltersSortsAndPaging()
    {
        var (plugin, store, _) = ReferencePackFixture.Embedded();
        store.Import(ReferencePackFixture.UserPack("perks", [
            new { id = PainkillerId, name = SharedLabel }, new { id = OtherPerkId, name = SharedLabel },
        ]), "synthetic-perks.zip");
        var table = Development(store.Pin(plugin, "PPSA04027"), PainkillerId, OtherPerkId, UnknownPerkId).Table;
        var query = new QueryableCollectionQuery { Text = SharedLabel, SortColumn = "rank", Descending = true, Limit = 1 };
        var first = table.Query(query);
        var second = table.Query(query with { Offset = 1 });
        Assert.Equal(4, first.TotalCount);
        Assert.Equal(2, first.FilteredCount);
        Assert.Equal(2, second.FilteredCount);
        Assert.Equal(SecondRowKey, Assert.Single(first.Rows).Key);
        Assert.Equal(FirstRowKey, Assert.Single(second.Rows).Key);
        Assert.Equal(SharedLabel, Cell(first.Rows[0], "name"));
        Assert.Contains("Test fixture", Assert.IsType<SectionNode>(first.Rows[0].Cells["name"]).Description);
        Assert.Empty(table.Query(query with { Offset = 2 }).Rows);

        var filtered = table.Query(query with { Filters = new Dictionary<string, string> { ["name"] = PainkillerId } });
        Assert.Equal(FirstRowKey, Assert.Single(filtered.Rows).Key);
        Assert.Empty(table.Query(query with { Filters = new Dictionary<string, string> { ["name"] = SharedLabel } }).Rows);
        Assert.Equal(SecondRowKey, Assert.Single(table.Query(new() { Text = OtherPerkId }).Rows).Key);
        var byIdentifier = table.Query(query with { SortColumn = "name", Descending = false, Limit = 2 });
        Assert.Equal(new[] { FirstRowKey, SecondRowKey }, byIdentifier.Rows.Select(row => row.Key));
        // Equal displayed labels do not turn the raw-name sort into a label/key tie-break.
        var reverse = table.Query(query with { SortColumn = "name", Limit = 2 });
        Assert.Equal(new[] { SecondRowKey, FirstRowKey }, reverse.Rows.Select(row => row.Key));
    }

    [Fact]
    public void SearchUsesTheSameTitleBuildAndSnapshotPinnedPackAsRendering()
    {
        const string localLabel = "Synthetic local perk caption";
        var (plugin, store, original) = ReferencePackFixture.Embedded();
        store.Import(ReferencePackFixture.UserPack("perks", [new { id = PainkillerId, name = localLabel }]), "synthetic-local.zip");
        var pinned = store.Pin(plugin, "PPSA04027");
        var local = Development(pinned, PainkillerId);
        Assert.Equal(localLabel, Cell(Assert.Single(local.Table.Query(new() { Text = localLabel }).Rows), "name"));
        Assert.Empty(local.Table.Query(new() { Text = "Painkiller" }).Rows);
        Assert.Equal(FirstRowKey, Assert.Single(local.Table.Query(new() { Text = PainkillerId }).Rows).Key);

        var old = Development(original, PainkillerId);
        Assert.Equal("Painkiller", Cell(Assert.Single(old.Table.Query(new() { Text = "Painkiller" }).Rows), "name"));
        Assert.Empty(old.Table.Query(new() { Text = localLabel }).Rows);
        var otherBuild = Development(pinned, 2220, PainkillerId);
        Assert.Equal("Painkiller", Cell(Assert.Single(otherBuild.Table.Query(new() { Text = "Painkiller" }).Rows), "name"));
        Assert.Empty(otherBuild.Table.Query(new() { Text = localLabel }).Rows);
        var otherTitle = Development(store.Pin(plugin, "PPSA00000"), PainkillerId);
        Assert.Empty(otherTitle.Table.Query(new() { Text = localLabel }).Rows);
        Assert.Empty(otherTitle.Table.Query(new() { Text = "Painkiller" }).Rows);
        Assert.Equal(PainkillerId, Cell(Assert.Single(otherTitle.Table.Query(new() { Text = PainkillerId }).Rows), "name"));
    }

    [Fact]
    public void DisplayNameSearchRetainsCollectionQueryBoundsAndDeclaredColumns()
    {
        var (_, _, context) = ReferencePackFixture.Embedded();
        var table = Development(context, PainkillerId).Table;
        Assert.Throws<ArgumentException>(() => table.Query(new() { Text = new string('x', QueryableCollectionLimits.MaxQueryLength + 1) }));
        Assert.Throws<ArgumentException>(() => table.Query(new() { Text = "Painkiller", Cursor = "unsupported" }));
        Assert.Throws<ArgumentException>(() => table.Query(new() { Text = "Painkiller", SortColumn = "displayName" }));
        Assert.Throws<ArgumentException>(() => table.Query(new() { Filters = new Dictionary<string, string> { ["displayName"] = "Painkiller" } }));
        Assert.Empty(table.Query(new() { Text = "Painkiller", Offset = int.MaxValue }).Rows);
        Assert.Equal(QueryableCollectionLimits.MaxPageSize, table.Query(new() { Text = "Painkiller", Limit = int.MaxValue }).Limit);
    }

    private static DevelopmentInspection Development(IReferenceContext context, params string[] perks) => Development(context, 2210, perks);

    private static DevelopmentInspection Development(IReferenceContext context, int build, params string[] perks)
    {
        var fixture = new InspectionPackageFixture();
        var stored = perks.Select((id, index) => fixture.Class(("type", "gamedataNewPerkType", fixture.Enum(id)),
            ("currLevel", "Int32", BitConverter.GetBytes(index + 1)))).ToArray();
        var group = fixture.Class(("type", "gamedataAttributeDataType", fixture.Enum("BodyAttributeData")),
            ("unlockedPerks", "array:SNewPerk", InspectionPackageFixture.Array(stored)));
        var player = fixture.Class(("ownerID", "entEntityID", fixture.Class(("hash", "Uint64", BitConverter.GetBytes(1UL)))),
            ("proficiencies", "array:SProficiency", InspectionPackageFixture.Array()),
            ("devPoints", "array:SDevelopmentPoints", InspectionPackageFixture.Array()),
            ("attributesData", "array:SAttributeData", InspectionPackageFixture.Array(group)));
        var system = fixture.Class(("playerData", "array:handle:PlayerDevelopmentData", InspectionPackageFixture.Array(BitConverter.GetBytes(1))));
        var reader = fixture.Reader(("PlayerDevelopmentSystem", system), ("PlayerDevelopmentData", player));
        var inspection = new DevelopmentInspection(reader, null, references: new CyberpunkReferences(context, build));
        Assert.Null(inspection.Warning); // A malformed fixture must not silently reduce coverage to an empty table.
        return inspection;
    }

    private static string Cell(TableRow row, string column) => Assert.IsType<SectionNode>(row.Cells[column]).Title;
}
