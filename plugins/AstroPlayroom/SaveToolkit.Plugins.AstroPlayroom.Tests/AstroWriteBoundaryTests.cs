using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroWriteBoundaryTests
{
    [Fact]
    public void RepeatedFieldEditsAtExactCapacityUndoAndRedoWithoutNormalizingImportedSpelling()
    {
        const string root = "\r\n<PreInstall><Common><NumCoin>0000000000</NumCoin>" +
            "<PlayCount>1</PlayCount></Common></PreInstall>opaque-中-";
        var text = root + new string('x', AstroTestData.AllocationSize - AstroTestData.TextOffset -
            Encoding.UTF8.GetByteCount(root) - 1);
        var original = AstroTestData.Bytes(text);
        var file = ImportAuthoredAllocation(original);
        var plugin = new AstroPlayroomPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("astro-capacity", file, plugin, document);
        _ = session.BuildView();
        var coins = AstroTestData.Find(document, AstroTestData.CoinsPath);
        var count = AstroTestData.Find(document, AstroTestData.CountPath);

        // The final coin spelling frees exactly the nine bytes needed by PlayCount.
        // Keep both changes to the same field in history, including its intermediate value.
        var result = session.ApplyEdits([
            Edit(coins.ValueId, int.MaxValue), Edit(coins.ValueId, 1), Edit(count.ValueId, int.MaxValue)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok);
        Assert.Equal(3, result.Changes.Count);
        Assert.Equal(FieldValue.Of((long)int.MaxValue), result.Changes[1].OldValue);
        var expected = AstroTestData.Bytes(text
            .Replace("<NumCoin>0000000000", "<NumCoin>1", StringComparison.Ordinal)
            .Replace("<PlayCount>1", "<PlayCount>2147483647", StringComparison.Ordinal));
        Assert.Equal(expected, AstroTestData.Output(document));
        Assert.NotEqual(0, expected[^2]);
        Assert.Equal(0, expected[^1]);
        using (var reopened = AstroTestData.Open(expected))
        {
            Assert.Equal(FieldValue.Of(1L), reopened.GetValue(coins.ValueId));
            Assert.Equal(FieldValue.Of((long)int.MaxValue), reopened.GetValue(count.ValueId));
        }

        var revision = session.State.Revision;
        var rejected = session.ApplyEdits([Edit(coins.ValueId, 12)], revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.Empty(rejected.Changes);
        Assert.Equal(revision, session.State.Revision);
        Assert.Equal(expected, AstroTestData.Output(document));

        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, AstroTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(expected, AstroTestData.Output(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, AstroTestData.Output(document));
    }

    private static DiskSaveFile ImportAuthoredAllocation(byte[] original)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("PPSA01325/sdimg_sce_sdmemory/memory.dat").Open();
            entry.Write(original);
        }

        var set = SaveSetImporter.Import([new ImportSource("authored.zip", () => new MemoryStream(zip.ToArray()))],
            Path.Combine(TestScratch.ProcessFolder, nameof(AstroWriteBoundaryTests)));
        return Assert.Single(set.AllDiskFiles);
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));
}
