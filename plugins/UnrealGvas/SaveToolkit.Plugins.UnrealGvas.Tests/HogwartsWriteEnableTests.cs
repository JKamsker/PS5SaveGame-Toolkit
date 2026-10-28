using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsWriteEnableTests
{
    [Fact]
    public void ExistingSqliteCell_EditReopensAndWarns()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file => file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01603");
        using var document = plugin.Open(sample, references);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var collection = Assert.Single(query.Collections, item => item.Id == "hogwarts/RawDatabaseImage/InventoryDynamic");
        var page = query.Query(collection.Id, new QueryableCollectionQuery { Limit = 200 });
        var field = page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>()
            .First(item => item.Spec is IntegerSpec { ReadOnly: false });
        var next = FieldValue.Of(field.Value.AsInteger() + 1);

        var outcome = document.ApplyEdits([new ValueEdit(field.ValueId, next)]);

        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Errors.Select(error => error.Message)));
        Assert.Contains(outcome.Warnings, warning => warning.Contains("game may reject", StringComparison.OrdinalIgnoreCase));
        byte[] output = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(output.Length <= 16 * 1024 * 1024);
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(sample.RelativePath, output, "PPSA01603"), references);
        var reopenedQuery = Assert.IsAssignableFrom<IQueryableDocument>(reopened);
        _ = reopenedQuery.Query(collection.Id, new QueryableCollectionQuery { Limit = 200 });
        Assert.Equal(next, reopened.GetValue(field.ValueId));
    }

    [Fact]
    public void ExistingSqliteCell_UndoAndRedoRestoreTheDatabaseValue()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file => file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01603");
        using var session = new DocumentSession("hogwarts-write", sample, plugin, plugin.Open(sample, references), references: references);
        _ = session.BuildView();
        var collection = session.QueryableCollections.Single(item => item.Id == "hogwarts/RawDatabaseImage/InventoryDynamic");
        var page = session.QueryCollection(collection.Id, new QueryableCollectionQuery { Limit = 200 });
        var field = page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>().First(item => item.Spec is IntegerSpec { ReadOnly: false });
        long next = checked((long)field.Value.AsInteger() + 1);
        var json = JsonDocument.Parse(next.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();

        var applied = session.ApplyEdits([new IncomingEdit(field.ValueId, json)]);

        Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(error => error.Message)));
        Assert.True(session.Undo().Ok);
        Assert.True(session.Redo().Ok);
        Assert.True(session.State.IsDirty);
    }
}
