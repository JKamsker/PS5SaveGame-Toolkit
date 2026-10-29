using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class BooleanUndoTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(255)]
    public void HeaderAndRawAliasesRestoreImportedBooleanByteAcrossUndoRedo(int storedByte)
    {
        // Accepted-input coverage, not evidence that the game produces noncanonical booleans.
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin");
        var container = new DsssContainer(file.ReadAllBytes());
        var plaintext = container.Plaintext.ToArray();
        var target = new SaveLayout(plaintext).Header!.Find(KnownHashes.Cleared)!;
        plaintext[target.Offset] = (byte)storedByte;
        var original = container.Rebuild(plaintext);
        using var document = new PragmataDocument(file.RelativePath, original);
        using var session = new DocumentSession("boolean-undo", file, new PragmataPlugin(), document,
            openOriginal: () => new PragmataDocument(file.RelativePath, original));
        var tabs = ((TabsNode)session.BuildView()).Tabs;
        var header = tabs.Single(tab => tab.Title == "Raw").Children.OfType<SectionNode>()
            .Single(section => section.LazyId!.StartsWith($"tree/{document.Layout.Header!.Path}/", StringComparison.Ordinal));
        var raw = session.Expand(header.LazyId!).OfType<FieldNode>().Single(field => field.ValueId == $"raw/{target.Path}");
        Assert.Equal(storedByte != 0, raw.Value.AsBool());
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());

        // Assigning the displayed value must be a byte-identical no-op.
        Assert.True(session.ApplyEdits([new(raw.ValueId, JsonSerializer.SerializeToElement(storedByte != 0))]).Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());

        var outcome = session.ApplyEdits([new(raw.ValueId, JsonSerializer.SerializeToElement(storedByte == 0))]);
        Assert.True(outcome.Ok);
        Assert.Equal(2, outcome.Changes.Count);
        Assert.NotEmpty(outcome.Warnings);
        var modified = session.Serialize().Single().Content.ToArray();
        var expected = plaintext.ToArray();
        expected[target.Offset] = storedByte == 0 ? (byte)1 : (byte)0;
        Assert.Equal(expected, new DsssContainer(modified).Plaintext);
        using var reopened = new PragmataDocument(file.RelativePath, modified);
        Assert.Equal(storedByte == 0, reopened.Read(reopened.Layout.Header!.Find(KnownHashes.Cleared)!).AsBool());

        for (var cycle = 0; cycle < 2; cycle++)
        {
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.IsDirty);
            Assert.Equal(storedByte != 0, document.GetValue(target.Path).AsBool());
            Assert.Equal(storedByte != 0, document.GetValue(raw.ValueId).AsBool());
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
        }
    }
}
