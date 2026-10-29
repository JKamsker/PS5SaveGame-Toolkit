using System.Text.Json;
using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsWriteSafetyTests
{
    private const string Inventory = "hogwarts/RawDatabaseImage/InventoryDynamic";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameValueEditPreservesOriginalCompressedFrames(bool synthetic)
    {
        var sample = Input(synthetic);
        using var document = new UnrealGvasPlugin().Open(sample);
        var field = Counts(document)[0];

        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);

        Assert.Equal(sample.ReadAllBytes(), Output(document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoringAllCellsPreservesOriginalBytesAfterRequeryAndRepeatedEdits(bool synthetic)
    {
        var sample = Input(synthetic);
        using var document = new UnrealGvasPlugin().Open(sample);
        var field = Counts(document)[0];
        var next = FieldValue.Of(field.Value.AsInteger() + 1);
        Assert.True(document.ApplyEdits([new(field.ValueId, next)]).Succeeded);
        byte[] edited = Output(document);
        Assert.NotEqual(sample.ReadAllBytes(), edited);
        _ = Counts(document); // A new binding must retain the imported baseline.

        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(sample.ReadAllBytes(), Output(document));
        Assert.True(document.ApplyEdits([new(field.ValueId, next)]).Succeeded);
        Assert.Equal(edited, Output(document));
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(sample.ReadAllBytes(), Output(document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedBatchPreservesOriginalBytesAndAllowsSubsequentEdit(bool synthetic)
    {
        var sample = Input(synthetic);
        using var document = new UnrealGvasPlugin().Open(sample);
        var fields = Counts(document);
        var outcome = document.ApplyEdits([
            new(fields[0].ValueId, FieldValue.Of(fields[0].Value.AsInteger() + 1)),
            new(fields[1].ValueId, FieldValue.Of(Int128.MaxValue)),
        ]);

        Assert.False(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Equal(fields[0].Value, document.GetValue(fields[0].ValueId));
        Assert.Equal(fields[1].Value, document.GetValue(fields[1].ValueId));
        Assert.Equal(sample.ReadAllBytes(), Output(document));
        Assert.True(document.ApplyEdits([new(fields[0].ValueId, FieldValue.Of(fields[0].Value.AsInteger() + 1))]).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingOneImagePreservesTheOtherImagesOriginalFrames(bool synthetic)
    {
        var sample = Input(synthetic);
        using var document = new UnrealGvasPlugin().Open(sample);
        var field = Counts(document)[0];
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(field.Value.AsInteger() + 1))]).Succeeded);

        var original = GvasFile.Parse(sample.ReadAllBytes());
        var edited = GvasFile.Parse(Output(document));
        var before = original.Properties.Single(node => node.Name == "RawExclusiveImage");
        var after = edited.Properties.Single(node => node.Name == before.Name);
        Assert.Equal(original.Original.Slice(before.Offset, before.Length).ToArray(),
            edited.Original.Slice(after.Offset, after.Length).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedBatchPreservesAnAlreadyEditedImageExactly(bool synthetic)
    {
        using var document = new UnrealGvasPlugin().Open(Input(synthetic));
        var fields = Counts(document);
        var next = FieldValue.Of(fields[0].Value.AsInteger() + 1);
        Assert.True(document.ApplyEdits([new(fields[0].ValueId, next)]).Succeeded);
        byte[] before = Output(document);

        var outcome = document.ApplyEdits([
            new(fields[0].ValueId, FieldValue.Of(next.AsInteger() + 1)),
            new(fields[1].ValueId, FieldValue.Of(Int128.MaxValue)),
        ]);

        Assert.False(outcome.Succeeded);
        Assert.Equal(next, document.GetValue(fields[0].ValueId));
        Assert.Equal(before, Output(document));
    }

    [Fact]
    public void OutputBudgetRejectionRollsBackSuccessfulSqliteChanges()
    {
        const int oversizedSuffixBytes = 16 * 1024 * 1024;
        byte[] bytes = [.. HogwartsInventoryFixture.Create().ReadAllBytes(), .. new byte[oversizedSuffixBytes]];
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("large.sav", bytes, "PPSA01603"));
        var field = Counts(document)[0];

        var outcome = document.ApplyEdits([new(field.ValueId, FieldValue.Of(field.Value.AsInteger() + 1))]);

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Errors, error => error.Message.Contains("slot budget", StringComparison.Ordinal));
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.Equal(bytes, Output(document));
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, Output(document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionUndoRedoRestoresExactBytesAcrossBothImages(bool synthetic)
    {
        using var import = synthetic ? HogwartsInventoryFixture.CreateDisk() : null;
        var sample = import?.File ?? Sample();
        var plugin = new UnrealGvasPlugin();
        var document = plugin.Open(sample);
        using var session = new DocumentSession("hogwarts-write-safety", sample, plugin, document);
        _ = session.BuildView();
        byte[] original = Output(document);
        var first = session.QueryCollection(Inventory, new() { Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows
            .SelectMany(row => row.Cells.Values).OfType<FieldNode>().Single(field => field.Spec is IntegerSpec);
        Assert.True(session.ApplyEdits([Edit(first)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        byte[] firstEdit = Output(document);
        var other = session.QueryCollection("hogwarts/RawExclusiveImage/InventoryDynamic", new() { Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows
            .SelectMany(row => row.Cells.Values).OfType<FieldNode>().Single(field => field.Spec is IntegerSpec);
        Assert.True(session.ApplyEdits([Edit(other)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        byte[] bothEdits = Output(document);

        for (int repeat = 0; repeat < 2; repeat++)
        {
            Assert.True(session.Undo().Ok);
            Assert.Equal(firstEdit, Output(document));
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.IsDirty);
            Assert.Equal(original, Output(document));
            Assert.True(session.Redo().Ok);
            Assert.Equal(firstEdit, Output(document));
            Assert.True(session.Redo().Ok);
            Assert.Equal(bothEdits, Output(document));
        }
    }

    [Fact]
    public void TextRevertRestoresSqliteAllocationAndAnotherOutstandingEdit()
    {
        using var original = SqliteImage.Create();
        original.Execute("CREATE TABLE MiscDataDynamic(DataOwner TEXT, DataName TEXT, DataValue TEXT);" +
            "INSERT INTO MiscDataDynamic VALUES('Player','A','before'),('Player','B','before');");
        using var database = new HogwartsDatabase("RawDatabaseImage", original.Serialize());
        var table = Assert.Single(database.Tables);
        database.SetCell(table, 1, "DataValue", "retained");
        byte[] first = database.SerializeForWrite();
        database.SetCell(table, 2, "DataValue", new string('x', 100_000));
        Assert.True(database.SerializeForWrite().Length > first.Length);

        database.SetCell(table, 2, "DataValue", "before");

        Assert.True(database.HasChanges);
        Assert.Equal(first, database.SerializeForWrite());
        database.SetCell(table, 1, "DataValue", "before");
        Assert.False(database.HasChanges);
        Assert.Equal(original.Serialize(), database.SerializeForWrite());
    }

    private static ISaveFile Input(bool synthetic) => synthetic ? HogwartsInventoryFixture.Create() : Sample();

    private static DiskSaveFile Sample() => SampleSaves.Load("PPSA01603").AllDiskFiles.First(file =>
        file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");

    private static FieldNode[] Counts(ISaveDocument document) => Assert.IsAssignableFrom<IQueryableDocument>(document)
        .Query(Inventory, new() { Limit = 2 }).Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>()
        .Where(field => field.Spec is IntegerSpec { ReadOnly: false }).ToArray();

    private static byte[] Output(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();

    private static IncomingEdit Edit(FieldNode field) => new(field.ValueId,
        JsonSerializer.SerializeToElement(checked((long)field.Value.AsInteger() + 1)));
}
