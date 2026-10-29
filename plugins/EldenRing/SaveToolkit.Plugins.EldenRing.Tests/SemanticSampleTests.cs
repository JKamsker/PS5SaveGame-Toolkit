using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class SemanticSampleTests
{
    [Fact]
    public void Session_PagesRegisterReferencesAndPickerInspectionPreservesRuneUndo()
    {
        var file = WriteTestSupport.Sample();
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var session = new DocumentSession("eldenring-semantic", file, plugin, plugin.Open(file, references), references: references);
        _ = session.BuildView();
        var page = session.QueryCollection("slots/0/inventory/held", new() { Text = "Rowa Fruit" });
        var field = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["item"]);
        Assert.Equal("Rowa Fruit", session.FindReference(field.ValueId, field.Value.AsString(), page.Revision).Label);
        var candidates = session.SearchReferences("item", new() { Text = "Scimitar" }, actionId: "references/inspect");
        Assert.Equal(13, candidates.Items.Count);
        Assert.Contains(candidates.Items, item => item.RawId == "0x006CF2A0" && item.Label == "Scimitar");
        _ = session.Expand("characters/0");
        Assert.True(session.ApplyEdits([new IncomingEdit("slots/0/runes", JsonSerializer.SerializeToElement(1148))]).Ok);
        var lookup = session.InvokeAction("references/inspect", new Dictionary<string, JsonElement> { ["item"] = JsonSerializer.SerializeToElement("0x400050F0") });
        Assert.True(lookup.Ok);
        Assert.Empty(lookup.Changes);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize());
    }

    [Fact]
    public void JapaneseSnapshot_ReproducesRangesCountsAndRepresentativeIdentities()
    {
        var file = WriteTestSupport.Sample();
        var bytes = file.ReadAllBytes();
        var slot = EldenRingParser.Parse(bytes).Slots[0];
        var semantic = SemanticReader.Read(bytes, slot);
        var held = semantic.Containers.Single(container => container.Id == "held");
        var storage = semantic.Containers.Single(container => container.Id == "storage");
        Assert.Equal(0xA6A3, held.Range.Offset);
        Assert.Equal(63U, held.CommonCount);
        Assert.Equal(2U, held.KeyCount);
        Assert.Equal(held.Capacity, held.Entries.Count);
        Assert.Equal(65, held.Entries.Count(entry => !entry.IsEmpty));
        Assert.Equal(3007, held.Entries.Count(entry => entry.IsEmpty));
        Assert.Empty(held.Warnings);
        Assert.Equal(0x13A5E, storage.Range.Offset);
        Assert.Equal(storage.Capacity, storage.Entries.Count);
        Assert.Equal(2048, storage.Entries.Count);
        Assert.All(storage.Entries, entry => Assert.True(entry.IsEmpty));
        Assert.Empty(storage.Warnings);
        Assert.Equal(2, held.Entries.Count(entry => entry.ItemId == 0x006CF2A0));
        Assert.Contains(held.Entries, entry => entry.ItemId == 0x01CB2310);
        Assert.Equal(12U, Assert.Single(held.Entries, entry => entry.ItemId == 0x400050F0).Quantity);
        Assert.Equal(3U, Assert.Single(held.Entries, entry => entry.ItemId == 0x40002774).Quantity);
        Assert.Equal(1U, Assert.Single(held.Entries, entry => entry.ItemId == 0x4000251C).Quantity);
        Assert.Equal("key", Assert.Single(held.Entries, entry => entry.ItemId == 0x40002198).Group);
        Assert.All(semantic.Ranges, range => Assert.InRange(range.Offset + range.Length, slot.Offset, slot.Offset + SyntheticSave.SlotSize));

        var plugin = new EldenRingPlugin();
        var packs = new ReferencePackStore([plugin]);
        using var document = plugin.Open(file, packs.Pin(plugin, "PPSA03169"));
        _ = document.ExpandNode("characters/0");
        Assert.Equal(504U, document.GetValue("slots/0/vitals/hp/current").AsUInt32());
        Assert.Equal(504U, document.GetValue("slots/0/vitals/hp/effective-max").AsUInt32());
        Assert.Equal(476U, document.GetValue("slots/0/vitals/hp/base-max").AsUInt32());
        Assert.Equal(1U, document.GetValue("slots/0/class-id").AsUInt32());
        var page = ((IQueryableDocument)document).Query("slots/0/inventory/held", new() { Text = "Rowa Fruit" });
        var named = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["item"]);
        Assert.Equal("Rowa Fruit", named.Label);
        Assert.Contains("Source:", named.Description);
        Assert.Equal(Confidence.Inferred, named.Confidence);
        var queryable = (IQueryableDocument)document;
        var inventory = queryable.Query("slots/0/inventory/held", new());
        Assert.Equal(3072, inventory.TotalCount);
        Assert.Equal(65, inventory.FilteredCount);
        Assert.Equal(3007, inventory.HiddenEmptyCount);
        Assert.All(inventory.Rows, row => Assert.False(row.IsEmpty));
        var emptyStorage = queryable.Query("slots/0/inventory/storage", new());
        Assert.Equal(2048, emptyStorage.TotalCount);
        Assert.Equal(0, emptyStorage.FilteredCount);
        Assert.Equal(2048, emptyStorage.HiddenEmptyCount);
        Assert.Empty(emptyStorage.Rows);
        var vacant = queryable.Query("slots/0/inventory/held", new() { IncludeEmpty = true, Text = "Empty slot", Limit = 1 });
        Assert.Equal(3007, vacant.FilteredCount);
        Assert.Equal(0, vacant.HiddenEmptyCount);
        Assert.True(Assert.Single(vacant.Rows).IsEmpty);
        Assert.True(bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void SampleAcquisitionRowsAreSixteenBytesAndEventRangeMatchesIndependentProbe()
    {
        var bytes = WriteTestSupport.Sample().ReadAllBytes();
        var slot = EldenRingParser.Parse(bytes).Slots[0];
        var ranges = SemanticReader.Read(bytes, slot).Ranges;
        var acquired = ranges.Single(range => range.Name == SemanticLayout.AcquisitionRangeName);
        Assert.Equal(0x1AC53, acquired.Offset);
        Assert.Equal(73U, EldenRingParser.ReadUInt32(bytes, acquired.Offset));
        Assert.Equal(8 + 7000 * 16, acquired.Length);
        var flags = ranges.Single(range => range.Name == SemanticLayout.EventRangeName);
        Assert.Equal(0x36600, flags.Offset);
        Assert.Equal(0x1BF99F, flags.Length);
        Assert.True(ProgressViews.Read(bytes, flags, 0xCBE, 3)); // Source 76100, not a console discovery assertion.
        Assert.True(ProgressViews.Read(bytes, flags, 0xCBE, 2)); // Source 76101.
        Assert.False(ProgressViews.Read(bytes, flags, 0xCBE, 1)); // Source 76102.
    }

    [Fact]
    public void SampleRegulation_DecodesAndValidatesEveryParamDirectoryWithoutChangingAllocation()
    {
        var file = WriteTestSupport.Sample();
        var bytes = file.ReadAllBytes();
        var decoded = RegulationDecoder.Decode(bytes.AsSpan(0x1960070, 0x240010));
        var archive = RegulationArchiveReader.Read(decoded);
        Assert.Equal(37_745_424, decoded.Length);
        Assert.Equal("11001000", archive.BinderVersion);
        Assert.Equal(186, archive.Tables.Count);
        Assert.Contains(archive.Tables, table => table.Name == "EquipParamGoods.param" && table.Rows == 1808 && table.DataVersion == 3);
        Assert.Contains(archive.Tables, table => table.Name == "EquipParamWeapon.param" && table.Rows == 2973 && table.DataVersion == 6);
        using var document = new EldenRingPlugin().Open(file);
        Assert.Contains(document.ExpandNode("regulation/metadata"), node => node is QueryableTableNode table && table.TotalCount == 186);
        Assert.True(bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }
}
