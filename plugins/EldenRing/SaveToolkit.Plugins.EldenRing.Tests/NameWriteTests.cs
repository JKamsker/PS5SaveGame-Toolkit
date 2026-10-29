using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class NameWriteTests
{
    [Theory]
    [InlineData("Ploxiテスト")]
    [InlineData("短名")]
    [InlineData("12345678901234🌙")]
    public void SampleNames_PersistBothMirrorsAndUndoRedoExactly(string name)
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var slot = EldenRingParser.Parse(original).Slots[0];
        var offsets = new[] { slot.PlayerOffset!.Value + EldenRingLayout.NameOffset, slot.ProfileOffset };
        using var session = WriteTestSupport.Session(file, out var document);
        var field = Assert.IsType<FieldNode>(Assert.IsType<TableNode>(Assert.IsType<TabsNode>(session.BuildView()).Tabs[0].Children[^1])
            .Rows[0].Cells["name"]);
        Assert.False(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.Contains("may reject", field.Warning);
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(field.ValueId, name)]).Ok);
        var edited = WriteTestSupport.Bytes(document);
        var expected = original.ToArray();
        var encoded = Encoding.Unicode.GetBytes(name);
        foreach (var offset in offsets)
        {
            encoded.CopyTo(expected, offset);
            expected.AsSpan(offset + encoded.Length, sizeof(ushort)).Clear();
            Assert.Equal(name, EldenRingParser.ReadName(edited, offset));
        }
        Assert.True(expected.AsSpan().SequenceEqual(edited));
        WriteTestSupport.OnlyRanges(original, edited, offsets.Select(offset => (offset, EldenRingLayout.NameBytes)).ToArray());
        using var reopened = WriteTestSupport.Reopen(document);
        _ = reopened.BuildView();
        Assert.Equal(name, reopened.GetValue(field.ValueId).AsString());
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(original, document);
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(edited, document);
    }

    [Fact]
    public void RepeatedRenames_PreserveDifferentTrailingBytesInEachMirror()
    {
        var fixture = new SyntheticSave([0, 9], trailingBytes: 19);
        fixture.Rename(9, "Old");
        var offsets = new[] { fixture.Players[9] + SyntheticSave.Name, fixture.Profile(9) };
        fixture.Bytes.AsSpan(offsets[0] + 8, SyntheticSave.NameBytes - 8).Fill(0x35);
        fixture.Bytes.AsSpan(offsets[1] + 8, SyntheticSave.NameBytes - 8).Fill(0x72);
        fixture.Bytes.AsSpan(SyntheticSave.FileSize).Fill(0xA5);
        var original = fixture.Bytes.ToArray();
        using var document = fixture.Open();
        _ = document.BuildView();
        var firstEdit = document.ApplyEdits([new("slots/9/name", FieldValue.Of("長い名前🌙"))]);
        Assert.True(firstEdit.Succeeded);
        var first = WriteTestSupport.Bytes(document);
        var secondEdit = document.ApplyEdits([new("slots/9/name", FieldValue.Of("短"))]);
        Assert.True(secondEdit.Succeeded);
        foreach (var offset in offsets)
            Assert.True(original.AsSpan(offset + 4, SyntheticSave.NameBytes - 4)
                .SequenceEqual(WriteTestSupport.Bytes(document).AsSpan(offset + 4, SyntheticSave.NameBytes - 4)));
        Assert.True(document.ApplyEdits(secondEdit.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(first, document);
        Assert.True(document.ApplyEdits(firstEdit.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(original, document);
        Assert.True(document.ApplyEdits(firstEdit.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(first, document);
    }

    [Theory]
    [InlineData("Hero\t0")]
    [InlineData("Hero\n0")]
    [InlineData("Hero\u007F0")]
    public void ImportedControlCharacters_CanBeRestoredExactlyBySessionUndo(string importedName)
    {
        var fixture = new SyntheticSave([0]);
        fixture.Rename(0, importedName);
        var offsets = new[] { fixture.Players[0] + SyntheticSave.Name, fixture.Profile(0) };
        var paddingStart = (importedName.Length + 1) * sizeof(ushort);
        fixture.Bytes.AsSpan(offsets[0] + paddingStart, SyntheticSave.NameBytes - paddingStart).Fill(0x35);
        fixture.Bytes.AsSpan(offsets[1] + paddingStart, SyntheticSave.NameBytes - paddingStart).Fill(0x72);
        using var disk = new SyntheticDiskSave(fixture.Bytes);
        using var session = WriteTestSupport.Session(disk.File, out var document);
        const string nameId = "slots/0/name";
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(nameId, "Renamed")]).Ok);
        var renamed = WriteTestSupport.Bytes(document);
        Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(nameId, "Other\tName")]).Ok);
        WriteTestSupport.EqualBytes(renamed, document);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(renamed, document);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678901234567")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    public void InvalidNames_RejectAtomicBatches(string invalid) => RejectName(FieldValue.Of(invalid));

    [Fact]
    public void InvalidSurrogatesAndWrongKinds_RejectDirectPluginCalls()
    {
        RejectName(FieldValue.Of(new string((char)0xD800, 1)));
        RejectName(FieldValue.Of(new string((char)0xDC00, 1)));
        RejectName(FieldValue.Of(12L));
    }

    private static void RejectName(FieldValue value)
    {
        var fixture = new SyntheticSave([0, 9]);
        using var document = fixture.Open();
        _ = document.BuildView();
        _ = document.ExpandNode("characters/0");
        Assert.False(document.ApplyEdits([new("slots/0/runes", FieldValue.Of(101L)), new("slots/9/name", value)]).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }
}
