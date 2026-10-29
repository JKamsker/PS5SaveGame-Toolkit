using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class CharacterWriteTests
{
    [Theory]
    [InlineData("PPSA01911", "ff7remake000", 0)]
    [InlineData("PPSA01911", "ff7remakeplus000", 5)]
    [InlineData("PPSA08664", "ff7rebirth000", 0)]
    [InlineData("PPSA18936", "ff7rebirth000", 0)]
    public void EachCharacterWriteReopensHasExactByteScopeAndSessionUndoRedo(string title, string name, int character)
    {
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name);
        var plugin = new FinalFantasy7Plugin();
        var original = file.ReadAllBytes();
        var schema = Ff7Schema.ForTitle(title)!;
        var offset = 0x30 + character * (schema.IsRemake ? 0x40 : 0x100);
        var key = $"character/{character}";
        foreach (var suffix in new[] { "hp", "mp", "level", "experience" })
        {
            var document = plugin.Open(file);
            using var session = new DocumentSession("character-writes", file, plugin, document);
            _ = session.BuildView();
            var old = document.GetValue(key + "/" + suffix).AsInt32();
            var target = suffix is "hp" or "mp" ? Math.Max(0, old - 1) : suffix == "level" ? 25 : 50_000;
            Assert.NotEqual(old, target);
            Assert.True(session.ApplyEdits([new(key + "/" + suffix, JsonSerializer.SerializeToElement(target))]).Ok);
            var edited = Assert.Single(document.Serialize()).Content.ToArray();
            var allowed = suffix == "hp" ? Enumerable.Range(offset + 0x10, 4)
                : suffix == "mp" ? Enumerable.Range(offset + 0x18, 4)
                : new[] { offset }.Concat(Enumerable.Range(offset + 0x20, 4));
            ByteChangeAssertions.OnlyOffsetsChanged(original, edited, allowed);
            using var reopened = plugin.Open(MemorySaveSet.SingleFile(name, edited, title));
            _ = reopened.BuildView();
            Assert.Equal(target, reopened.GetValue(key + "/" + suffix).AsInt32());
            if (suffix is "level" or "experience")
            {
                var thresholds = ProgressionEdits.CharacterCurves(schema)[character];
                Assert.Equal(thresholds.Count(value => value <= reopened.GetValue(key + "/experience").AsUInt32()),
                    reopened.GetValue(key + "/level").AsInt32());
            }
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    public void InvalidCharacterBatchesRejectBeforeChangingBytes(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        using var document = new FinalFantasy7Plugin().Open(file);
        _ = document.BuildView();
        var oldHp = document.GetValue("character/0/hp").AsInt32();
        foreach (var invalid in new[]
        {
            new ValueEdit("character/0/level", FieldValue.Of(0L)),
            new ValueEdit("character/0/level", FieldValue.Of(71L)),
            new ValueEdit("character/0/experience", FieldValue.Of(10_000_000L)),
            new ValueEdit("character/0/hp", FieldValue.Of((long)oldHp + 1)),
            new ValueEdit("character/0/mp", FieldValue.Of(-1L)),
            new ValueEdit("character/0/max-hp", FieldValue.Of(1L)),
        })
        {
            Assert.False(document.ApplyEdits([new("character/0/hp", FieldValue.Of(1L)), invalid]).Succeeded);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.False(document.ApplyEdits([new("character/0/level", FieldValue.Of(2L)),
            new("character/0/experience", FieldValue.Of(100_000L))]).Succeeded);
        Assert.False(document.ApplyEdits([new("character/0/level", FieldValue.Of(2L)),
            new("character/0/level", FieldValue.Of(3L))]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RebirthWeaponReferenceUsesOwnedFamilyAndPreservesAllSocketAndDetailBytes()
    {
        const string title = "PPSA08664";
        var plugin = new FinalFantasy7Plugin();
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var original = file.ReadAllBytes();
        var originalInstance = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(0xCC));
        var target = Enumerable.Range(0, 128).Select(slot => 0x5FA6C + slot * 0x20)
            .Where(offset => BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(offset + 4)) is >= 1001 and <= 1007)
            .Select(offset => BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(offset)))
            .First(instance => instance != originalInstance);
        var document = plugin.Open(file);
        using var session = new DocumentSession("weapon", file, plugin, document);
        _ = session.BuildView();
        foreach (var invalid in new[] { 1002U, uint.MaxValue, 123456U,
            BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(0x1CC)) })
        {
            Assert.False(document.ApplyEdits([new("character/0/weapon", FieldValue.Of((long)invalid))]).Succeeded);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.True(session.ApplyEdits([new("character/0/weapon", JsonSerializer.SerializeToElement(target))]).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        ByteChangeAssertions.OnlyOffsetsChanged(original, edited, Enumerable.Range(0xCC, 4));
        using var reopened = plugin.Open(MemorySaveSet.SingleFile(file.FileName, edited, title));
        _ = reopened.BuildView();
        Assert.Equal(target, reopened.GetValue("character/0/weapon").AsUInt32());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UndoRestoresAnInconsistentOriginalXpPairWithoutNormalizingIt()
    {
        const string title = "PPSA08664";
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var original = file.ReadAllBytes();
        original[0x30] = 40;
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(0x50), 0);
        var plugin = new FinalFantasy7Plugin();
        ISaveDocument Open() => plugin.Open(MemorySaveSet.SingleFile(file.FileName, original, title));
        var document = Open();
        using var session = new DocumentSession("original-pair", file, plugin, document, openOriginal: Open);
        _ = session.BuildView();
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyEdits([new("character/0/level", JsonSerializer.SerializeToElement(25))]).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void NewCharacterFieldsKeepWarningsIncludingDemo()
    {
        foreach (var title in new[] { "PPSA01911", "PPSA08664", "PPSA18936" })
        {
            var schema = Ff7Schema.ForTitle(title)!;
            var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
            using var document = new FinalFantasy7Plugin().Open(file);
            var view = Assert.IsType<TabsNode>(document.BuildView());
            var table = Assert.IsType<TableNode>(Assert.Single(view.Tabs.Single(tab => tab.Title == "Characters").Children.OfType<TableNode>()));
            var fields = table.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>();
            Assert.Contains(fields, field => !field.Spec.ReadOnly);
            Assert.All(fields.Where(field => !field.Spec.ReadOnly), field =>
            {
                Assert.Equal(Confidence.Inferred, field.Confidence);
                Assert.Contains("game may reject", field.Description);
                Assert.Contains("Not validated on console", field.Warning);
            });
        }
    }
}
