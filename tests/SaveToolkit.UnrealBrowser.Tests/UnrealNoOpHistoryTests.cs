using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.UnrealGvas;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;
using SaveToolkit.Testing.UnrealBrowser;

namespace SaveToolkit.UnrealBrowser.Tests;

public sealed class UnrealNoOpHistoryTests
{
    [Fact]
    public void CleanRepeatedJediScalarIsByteIdenticalWithoutHistoryOrRevision()
    {
        using var fixture = new JediSession();
        var before = fixture.Session.State;
        var result = fixture.Session.ApplyEdits([Edit(1.25)]);
        Assert.True(result.Ok);
        Assert.Empty(result.Changes);
        Assert.Equal(before, result.State);
        Assert.Empty(fixture.Session.Serialize());
        fixture.AssertBytes(UnrealBrowserFixtures.Settings());
        Assert.Empty(fixture.Session.GetChanges().Changes);
    }

    [Fact]
    public void DirtyRepeatedJediScalarKeepsOneHistoryEntryAndExactPatchedOutput()
    {
        using var fixture = new JediSession();
        var edit = fixture.Session.ApplyEdits([Edit(1.75)]);
        Assert.True(edit.Ok);
        var result = fixture.Session.ApplyEdits([Edit(1.75)]);
        Assert.Empty(result.Changes);
        Assert.Equal(edit.State, result.State);
        Assert.Equal(UnrealBrowserFixtures.Settings(1.75f), Assert.Single(fixture.Session.Serialize()).Content.ToArray());
        fixture.AssertBytes(UnrealBrowserFixtures.Settings(1.75f));
        Assert.Single(fixture.Session.GetChanges().Changes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
        fixture.AssertBytes(UnrealBrowserFixtures.Settings());
    }

    [Fact]
    public void RepeatedJediScalarAfterUndoPreservesRedoAndRevision()
    {
        using var fixture = new JediSession();
        Assert.True(fixture.Session.ApplyEdits([Edit(1.75)]).Ok);
        Assert.True(fixture.Session.Undo().Ok);
        var before = fixture.Session.State;
        Assert.True(before.CanRedo);
        var result = fixture.Session.ApplyEdits([Edit(1.25)]);
        Assert.Empty(result.Changes);
        Assert.Equal(before, result.State);
        fixture.AssertBytes(UnrealBrowserFixtures.Settings());
        Assert.True(fixture.Session.Redo().Ok);
        fixture.AssertBytes(UnrealBrowserFixtures.Settings(1.75f));
        Assert.False(fixture.Session.State.CanRedo);
    }

    [Fact]
    public void MixedJediBatchKeepsOneHistoryEntryAndRestoresAllOriginalBytes()
    {
        using var fixture = new JediSession();
        var result = fixture.Session.ApplyEdits([Edit(1.25), Edit(1.75)]);
        Assert.True(result.Ok);
        Assert.Equal(2, result.Changes.Count);
        Assert.Equal(1, result.State.Revision);
        fixture.AssertBytes(UnrealBrowserFixtures.Settings(1.75f));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
        fixture.AssertBytes(UnrealBrowserFixtures.Settings());
        Assert.True(fixture.Session.Redo().Ok);
        fixture.AssertBytes(UnrealBrowserFixtures.Settings(1.75f));
    }

    [Fact]
    public void InvalidJediBatchIsRejectedWithoutARepeatedScalarEffect()
    {
        using var fixture = new JediSession();
        var result = fixture.Session.ApplyEdits([Edit(1.25), new("property/0", JsonSerializer.SerializeToElement("invalid"))]);
        Assert.False(result.Ok);
        Assert.Equal(new DocumentState(false, false, false), result.State);
        fixture.AssertBytes(UnrealBrowserFixtures.Settings());
    }

    private static IncomingEdit Edit(double value) => new("property/0", JsonSerializer.SerializeToElement(value));

    private sealed class JediSession : IDisposable
    {
        private readonly ISaveDocument _document;
        public DocumentSession Session { get; }

        public JediSession()
        {
            var root = Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N"));
            var save = SaveSetImporter.Import([new ImportSource("PPSA02199/sdimg_E2E/settings.sav",
                () => new MemoryStream(UnrealBrowserFixtures.Settings(), writable: false))], root);
            var file = save.AllDiskFiles.Single();
            var plugin = new UnrealGvasPlugin();
            _document = plugin.Open(file);
            Session = new DocumentSession("jedi", file, plugin, _document);
            Session.BuildView();
        }

        public void AssertBytes(byte[] expected) => Assert.Equal(expected, Assert.Single(_document.Serialize()).Content.ToArray());
        public void Dispose() => Session.Dispose();
    }
}
