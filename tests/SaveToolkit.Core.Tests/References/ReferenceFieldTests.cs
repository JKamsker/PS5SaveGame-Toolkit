using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceFieldTests
{
    private static ReferenceFieldService Service()
    {
        var plugin = new ItemsPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, ItemsPlugin.TitleId);
        _ = context.Catalog("items", "1.01");
        return new ReferenceFieldService(context);
    }

    [Theory]
    [InlineData("1001", false, true)]
    [InlineData("missing", false, false)]
    [InlineData("missing", true, true)]
    [InlineData("01001", false, false)]
    [InlineData("1001 ", false, false)]
    public void Coercer_UsesExactCatalogIds(string rawId, bool custom, bool accepted)
    {
        var result = FieldValueCoercer.FromJson(JsonSerializer.SerializeToElement(rawId),
            new ReferenceSpec("items") { AllowCustom = custom }, Service());

        Assert.Equal(accepted, result.Ok);
        if (accepted)
        {
            Assert.Equal(rawId, result.Value.AsString());
        }
    }

    [Fact]
    public void Coercer_RejectsWrongTypes_ReadOnly_AndMissingCatalog()
    {
        var spec = new ReferenceSpec("items");
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of(1001L), spec, references: Service()).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("1001"), spec with { ReadOnly = true }, references: Service()).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("1001"), spec).Ok);
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of("custom"), spec with { AllowCustom = true }).Ok);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Policy_IsEnforcedForCandidatesAndEdits_EvenWithCustomAllowed(bool available, bool eligible)
    {
        var spec = new ReferenceSpec("items")
        {
            AllowCustom = true,
            Assess = _ => new ReferenceEligibility { Available = available, Eligible = eligible, Reasons = ["Requires a quest."] },
        };
        var service = Service();

        Assert.False(service.Find(spec, "1001").Eligibility.Selectable);
        var result = FieldValueCoercer.Normalize(FieldValue.Of("1001"), spec, references: service);
        Assert.False(result.Ok);
        Assert.Equal("Requires a quest.", result.Error);
    }

    [Fact]
    public void Paging_Search_Category_AndProvenance_AreResolvedFromF2()
    {
        var service = Service();
        var page = service.Search(new ReferenceSpec("items"), new ReferenceQuery { Category = "consumable", Offset = 1, Limit = 1 }, 7);
        Assert.Equal(2, page.Total);
        var candidate = Assert.Single(page.Items);
        Assert.Equal("1002", candidate.RawId);
        Assert.Equal("Ether", candidate.Label);
        Assert.Equal("Restores a little mana.", candidate.Description);
        Assert.Equal("test.items.en:icons/ether.png", candidate.AssetId);
        Assert.Equal("PS5 Save Game Toolkit contributors", candidate.Dataset!.Attribution.Author);
        Assert.Equal("icons", candidate.AssetDataset!.Id);
        Assert.Equal(7, page.Revision);
        Assert.Equal("1001", Assert.Single(service.Search(new ReferenceSpec("items"), new ReferenceQuery { Text = "potion" }, 0).Items).RawId);
        var capped = service.Search(new ReferenceSpec("items"), new ReferenceQuery { Limit = int.MaxValue, Offset = -1 }, 0);
        Assert.Equal(ReferenceQuery.MaxLimit, capped.Limit);
        Assert.Equal(0, capped.Offset);
        Assert.Empty(service.Search(new ReferenceSpec("items"), new ReferenceQuery { Offset = int.MaxValue }, 0).Items);
    }

    [Fact]
    public void SpecAndUnknownValue_RoundTripThroughViewProtocol()
    {
        ViewNode view = new FieldNode("item", "Item", new ReferenceSpec("items") { AllowCustom = true }, FieldValue.Of("18446744073709551615"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(view, options);
        var restored = Assert.IsType<FieldNode>(JsonSerializer.Deserialize<ViewNode>(json, options));
        var spec = Assert.IsType<ReferenceSpec>(restored.Spec);
        Assert.Equal("items", spec.CatalogId);
        Assert.True(spec.AllowCustom);
        Assert.Equal("18446744073709551615", restored.Value.AsString());
        Assert.Contains("\"kind\":\"reference\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCurrentValue_IsPreserved_Replaceable_AndRestoredByUndo()
    {
        var plugin = new ItemsPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var bytes = ItemsPlugin.Create(1, (9999, 3));
        var zip = TestZips.Build(($"{ItemsPlugin.TitleId}/sdimg_SAVE/items.dat", bytes));
        var ws = store.Create("items", [new ImportSource("items.zip", () => new MemoryStream(zip))]);
        var session = ws.Open(ws.Set.AllDiskFiles.Single().Id, null);
        var view = (SectionNode)session.BuildView();
        var field = (FieldNode)((TableNode)view.Children[0]).Rows[0].Cells["item"];
        Assert.Equal("9999", field.Value.AsString());
        Assert.Equal("9999", session.FindReference(field.ValueId, "9999").RawId);
        Assert.Null(session.FindReference(field.ValueId, "9999").Label);
        Assert.Empty(session.Serialize());

        var invalid = session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement("missing"))]);
        Assert.False(invalid.Ok);
        Assert.Empty(session.Serialize());
        var ineligible = session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement("2001"))]);
        Assert.False(ineligible.Ok);
        Assert.Contains("consumables", Assert.Single(ineligible.Errors).Message, StringComparison.Ordinal);
        var accepted = session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement("1001"))]);
        Assert.True(accepted.Ok);
        Assert.Equal("9999", Assert.Single(accepted.Changes).OldValue.AsString());
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.Serialize());
        Assert.True(session.Redo().Ok);
        Assert.Equal(ItemsPlugin.Create(1, (1001, 3)), Assert.Single(session.Serialize()).Content.ToArray());
        Assert.Throws<StaleViewException>(() => session.SearchReferences(field.ValueId, new ReferenceQuery(), revision: 99));
        Assert.Throws<KeyNotFoundException>(() => session.FindReference("unpublished", "1001"));
    }
}
