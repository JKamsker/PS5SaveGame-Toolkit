using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class NewRecordTests
{
    [Fact]
    public void NewRepeatedValues_EmitBytesIncludingExplicitDefaults()
    {
        Assert.Equal(new byte[] { 10, 3, 1, 2, 3 }, new ProtoField(1, 2, new("bank", "bytes", true), [], new byte[] { 1, 2, 3 }, null).Encode());
        Assert.Equal(new byte[] { 8, 0 }, new ProtoField(1, 0, new("points", "uint32", true), [], 0UL, null).Encode());
        Assert.Equal(new byte[] { 10, 0 }, new ProtoField(1, 2, new("paths", "string", true), [], string.Empty, null).Encode());
        Assert.Empty(new ProtoField(1, 0, new("points", "uint32", false), [], 0UL, null).Encode());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void ProfileImport_PersistsOneNewBankRecordAndHostHistoryRestoresIt(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var file = set.AllDiskFiles.First(f => f.FileName == "profile" && !f.Directory.IsBackup);
        var characterFile = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var character = (GearboxDocument)plugin.Open(characterFile);
        var serial = character.Inventory.Entries.First(entry => character.Inventory.Info(entry.Id).Complete).Serial;
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("bank-import", file, plugin, document);
        _ = session.BuildView();
        var count = document.Inventory.Entries.Count(entry => entry.List == GearboxInventory.Bank);
        var preview = session.PlanAction("items/import", new Dictionary<string, JsonElement>
            { ["code"] = JsonSerializer.SerializeToElement("BL3(" + Convert.ToBase64String(serial) + ")") });
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var edited = document.Serialize().Single().Content.ToArray();
        using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, edited));
        Assert.Equal(count + 1, reopened.Inventory.Entries.Count(entry => entry.List == GearboxInventory.Bank));
        Assert.Equal(serial, reopened.Inventory.Entries.Last(entry => entry.List == GearboxInventory.Bank).Serial);
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, document.Serialize().Single().Content.ToArray());
    }
}
