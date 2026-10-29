using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartReferenceOnlyWriteTests
{
    [Fact]
    public void ReferenceReplacementWorksWhenAmountsHaveReadOnlyStorage()
    {
        var bytes = InventoryFixture.Create(amountType: "UInt32Property");
        var imported = SaveSetImporter.Import(
            [new ImportSource("PPSA10695/sdimg_authored/slot", () => new MemoryStream(bytes, writable: false))],
            Path.Combine(TestScratch.ProcessFolder, "atomic-asset-only"));
        var file = Assert.Single(imported.AllDiskFiles);
        var context = Assert.IsType<ReferenceContext>(WriteTestData.References());
        var document = WriteTestData.Plugin.Open(file, context);
        using var session = new DocumentSession("atomic-asset-only", file,
            WriteTestData.Plugin, document, references: context);
        _ = session.BuildView();
        var page = session.QueryCollection(InventoryCollection.Id, new() { IncludeEmpty = true, Limit = 200 });
        Assert.Equal(2, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.True(WriteTestData.Field(row, "amount").Spec.ReadOnly));
        var source = page.Rows.Single(row => WriteTestData.Field(row, "asset").Value.AsString() == "DA_Item_Shved");
        var field = WriteTestData.Field(source, "asset");
        Assert.False(field.Spec.ReadOnly);
        Assert.True(document.CanEdit);
        Assert.False(document.ApplyEdits([new(WriteTestData.Field(source, "amount").ValueId, FieldValue.Of(1))]).Succeeded);
        var plan = Assert.IsAssignableFrom<IActionPlanningDocument>(document).PlanAction(
            InventoryPlanning.ActionId, new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(1) }, new());
        Assert.Empty(plan.Changes);
        Assert.Equal(2, plan.SkippedRows.Count);
        Assert.Equal(bytes, WriteTestData.Output(document));

        var applied = session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement("DA_Item_ShotgunKS23"))], session.State.Revision);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(error => error.Message)));
        Assert.Contains(applied.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
        var edited = WriteTestData.Output(document);
        var before = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var after = SlotDataDecoder.Decode(edited, SaveExtensionFile.Parse(edited));
        var state = new InventoryState(before, context.Catalog("items"));
        var item = Assert.Single(state.Items, candidate => candidate.Key == source.Key);
        var savedState = new InventoryState(after, context.Catalog("items"));
        var saved = Assert.Single(savedState.Items, candidate => candidate.Key == source.Key);
        WritePreservation.AssertReferenceDiff(before, after, item, saved);
        Assert.Equal("UInt32Property", saved.Quantity.Type);
        Assert.Equal(item.Amount, saved.Amount);
        Assert.Equal(after.Plain, WriteTestData.IndependentDecode(bytes, edited));
        Assert.True(session.Undo().Ok);
        Assert.Equal(bytes, WriteTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, WriteTestData.Output(document));
    }

    [Fact]
    public void ReadOnlyAmountsWithoutAReferencePackKeepTheDocumentReadOnly()
    {
        var bytes = InventoryFixture.Create(amountType: "UInt32Property");
        using var document = WriteTestData.Plugin.Open(MemorySaveSet.SingleFile("slot", bytes, "PPSA10695"));
        Assert.False(document.CanEdit);
        Assert.All(WriteTestData.Table(document).Rows, row =>
        {
            Assert.True(WriteTestData.Field(row, "amount").Spec.ReadOnly);
            Assert.True(WriteTestData.Field(row, "asset").Spec.ReadOnly);
        });
        Assert.Equal(bytes, WriteTestData.Output(document));
    }
}
