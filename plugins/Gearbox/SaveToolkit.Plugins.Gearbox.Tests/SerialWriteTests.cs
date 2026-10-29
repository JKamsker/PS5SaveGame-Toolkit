using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SerialWriteTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EverySerialWrite_RepairsIndependentCrcPreservesEveryOtherBitAndRoundTrips(string title)
    {
        var plugin = new GearboxPlugin();
        var wonderlands = title == "PPSA01493";
        var tested = new HashSet<string>();
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch && !f.Directory.IsBackup))
        {
            using var document = (GearboxDocument)plugin.Open(file);
            _ = document.BuildView();
            var originalRoot = document.Root.Clone();
            foreach (var entry in document.Inventory.Entries.ToArray())
            {
                var info = new SerialReader(wonderlands).Read(entry.Serial);
                foreach (var property in new[] { "level", "rerolls", "chaos" })
                {
                    var offset = property switch { "level" => info.LevelOffset, "rerolls" => info.RerollOffset, _ => info.ChaosOffset };
                    if (offset is null) continue;
                    var old = SerialWrites.Value(info, property)!.Value;
                    var next = old == 1 ? 2 : 1;
                    _ = document.Query(entry.List, new() { Limit = 200 });
                    var detail = document.ExpandNode("detail/" + entry.Id).OfType<FieldNode>().Single(f => f.ValueId == entry.Id + "/" + property);
                    Assert.Equal(Confidence.Inferred, detail.Confidence);
                    Assert.Contains("Not validated on console", detail.Description);
                    Assert.True(document.ApplyEdits([new(detail.ValueId, FieldValue.Of(next))]).Succeeded);
                    var output = document.Serialize().Single().Content;
                    using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, output));
                    var changed = reopened.Inventory.Entries.Single(e => e.List == entry.List && e.Index == entry.Index).Serial;
                    Assert.Equal(next, SerialWrites.Value(new SerialReader(wonderlands).Read(changed), property));
                    Assert.Equal(entry.Serial[..5], changed[..5]);
                    var before = IndependentSerialProbe.Decode(entry.Serial);
                    var after = IndependentSerialProbe.Decode(changed);
                    var width = property == "rerolls" ? 8 : 7;
                    Assert.Equal(before.Length, after.Length);
                    var diff = ByteDiffer.Compare(before, after, options: new() { MergeGap = 0, ContextBytes = 0, MaxPreviewBytes = 0 });
                    Assert.All(diff.Ranges, range => Assert.True(range.Offset >= offset.Value / 8 && range.End <= (offset.Value + width + 7) / 8));
                    for (var bit = 0; bit < before.Length * 8; bit++)
                        if (bit < offset || bit >= offset + width) Assert.Equal((before[bit / 8] >> (bit % 8)) & 1, (after[bit / 8] >> (bit % 8)) & 1);
                    AssertUnrelated(originalRoot, reopened.Root, entry.List);
                    Assert.True(document.ApplyEdits([new(detail.ValueId, FieldValue.Of(old))]).Succeeded);
                    Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
                    Assert.False(document.ApplyEdits([new(detail.ValueId, FieldValue.Of(-1))]).Succeeded);
                    Assert.False(document.ApplyEdits([new(detail.ValueId, FieldValue.Of(SerialWrites.Maximum(wonderlands, property) + 1))]).Succeeded);
                    tested.Add(property);
                }
            }
        }
        Assert.Contains("level", tested);
        Assert.Contains("rerolls", tested);
        if (wonderlands) Assert.Contains("chaos", tested);
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void ItemFlagsAndSerialFields_HostUndoRedoRestoresExactBytes(string title)
    {
        var plugin = new GearboxPlugin();
        var file = GearboxTestData.PrimaryCharacter(title);
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("serial-write", file, plugin, document);
        _ = session.BuildView();
        var page = session.QueryCollection("inventory_items", new() { Limit = 200 });
        foreach (var row in page.Rows.Take(3))
        {
            _ = session.QueryCollection("inventory_items", new() { Limit = 200 });
            var detail = (SectionNode)row.Cells["detail"];
            var fields = session.Expand(detail.LazyId!).OfType<FieldNode>().Where(f => !f.Spec.ReadOnly).ToArray();
            foreach (var field in fields)
            {
                var next = field.Value.AsInt64() == 1 ? 2 : 1;
                var original = document.Serialize().Single().Content.ToArray();
                _ = session.QueryCollection("inventory_items", new() { Limit = 200 });
                _ = session.Expand(detail.LazyId!);
                var result = session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(next.ToString(System.Globalization.CultureInfo.InvariantCulture)))]);
                Assert.True(result.Ok, field.ValueId + ": " + string.Join("; ", result.Errors.Select(e => e.Message)));
                var edited = document.Serialize().Single().Content.ToArray();
                if (field.ValueId.EndsWith("/mark", StringComparison.Ordinal))
                {
                    var beforeRoot = GearboxContainer.Parse(original, title, false).Decode();
                    var afterRoot = GearboxContainer.Parse(edited, title, false).Decode();
                    var index = document.Inventory.Find(row.Key).Index;
                    var beforeItem = beforeRoot.Children("inventory_items").ElementAt(index);
                    var afterItem = afterRoot.Children("inventory_items").ElementAt(index);
                    AssertUnrelated(beforeItem, afterItem, "flags");
                    Assert.Equal(beforeItem.Integer("flags") & ~6L, afterItem.Integer("flags") & ~6L);
                }
                Assert.True(session.Undo().Ok);
                Assert.Equal(original, document.Serialize().Single().Content.ToArray());
                Assert.True(session.Redo().Ok);
                Assert.Equal(edited, document.Serialize().Single().Content.ToArray());
                Assert.True(session.Undo().Ok);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownTail_IsPreservedForHeaderLevelButRejectedForTailEdits(bool wonderlands)
    {
        var serial = IndependentSerialProbe.Encode(SyntheticItem.Payload(wonderlands, customizations: 1), wonderlands ? 5 : 4, int.MinValue);
        var changed = SerialWrites.Set(serial, wonderlands, "level", 17);
        Assert.Equal(17, new SerialReader(wonderlands).Read(changed).Level);
        Assert.Throws<SaveFormatException>(() => SerialWrites.Set(serial, wonderlands, "rerolls", 1));
        var corrupt = serial.ToArray();
        corrupt[^1] ^= 1;
        Assert.Throws<SaveFormatException>(() => SerialWrites.Set(corrupt, wonderlands, "level", 1));
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void Serialize_RejectsNewCorruptSerialWhilePreservingOpaqueOriginals(string title)
    {
        var plugin = new GearboxPlugin();
        var file = GearboxTestData.PrimaryCharacter(title);
        using var document = (GearboxDocument)plugin.Open(file);
        var entry = document.Inventory.Entries.First();
        var corrupt = entry.Serial.ToArray();
        corrupt[^1] ^= 1;
        document.Inventory.SetSerial(entry.Id, corrupt);
        Assert.Throws<SaveFormatException>(() => document.Serialize());
        var container = GearboxContainer.Parse(file.ReadAllBytes(), title, false);
        var root = container.Decode();
        root.Children("inventory_items").First().Last("item_serial_number")!.Set(corrupt);
        var originalCorrupt = container.Encode(root);
        using var preserved = plugin.Open(GearboxTestData.Overlay(file, originalCorrupt));
        Assert.Equal(originalCorrupt, preserved.Serialize().Single().Content.ToArray());
    }


    internal static void AssertUnrelated(ProtoMessage before, ProtoMessage after, params string[] changedLists)
    {
        Assert.Equal(before.Fields.Where(f => !changedLists.Contains(f.Spec?.Name)).Select(f => Convert.ToHexString(f.Encode())),
            after.Fields.Where(f => !changedLists.Contains(f.Spec?.Name)).Select(f => Convert.ToHexString(f.Encode())));
    }
}
