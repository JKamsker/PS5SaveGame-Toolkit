using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiEditingTests
{
    [Fact]
    public void EveryEligibleRecordEditsReopensAndRestoresExactlyAcrossEverySlot()
    {
        var editedNames = new HashSet<string>();
        foreach (var file in Files)
        {
            var original = file.ReadAllBytes();
            using var doc = Open(file);
            if (Word(original, 0) == 0) continue;
            var offsets = InventoryRecords(original).ToDictionary(row => row.Name, row => row.Data);
            var quantities = Inventory(doc).Rows.Select(row => (row.Key, Field: Assert.IsType<FieldNode>(row.Cells["quantity"])))
                .Where(row => !row.Field.Spec.ReadOnly).ToArray();
            foreach (var (name, field) in quantities)
            {
                var max = Assert.IsType<IntegerSpec>(field.Spec).Max;
                Assert.Equal(Confidence.Inferred, field.Confidence);
                Assert.Contains("Not validated on console", field.Description);
                Assert.Contains("game may reject", field.Warning);
                Assert.False(doc.ApplyEdits([new(field.ValueId, FieldValue.Of(max + 1))]).Succeeded);
                Assert.False(doc.ApplyEdits([new(field.ValueId, FieldValue.Of(-1L))]).Succeeded);
                Assert.Equal(original, Assert.Single(doc.Serialize()).Content.ToArray());
                foreach (var target in new[] { (Int128)0, max })
                {
                    var outcome = doc.ApplyEdits([new(field.ValueId, FieldValue.Of(target))]);
                    Assert.True(outcome.Succeeded);
                    Assert.Contains(outcome.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
                    var data = offsets[name];
                    var expected = original.ToArray();
                    BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(data + 5), checked((uint)target));
                    var ammo = Inventory(doc).Rows.Single(row => row.Key == name).Cells["copy"] is FieldNode;
                    if (ammo) BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(data + 9), checked((uint)target));
                    Repair(expected);
                    var edited = Assert.Single(doc.Serialize()).Content.ToArray();
                    Assert.Equal(expected, edited);
                    WriteAssertions.OnlyRangesChanged(original, edited, [(12, 4), (data + 5, ammo ? 8 : 4)]);
                    using var reopened = Open(WithBytes(edited));
                    Assert.Equal(target, reopened.GetValue(field.ValueId).AsInteger());
                    if (ammo) Assert.Equal(target, reopened.GetValue(field.ValueId + "-copy").AsInteger());
                    Assert.True(doc.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
                    Assert.Equal(original, Assert.Single(doc.Serialize()).Content.ToArray());
                }
                editedNames.Add(name);
            }
        }
        Assert.Equal(14, editedNames.Count);
    }

    [Fact]
    public void InvalidTypesAndMixedBatchesRejectWithoutChangingAnyByte()
    {
        var file = Slot("suckerpunchUser4");
        using var doc = Open(file);
        FieldValue[] values = [FieldValue.Of("7"), FieldValue.Of(7.0), FieldValue.Of(true), FieldValue.Of(Int128.MaxValue), FieldValue.Of(-1L)];
        foreach (var value in values)
        {
            var result = doc.ApplyEdits([new(MonId, FieldValue.Of(100L)), new(ArrowId, value)]);
            Assert.False(result.Succeeded);
            Assert.Empty(result.Changes);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(doc.Serialize()).Content.ToArray());
        }
        Assert.False(doc.ApplyEdits([new("yotei/inventory/TestItem/quantity", FieldValue.Of(1L))]).Succeeded);
    }

    [Fact]
    public void ScalarCoreUndoRedoCoversEveryNewWritableRecord()
    {
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser4"));
        var plugin = new SuckerPunchPlugin();
        using var doc = plugin.Open(file);
        using var session = new DocumentSession("yotei-edits", file, plugin, doc);
        session.BuildView();
        var fields = Inventory(doc).Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"]))
            .Where(field => !field.Spec.ReadOnly).ToArray();
        Assert.Equal(14, fields.Length);
        foreach (var field in fields)
        {
            var value = field.Value.AsInteger() == 0 ? "1" : "0";
            Assert.True(session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(value))]).Ok);
            var edited = Assert.Single(doc.Serialize()).Content.ToArray();
            Assert.True(session.Undo().Ok);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(doc.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(edited, Assert.Single(doc.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
        }
    }

    [Fact]
    public void CountCopiesEligibilityAndUnknownTailArePreserved()
    {
        var bytes = Slot("suckerpunchUser4").ReadAllBytes();
        var records = InventoryRecords(bytes).ToDictionary(row => row.Name, row => row.Data);
        bytes[records["Mon"] + 4] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(records["ArrowNormal"] + 9), 8);
        bytes[^1] = 0xA5; // Synthetic non-zero tail tests preservation, not the game's checksum coverage.
        Repair(bytes);
        using var doc = Open(WithBytes(bytes));
        Assert.False(doc.ApplyEdits([new(MonId, FieldValue.Of(1L))]).Succeeded);
        Assert.False(doc.ApplyEdits([new(ArrowId, FieldValue.Of(1L))]).Succeeded);
        Assert.True(doc.ApplyEdits([new("yotei/inventory/Flowers/quantity", FieldValue.Of(1L))]).Succeeded);
        var edited = Assert.Single(doc.Serialize()).Content.ToArray();
        Assert.Equal(0xA5, edited[^1]);
        Assert.Equal(8U, Word(edited, records["ArrowNormal"] + 9));
        Assert.Equal(0, edited[records["Mon"] + 4]);
    }

    [Fact]
    public void MismatchedChecksumCanBeInspectedWithoutRepairOrEdits()
    {
        var bytes = Slot("suckerpunchUser4").ReadAllBytes();
        bytes[12] ^= 1;
        using var doc = Open(WithBytes(bytes));
        Assert.False(doc.CanEdit);
        Assert.False(doc.ApplyEdits([new(MonId, FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(doc.Serialize()).Content.ToArray());
    }
}
