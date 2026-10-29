using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherWriteSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateFieldEditsAreRejectedBeforeChangingValuesOrHistory(bool previouslyEdited)
    {
        var input = SyntheticPlayerSave.Create();
        var file = Import(input);
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("duplicate-writes", file, plugin, document);
        _ = session.BuildView();
        if (previouslyEdited)
        {
            Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming("inventory/1/quantity", FieldValue.Of(300))]).Ok);
        }
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        var state = session.State;

        var result = session.ApplyEdits([
            EnabledWriteFixture.Incoming("player/level", FieldValue.Of(2)),
            EnabledWriteFixture.Incoming("inventory/1/quantity", FieldValue.Of(500)),
            EnabledWriteFixture.Incoming("inventory/1/quantity", FieldValue.Of(600)),
        ]);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.ValueId == "inventory/1/quantity");
        Assert.Equal(1, document.GetValue("player/level").AsInt32());
        Assert.Equal(previouslyEdited ? 300 : 250, document.GetValue("inventory/1/quantity").AsInt32());
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(state, session.State);
        Assert.Equal(previouslyEdited, session.Undo().Ok);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static DiskSaveFile Import(byte[] input) => Assert.Single(SaveSetImporter.Import(
        [new ImportSource("PPSA04018/sdimg_TEST/Manual.sav", () => new MemoryStream(input))],
        Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N"))).AllDiskFiles);
}
