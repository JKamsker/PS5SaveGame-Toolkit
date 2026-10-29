using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SerialPartTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void SamplePartSlots_IndependentCrcAndOnlySelectedBitsChange(string title)
    {
        var plugin = new GearboxPlugin();
        var wonderlands = title == "PPSA01493";
        var database = SerialDatabase.For(wonderlands);
        var tested = 0;
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch && !f.Directory.IsBackup))
        {
            using var document = (GearboxDocument)plugin.Open(file);
            foreach (var entry in document.Inventory.Entries)
            {
                var info = document.Inventory.Info(entry.Id);
                if (!info.Complete) continue;
                var slots = info.Parts.Concat(info.GenericParts).ToArray();
                for (var index = 0; index < slots.Length; index++)
                {
                    var slot = slots[index];
                    var candidate = Alternative(database, slot);
                    if (candidate is null) continue;
                    var changed = SerialParts.Set(entry.Serial, wonderlands, index, candidate);
                    var before = IndependentSerialProbe.Decode(entry.Serial);
                    var after = IndependentSerialProbe.Decode(changed); // independently checks folded CRC
                    var bits = new ProbeBits(after);
                    for (var bit = 0; bit < slot.Offset; bit++) _ = bits.Take(1);
                    Assert.Equal(int.Parse(candidate.Split(':')[1], CultureInfo.InvariantCulture), bits.Take(slot.Width));
                    Assert.NotEqual(entry.Serial, changed);
                    Assert.Equal(candidate, SerialParts.Slot(new SerialReader(wonderlands).Read(changed), index).Key);
                    Assert.Equal(before.Length, after.Length);
                    Assert.Equal(entry.Serial[..5], changed[..5]);
                    for (var bit = 0; bit < before.Length * 8; bit++)
                        if (bit < slot.Offset || bit >= slot.Offset + slot.Width)
                            Assert.Equal((before[bit / 8] >> (bit % 8)) & 1, (after[bit / 8] >> (bit % 8)) & 1);
                    Assert.Equal(entry.Serial, SerialParts.Set(changed, wonderlands, index, slot.Key));
                    Assert.Throws<SaveFormatException>(() => SerialParts.Set(entry.Serial, wonderlands, index, "ManufacturerData:1"));
                    tested++;
                }
            }
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        }
        Assert.True(tested > 100, "The real corpus must exercise many part slots.");
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void PickerEdit_ReopensRejectsInvalidIdsAndUndoRestoresExactBytes(string title)
    {
        var plugin = new GearboxPlugin();
        var file = GearboxTestData.PrimaryCharacter(title);
        var store = new ReferencePackStore([plugin]);
        var references = store.Pin(plugin, title);
        using var document = (GearboxDocument)plugin.Open(file, references);
        using var session = new DocumentSession("part-picker", file, plugin, document, references: references);
        _ = session.BuildView();
        var entry = document.Inventory.Entries.First(e => document.Inventory.Info(e.Id) is { Complete: true, Parts.Count: > 0 });
        _ = session.QueryCollection(entry.List, new() { Limit = 200 });
        var detail = session.Expand("detail/" + entry.Id);
        var field = Assert.IsType<TableNode>(detail.Last()).Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>()
            .First(f => f.Spec is ReferenceSpec && Alternative(SerialDatabase.For(title == "PPSA01493"), SerialParts.Slot(document.Inventory.Info(entry.Id),
                int.Parse(f.ValueId.Split('/').Last(), CultureInfo.InvariantCulture))) is not null);
        var slotIndex = int.Parse(field.ValueId.Split('/').Last(), CultureInfo.InvariantCulture);
        var originalInfo = document.Inventory.Info(entry.Id);
        var slot = SerialParts.Slot(originalInfo, slotIndex);
        var candidate = Alternative(SerialDatabase.For(title == "PPSA01493"), slot)!;
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.Contains("legal gear", field.Description);
        var candidates = session.SearchReferences(field.ValueId, new() { Category = slot.Category, Limit = 200 });
        Assert.Contains(candidates.Items, c => c.Eligibility.Selectable);
        Assert.All(session.SearchReferences(field.ValueId, new() { Category = "ManufacturerData" }).Items,
            c => Assert.False(c.Eligibility.Selectable));
        var forged = SerialDatabase.For(title == "PPSA01493").Find(ItemCatalogs.Indices, "ManufacturerData:1")! with { Category = slot.Category };
        Assert.False(Assert.IsType<ReferenceSpec>(field.Spec).Assess!(forged).Selectable);
        Assert.False(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement("missing:99"))]).Ok);
        Assert.False(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement("ManufacturerData:1"))]).Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.False(document.ApplyEdits([new("currency/618814354", FieldValue.Of(123)), new(field.ValueId, FieldValue.Of("ManufacturerData:1"))]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        var before = document.Root.Clone();
        Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(candidate))]).Ok);
        var output = document.Serialize().Single().Content;
        using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, output));
        Assert.Equal(candidate, SerialParts.Slot(reopened.Inventory.Info(entry.Id), slotIndex).Key);
        SerialWriteTests.AssertUnrelated(before, reopened.Root, entry.List);
        Assert.NotSame(originalInfo, document.Inventory.Info(entry.Id));
        Assert.Same(document.Inventory.Info(entry.Id), document.Inventory.Info(entry.Id));
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.Equal(slot.Key, SerialParts.Slot(document.Inventory.Info(entry.Id), slotIndex).Key);
        Assert.True(session.Redo().Ok);
        Assert.Equal(output.ToArray(), document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownTailAndOutOfRangeSlotsOrOffsets_RejectBeforeMutation(bool wonderlands)
    {
        var serial = IndependentSerialProbe.Encode(SyntheticItem.Payload(wonderlands, customizations: 1), wonderlands ? 5 : 4, -1);
        Assert.Throws<SaveFormatException>(() => SerialParts.Set(serial, wonderlands, 0, "InventoryGenericPartData:1"));
        var envelope = SerialEnvelope.Decode(serial, wonderlands);
        Assert.Throws<ArgumentOutOfRangeException>(() => envelope.Patch(int.MaxValue, 7, 1));
        Assert.Throws<SaveFormatException>(() => SerialParts.Slot(new(), -1));
        Assert.Equal(serial, envelope.EncodeOriginalSeed());
    }

    private static string? Alternative(SerialDatabase database, SerialAsset slot) => database.Records(ItemCatalogs.Indices)
        .FirstOrDefault(r => r.Category == slot.Category && r.Key != slot.Key && int.Parse(r.Attributes["index"], CultureInfo.InvariantCulture) < 1 << slot.Width)?.Key;
}
