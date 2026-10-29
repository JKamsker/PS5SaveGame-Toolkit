using System.Collections;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class InventoryPerformanceTests
{
    private const int RowCount = 1000;
    private const string SeedEntry = "{\"ItemData\":{\"GeneratorData\":{\"GenesisRandomSeed\":100}}}";

    [Fact]
    public void DescribingEveryIdentityEnumeratesTheInventoryOnlyOnce()
    {
        var root = JsonTokenTree.Parse(Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat(SeedEntry, RowCount)) + "]")).Nodes[0];
        var counted = new CountingRows(root.Children);
        var identities = new InventoryIdentity(counted);
        for (var index = 0; index < RowCount; index++)
            Assert.Contains($"seed=100, occurrence={index};", identities.Describe(root.Children[index]));
        Assert.Equal(RowCount, counted.Reads);
        for (var index = RowCount - 1; index >= 0; index--)
            Assert.Contains($"occurrence={index};", identities.Describe(root.Children[index]));
        Assert.Equal(RowCount, counted.Reads);
    }

    [Fact]
    public void WarmLateInventoryPagesReuseIdentityCountsAndInvalidateOnEditsAndRestores()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472}," +
            "\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":false},\"Inventory\":{\"Entries\":[" +
            string.Join(',', Enumerable.Repeat(SeedEntry, RowCount)) + "]}}}");
        using var document = new XorJsonPlugin().Open(MemorySaveSet.SingleFile("hero.sav", bytes));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var query = new QueryableCollectionQuery { Offset = 980, Limit = 20 };
        _ = queryable.Query("character/inventory", query);
        var allocated = AllocationProbe.Measure(() => queryable.Query("character/inventory", query), out var page);
        allocated.AssertInRange(1, 128 * 1024);
        Assert.Contains("occurrence=999;", Assert.IsType<SectionNode>(page.Rows[^1].Cells["details"]).Description!);

        var first = queryable.Query("character/inventory", new() { Limit = 1 }).Rows[0];
        var firstRowId = first.Key["value/".Length..];
        var removal = document.InvokeAction("json/remove/" + firstRowId, new Dictionary<string, FieldValue>());
        var last = queryable.Query("character/inventory", new() { Descending = true, Limit = 1 }).Rows[0];
        Assert.Contains("occurrence=998;", Assert.IsType<SectionNode>(last.Cells["details"]).Description!);
        _ = document.RestoreMemento(removal.Revert!);
        last = queryable.Query("character/inventory", new() { Descending = true, Limit = 1 }).Rows[0];
        Assert.Contains("occurrence=999;", Assert.IsType<SectionNode>(last.Cells["details"]).Description!);

        var seed = XorJsonTests.Fields(document).First(field => field.Label == "GenesisRandomSeed");
        Assert.True(document.ApplyEdits([new(seed.ValueId, FieldValue.ExactNumber("100.0"))]).Succeeded);
        last = queryable.Query("character/inventory", new() { Descending = true, Limit = 1 }).Rows[0];
        Assert.Contains("occurrence=998;", Assert.IsType<SectionNode>(last.Cells["details"]).Description!);
        Assert.True(document.ApplyEdits([new(seed.ValueId, seed.Value)]).Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void ExactSeedSpellingsAndAbsentOrAmbiguousSeedsKeepTheirExistingIdentityMeaning()
    {
        var text = "[" + SeedEntry + "," + SeedEntry.Replace(":100", ":100.0", StringComparison.Ordinal) + "," + SeedEntry +
            ",{}, {\"ItemData\":{\"GeneratorData\":{\"GenesisRandomSeed\":100,\"GenesisRandomSeed\":100}}}]";
        var rows = JsonTokenTree.Parse(Encoding.UTF8.GetBytes(text)).Nodes[0].Children;
        var identities = new InventoryIdentity(rows);
        Assert.Contains("seed=100, occurrence=0;", identities.Describe(rows[0]));
        Assert.Contains("seed=100.0, occurrence=0;", identities.Describe(rows[1]));
        Assert.Contains("seed=100, occurrence=1;", identities.Describe(rows[2]));
        Assert.Contains("generator seed absent", identities.Describe(rows[3]));
        Assert.Contains("generator seed absent", identities.Describe(rows[4]));
    }

    private sealed class CountingRows(IReadOnlyList<JsonValueToken> rows) : IReadOnlyList<JsonValueToken>
    {
        public int Reads { get; private set; }
        public int Count => rows.Count;
        public JsonValueToken this[int index] { get { Reads++; return rows[index]; } }
        public IEnumerator<JsonValueToken> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) yield return this[i];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
