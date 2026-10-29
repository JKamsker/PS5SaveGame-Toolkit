using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsSqliteRollbackTests
{
    private const string Primary = "hogwarts/RawDatabaseImage/InventoryDynamic";
    private const string Exclusive = "hogwarts/RawExclusiveImage/InventoryDynamic";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SqliteSetterFailureRollsBackEarlierCellsAndPreservesPriorImages(bool acrossImages, bool priorEdits)
    {
        using var source = SqliteImage.Create();
        source.Execute("CREATE TABLE InventoryDynamic(Count INTEGER CHECK(Count BETWEEN 0 AND 10)); INSERT INTO InventoryDynamic VALUES(1),(2);");
        byte[] frames = HogwartsImage.EncodeStored(source.Serialize());
        byte[] original = TitleFixture.Hogwarts(frames, frames);
        var plugin = new UnrealGvasPlugin();
        using var document = plugin.Open(MemorySaveSet.SingleFile("save.sav", original, "PPSA01603"));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var primary = Counts(queryable, Primary);
        var exclusive = Counts(queryable, Exclusive);
        if (priorEdits)
            Assert.True(document.ApplyEdits([new(primary[0].ValueId, FieldValue.Of(3)), new(exclusive[0].ValueId, FieldValue.Of(4))]).Succeeded);
        byte[] before = Bytes(document);
        var baseline = primary.Concat(exclusive).ToDictionary(field => field.ValueId, field => document.GetValue(field.ValueId));
        var failing = acrossImages ? exclusive[1] : primary[1];

        // Both values satisfy their published Int64Spec; the second setter fails inside SQLite.
        var rejected = document.ApplyEdits([new(primary[0].ValueId, FieldValue.Of(5)), new(failing.ValueId, FieldValue.Of(20))]);

        Assert.False(rejected.Succeeded);
        Assert.Empty(rejected.Changes);
        Assert.Contains(rejected.Errors, error => error.Message.Contains("CHECK constraint failed", StringComparison.Ordinal));
        foreach (var (id, value) in baseline) Assert.Equal(value, document.GetValue(id));
        Assert.Equal(before, Bytes(document));
        if (!priorEdits) Assert.Equal(original, Bytes(document));

        // Re-querying must observe the restored SQL cells, not just cached field bindings.
        foreach (var field in Counts(queryable, Primary).Concat(Counts(queryable, Exclusive)))
            Assert.Equal(baseline[field.ValueId], field.Value);
        Assert.True(document.ApplyEdits([new(primary[0].ValueId, FieldValue.Of(5)), new(failing.ValueId, FieldValue.Of(6))]).Succeeded);
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("save.sav", Bytes(document), "PPSA01603"));
        var reopenedQuery = Assert.IsAssignableFrom<IQueryableDocument>(reopened);
        foreach (var field in Counts(reopenedQuery, Primary).Concat(Counts(reopenedQuery, Exclusive)))
            Assert.Equal(field.ValueId == primary[0].ValueId ? FieldValue.Of(5)
                : field.ValueId == failing.ValueId ? FieldValue.Of(6) : baseline[field.ValueId], field.Value);
    }

    private static FieldNode[] Counts(IQueryableDocument document, string collection) => document.Query(collection, new()).Rows
        .Select(row => Assert.IsType<FieldNode>(row.Cells["Count"])).ToArray();

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
