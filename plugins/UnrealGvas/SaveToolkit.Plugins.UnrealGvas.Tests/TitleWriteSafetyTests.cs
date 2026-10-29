using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class TitleWriteSafetyTests
{
    [Theory]
    [InlineData("generic", int.MinValue)]
    [InlineData("generic", 0)]
    [InlineData("generic", int.MaxValue)]
    [InlineData("jedi", int.MinValue)]
    [InlineData("jedi", 0)]
    [InlineData("jedi", int.MaxValue)]
    [InlineData("sackboy", int.MinValue)]
    [InlineData("sackboy", 0)]
    [InlineData("sackboy", int.MaxValue)]
    public void StorageBoundariesReportExactChangesAndPreserveEveryUneditedByte(string title, int target)
    {
        byte[] original = TitleFixture.Scalars(title);
        var plugin = new UnrealGvasPlugin();
        var file = MemorySaveSet.SingleFile("save.sav", original, TitleFixture.TitleId(title));
        using var document = plugin.Open(file);
        var fields = GvasFormatTests.Expand(document);
        var field = Count(fields, title);
        Assert.True(document.CanEdit);
        Assert.False(field.Spec.ReadOnly);
        Assert.Equal(title == "generic" ? Confidence.Verified : Confidence.Inferred, field.Confidence);
        Assert.Contains("Not validated on console", field.Warning);

        var outcome = document.ApplyEdits([new(field.ValueId, FieldValue.Of(target))]);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Errors);
        var change = Assert.Single(outcome.Changes);
        Assert.Equal((field.ValueId, field.Value, FieldValue.Of(target)), (change.ValueId, change.OldValue, change.NewValue));
        if (title != "generic") Assert.Contains(outcome.Warnings, warning => warning.Contains("Checksum coverage is unproven", StringComparison.Ordinal));
        byte[] expected = TitleFixture.Scalars(title, target);
        var output = Assert.Single(document.Serialize());
        Assert.Equal(file.RelativePath, output.RelativePath);
        Assert.Equal(expected, output.Content.ToArray());
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("save.sav", output.Content.ToArray(), TitleFixture.TitleId(title)));
        var actual = GvasFormatTests.Expand(reopened);
        Assert.Equal(fields.Select(item => item.ValueId), actual.Select(item => item.ValueId));
        foreach (var item in fields)
            Assert.Equal(item.ValueId == field.ValueId ? FieldValue.Of(target) : item.Value, reopened.GetValue(item.ValueId));
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(original, Bytes(document));
    }

    [Theory]
    [InlineData("generic", false)]
    [InlineData("generic", true)]
    [InlineData("jedi", false)]
    [InlineData("jedi", true)]
    [InlineData("sackboy", false)]
    [InlineData("sackboy", true)]
    public void InvalidBatchesPreservePristineAndAlreadyEditedState(string title, bool wrongKind)
    {
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save.sav", TitleFixture.Scalars(title), TitleFixture.TitleId(title)));
        var fields = GvasFormatTests.Expand(document);
        var count = Count(fields, title);
        var other = fields.Single(field => field.Label.StartsWith("OtherCount ·", StringComparison.Ordinal));
        foreach (bool priorEdit in new[] { false, true })
        {
            if (priorEdit) Assert.True(document.ApplyEdits([new(count.ValueId, FieldValue.Of(23))]).Succeeded);
            byte[] before = Bytes(document);
            var beforeValue = document.GetValue(count.ValueId);
            var invalid = wrongKind ? FieldValue.Of("bad") : FieldValue.Of(Int128.MaxValue);

            var rejected = document.ApplyEdits([new(count.ValueId, FieldValue.Of(27)), new(other.ValueId, invalid)]);

            Assert.False(rejected.Succeeded);
            Assert.NotEmpty(rejected.Errors);
            Assert.Empty(rejected.Changes);
            Assert.Equal(beforeValue, document.GetValue(count.ValueId));
            Assert.Equal(other.Value, document.GetValue(other.ValueId));
            Assert.Equal(before, Bytes(document));
        }
        Assert.True(document.ApplyEdits([new(count.ValueId, count.Value)]).Succeeded);
        Assert.Equal(TitleFixture.Scalars(title), Bytes(document));
    }

    [Theory]
    [InlineData("jedi", "Crc32")]
    [InlineData("jedi", "UncompressedActorRecordsSize")]
    [InlineData("sackboy", "Checksum")]
    public void IntegrityAndActorLengthRejectMixedBatchesWithoutDiscardingPriorEdits(string title, string protectedName)
    {
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save.sav", TitleFixture.Scalars(title), TitleFixture.TitleId(title)));
        var fields = GvasFormatTests.Expand(document);
        var count = Count(fields, title);
        var protectedField = fields.Single(field => field.Label.StartsWith(protectedName + " ·", StringComparison.Ordinal));
        Assert.True(protectedField.Spec.ReadOnly);
        Assert.True(document.ApplyEdits([new(count.ValueId, FieldValue.Of(23))]).Succeeded);
        byte[] before = Bytes(document);

        var rejected = document.ApplyEdits([new(count.ValueId, FieldValue.Of(27)), new(protectedField.ValueId, FieldValue.Of(1))]);

        Assert.False(rejected.Succeeded);
        Assert.NotEmpty(rejected.Errors);
        Assert.Empty(rejected.Changes);
        Assert.Equal(protectedField.Value, document.GetValue(protectedField.ValueId));
        Assert.Equal(FieldValue.Of(23), document.GetValue(count.ValueId));
        Assert.Equal(before, Bytes(document));
    }

    [Theory]
    [InlineData("jedi")]
    [InlineData("sackboy")]
    public void UnmatchedVersionsKeepTheWholeIntegrityBearingDocumentReadOnly(string title)
    {
        byte[] original = TitleFixture.Scalars(title);
        const int enginePatchOffset = 16;
        BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(enginePatchOffset), 9);
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save.sav", original, TitleFixture.TitleId(title)));
        Assert.False(document.CanEdit);
        var fields = GvasFormatTests.Expand(document);
        Assert.NotEmpty(fields);
        Assert.All(fields, field => Assert.True(field.Spec.ReadOnly));
        var count = Count(fields, title);
        Assert.False(document.ApplyEdits([new(count.ValueId, FieldValue.Of(23))]).Succeeded);
        Assert.Equal(original, Bytes(document));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("jedi")]
    [InlineData("sackboy")]
    public void SessionReopenValidationAndRepeatedUndoRedoPreserveCompleteBytes(string title)
    {
        byte[] original = TitleFixture.Scalars(title);
        byte[] expected = TitleFixture.Scalars(title, int.MaxValue);
        using var import = new TestSaveImport(original, $"{TitleFixture.TitleId(title)}/sdimg_Test/save.sav");
        var plugin = new UnrealGvasPlugin();
        var document = plugin.Open(import.File);
        using var session = new DocumentSession("title-write", import.File, plugin, document);
        var count = Count(GvasFormatTests.Expand(document), title);
        _ = session.BuildView();
        _ = session.Expand("search");

        var applied = session.ApplyEdits([new IncomingEdit(count.ValueId, JsonSerializer.SerializeToElement(int.MaxValue))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(error => error.Message)));
        Assert.True(session.State.IsDirty);
        Assert.Equal(expected, Bytes(document));
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.IsDirty);
            Assert.Equal(original, Bytes(document));
            Assert.True(session.Redo().Ok);
            Assert.True(session.State.IsDirty);
            Assert.Equal(expected, Bytes(document));
        }
    }

    private static FieldNode Count(IReadOnlyList<FieldNode> fields, string title) =>
        fields.Single(field => field.Label.StartsWith(TitleFixture.CountName(title) + " ·", StringComparison.Ordinal));

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
