using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class SampleTests(ITestOutputHelper output)
{
    private const int SamplePlayerOffset = 0xA2FF;
    private const int SampleRunesOffset = SamplePlayerOffset + SyntheticSave.Runes;
    private const uint SampleRunes = 1147;
    private const uint EditedRunes = SampleRunes + 1;

    [Fact]
    public void JapaneseSample_HasKnownCharacterAndMatchingProfile()
    {
        using var document = new EldenRingPlugin().Open(WriteTestSupport.Sample());
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var table = Assert.IsType<TableNode>(view.Tabs[0].Children[^1]);
        Assert.Equal(10, table.Rows.Count);
        Assert.Single(table.Rows, row => Assert.IsType<TextNode>(row.Cells["status"]).Text == "Active");
        _ = document.ExpandNode("characters/0");
        Assert.True(document.CanEdit);
        Assert.Equal("Ploxi", document.GetValue("slots/0/name").AsString());
        Assert.Equal(12U, document.GetValue("slots/0/level").AsUInt32());
        Assert.Equal(SampleRunes, document.GetValue("slots/0/runes").AsUInt32());
        Assert.Equal(4427U, document.GetValue("slots/0/rune-memory").AsUInt32());
        Assert.Equal(4_597_996UL, document.GetValue("slots/0/playtime").AsUInt64());
        Assert.Equal(150U, document.GetValue("slots/0/version").AsUInt32());
        string[] attributes = ["vigor", "mind", "endurance", "strength", "dexterity", "intelligence", "faith", "arcane"];
        uint[] expected = [13, 13, 11, 11, 16, 10, 8, 9];
        Assert.Equal(expected, attributes.Select(attribute => document.GetValue($"slots/0/attributes/{attribute}").AsUInt32()));
    }

    [Fact]
    public void EveryArchiveRecordAndBackup_OpensAndRoundTripsIdentically()
    {
        var plugin = new EldenRingPlugin();
        var files = SampleSaves.Load("PPSA03169").AllDiskFiles.Where(file => file.FileName == "memory.dat").ToArray();
        Assert.Equal(2, files.Length);
        Assert.Single(files, file => file.Directory.IsBackup);
        Assert.Single(files, file => !file.Directory.IsBackup);
        Assert.Single(files.Select(file => file.Sha256).Distinct());
        foreach (var file in files)
        {
            Assert.Equal(SyntheticSave.FileSize, file.Length);
            Assert.Equal(ProbeScore.Title, plugin.Probe(file).Score);
            using var document = plugin.Open(file);
            _ = document.BuildView();
            var serialized = Assert.Single(document.Serialize());
            Assert.Equal(file.RelativePath, serialized.RelativePath);
            Assert.True(file.ReadAllBytes().AsSpan().SequenceEqual(serialized.Content.Span));
            Assert.Equal("Ploxi", document.GetValue("slots/0/name").AsString());
        }
    }

    [Fact]
    public void RealRuneEdit_UndoAndRedo_PreserveEveryOtherByte()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var expected = original.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(SampleRunesOffset), EditedRunes);
        var plugin = new EldenRingPlugin();
        using var session = new DocumentSession("eldenring-test", file, plugin, plugin.Open(file));
        _ = session.BuildView();
        _ = session.Expand("characters/0");
        var applied = session.ApplyEdits([new IncomingEdit("slots/0/runes", JsonSerializer.SerializeToElement(EditedRunes))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(applied.Ok);
        Assert.Equal(SampleRunes, Assert.Single(applied.Changes).OldValue.AsUInt32());
        Assert.True(expected.AsSpan().SequenceEqual(Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.Span));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize(cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(session.Redo().Ok);
        Assert.True(expected.AsSpan().SequenceEqual(Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.Span));
        Assert.True(original.AsSpan().SequenceEqual(file.ReadAllBytes()));
    }

    [Fact]
    public void SampleOpenAndViews_KeepCharactersAndRawDataLazyAndCuratedExpansionBounded()
    {
        var file = WriteTestSupport.Sample();
        var watch = Stopwatch.StartNew();
        using var document = new EldenRingPlugin().Open(file);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var fields = document.ExpandNode("characters/0");
        watch.Stop();
        output.WriteLine($"Open + initial view + curated slot: {watch.Elapsed.TotalMilliseconds:F2} ms (diagnostic only).");
        Assert.Equal(6, view.Tabs.Count);
        var characters = Assert.Single(view.Tabs, tab => tab.Title == "Characters");
        Assert.Equal(SyntheticSave.SlotCount, characters.Children.Count);
        Assert.Equal(Enumerable.Range(0, SyntheticSave.SlotCount).Select(index => $"characters/{index}"),
            characters.Children.Select(child => Assert.IsType<SectionNode>(child).LazyId));
        var raw = Assert.Single(view.Tabs, tab => tab.Title == "Raw");
        Assert.Equal(SyntheticSave.SlotCount + 3, raw.Children.Count); // Slots, header, profile and regulation.
        Assert.All(characters.Children.Concat(raw.Children), child =>
        {
            var section = Assert.IsType<SectionNode>(child);
            Assert.NotNull(section.LazyId);
            Assert.Empty(section.Children);
        });
        Assert.InRange(fields.Count, 1, 32);
        Assert.Equal(SampleRunes, Assert.Single(fields.OfType<FieldNode>(), field => field.ValueId == "slots/0/runes").Value.AsUInt32());
        WriteTestSupport.EqualBytes(file.ReadAllBytes(), document);
    }
}
