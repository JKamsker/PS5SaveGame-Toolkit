using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithContextTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Fact]
    public void ArraysAreSeparateExperimentalAndPreserveUnknownEquipment()
    {
        var equipment = new byte[FaithSchema.EquipmentLength];
        BinaryPrimitives.WriteInt32LittleEndian(equipment, 100029);
        BinaryPrimitives.WriteInt32LittleEndian(equipment.AsSpan(24), 900000);
        BinaryPrimitives.WriteInt32LittleEndian(equipment.AsSpan(48), 100201);
        var skills = new byte[FaithSchema.SkillLength];
        skills[0] = 3;
        skills[68] = 0x81;
        skills[80] = 0x42;
        skills[91] = 0xFF; // Include the flags beyond the 68 possible rank bytes.
        var bytes = FaithTestData.Container((FaithSchema.EquipmentType, FaithSchema.EquipmentVersion, equipment),
            (FaithSchema.SkillType, FaithSchema.SkillVersion, skills));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var fields = FaithViewFields.All(document.BuildView()).ToDictionary(field => field.ValueId);
        Assert.Equal("100029", fields["equipment/0/0/item"].Value.AsString());
        Assert.Equal("900000", fields["equipment/1/0/item"].Value.AsString());
        Assert.Equal("100201", fields["equipment/2/0/item"].Value.AsString());
        Assert.Equal("Unknown item 900000", fields["equipment/1/0/item"].Label);
        Assert.Equal(Confidence.Unknown, fields["equipment/1/0/item"].Confidence);
        Assert.Equal(68, fields.Keys.Count(key => key.StartsWith("skills/ranks/", StringComparison.Ordinal)));
        Assert.Equal(12, fields.Keys.Count(key => key.StartsWith("skills/new/", StringComparison.Ordinal)));
        Assert.Equal(12, fields.Keys.Count(key => key.StartsWith("skills/protect/", StringComparison.Ordinal)));
        Assert.Equal(3, document.GetValue("skills/ranks/0").AsInt32());
        Assert.Equal(0x81, document.GetValue("skills/new/0").AsInt32());
        Assert.Equal(0x42, document.GetValue("skills/protect/0").AsInt32());
        Assert.Equal(0xFF, document.GetValue("skills/protect/11").AsInt32());
        Assert.All(fields.Values.Where(field => !field.Spec.ReadOnly), field => Assert.Contains("Not validated on console", field.Warning));
        Assert.True(document.ApplyEdits([new ValueEdit("skills/ranks/0", FieldValue.Of(1))]).Succeeded);
        Assert.True(document.ApplyEdits([new ValueEdit("skills/ranks/0", FieldValue.Of(3))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(2, 2021112401UL, 168, "party/map")]
    [InlineData(13, 20220104UL, 72, "equipment/0/0/item")]
    [InlineData(27, 20210422UL, 96, "skills/ranks/0")]
    public void UnsupportedOrAmbiguousContextStaysOpaque(uint type, ulong version, int length, string valueId)
    {
        var payload = new byte[length];
        byte[][] cases = [
            FaithTestData.Container((type, version + 1, payload)),
            FaithTestData.Container((type, version, new byte[length - 8])),
            FaithTestData.Container((type, version, payload), (type, version, payload)),
            FaithTestData.Container((type, version, payload), (type, version + 1, payload)),
        ];
        foreach (var bytes in cases)
        {
            using var document = _plugin.Open(FaithTestData.File(bytes));
            _ = document.BuildView();
            Assert.Throws<KeyNotFoundException>(() => document.GetValue(valueId));
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("PPSA10665", 59, 172, 172)]
    [InlineData("PPSA13803", 11, 2, 2)]
    public void EveryGameplayOccurrenceHasGatedContextAndExplicitInventoryFallback(string title, int expected, int distinctIds, int namedIds)
    {
        var store = new ReferencePackStore([_plugin]);
        var catalog = new FaithCatalog(store.Pin(_plugin, title));
        var ids = new HashSet<int>();
        var count = 0;
        foreach (var file in FaithSamples.Records(title).Where(file => file.FileName.StartsWith("game-", StringComparison.Ordinal)))
        {
            var bytes = file.ReadAllBytes();
            var container = FaithContainer.Parse(bytes);
            Assert.NotNull(FaithSchema.Unique(container, FaithSchema.EquipmentType, FaithSchema.SupportsEquipment));
            Assert.NotNull(FaithSchema.Unique(container, FaithSchema.PartyType, FaithSchema.SupportsParty));
            Assert.NotNull(FaithSchema.Unique(container, FaithSchema.SkillType, FaithSchema.SupportsSkills));
            using var document = _plugin.Open(file, store.Pin(_plugin, title));
            var fields = FaithViewFields.All(document.BuildView()).GroupBy(field => field.ValueId).Select(group => group.First()).ToArray();
            Assert.All(fields.Where(field => !field.Spec.ReadOnly && !field.ValueId.EndsWith("/quantity", StringComparison.Ordinal)),
                field => Assert.Contains("Not validated on console", field.Description));
            foreach (var item in FaithInventory.Parse(bytes, container).Items)
            {
                ids.Add(item.Id);
                Assert.True(catalog.Item(item.Id) is not null || catalog.Name(item.Id) == $"Unknown item {item.Id}");
            }
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
            count++;
        }
        Assert.Equal(expected, count);
        Assert.Equal(distinctIds, ids.Count);
        Assert.Equal(namedIds, ids.Count(id => catalog.Item(id) is not null));
    }

    [Theory]
    [InlineData("PPSA10665", 602000, 100029)]
    [InlineData("PPSA13803", 392000, 100001)]
    public void KnownContextValuesMatchFirstAutosave(string title, int mapId, int equipmentId)
    {
        using var document = _plugin.Open(FaithSamples.Snapshot(title));
        _ = document.BuildView();
        Assert.Equal(FaithCatalog.Key(mapId), document.GetValue("party/map").AsString());
        Assert.Equal(FaithCatalog.Key(equipmentId), document.GetValue("equipment/0/0/item").AsString());
    }

    [Fact]
    public void CatalogPickerLookupCannotChangeSaveBytes()
    {
        var file = FaithSamples.Snapshot("PPSA10665");
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        var result = document.InvokeAction(FaithCatalog.LookupActionId,
            new Dictionary<string, FieldValue> { [FaithCatalog.LookupParameter] = FieldValue.Of("100029") });
        Assert.Contains("Defender", result.Message);
        Assert.Contains("CC-BY-SA", result.Message);
        Assert.Empty(result.Changes);
        Assert.True(result.Undoable);
        Assert.Throws<ArgumentException>(() => document.InvokeAction(FaithCatalog.LookupActionId,
            new Dictionary<string, FieldValue> { [FaithCatalog.LookupParameter] = FieldValue.Of("900000") }));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CatalogLookupIsAnF13InspectionWithUnchangedSessionState()
    {
        var file = FaithSamples.Snapshot("PPSA10665");
        using var fixture = new FaithSessionFile(file.ReadAllBytes());
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        using var document = _plugin.Open(fixture.File, references);
        using var session = new DocumentSession("ffxvi-inspect", fixture.File, _plugin, document, references: references);
        _ = session.BuildView();
        var revision = session.State.Revision;
        var result = session.InvokeAction(FaithCatalog.LookupActionId,
            new Dictionary<string, JsonElement>
            {
                [FaithCatalog.LookupParameter] = JsonSerializer.SerializeToElement("100029"),
            }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("Defender", result.Message);
        Assert.Equal(revision, result.State.Revision);
        Assert.False(result.State.CanUndo);
        Assert.Equal(fixture.File.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA10665", "Hideaway 2")]
    [InlineData("PPSA13803", "Phoenix Gate")]
    public void LocationAndSummonLabelsUseLicensedNexContext(string title, string location)
    {
        var file = FaithSamples.Snapshot(title);
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        using var document = _plugin.Open(file, references);
        var fields = FaithViewFields.All(document.BuildView()).ToArray();
        var map = Assert.Single(fields, field => field.ValueId == "party/map");
        Assert.Contains(location, map.Label);
        Assert.Contains("MIT", map.Description);
        Assert.Contains("d3ad062", map.Description);
        Assert.Equal(Confidence.Inferred, map.Confidence);
        Assert.False(map.Spec.ReadOnly);
        Assert.Equal("Phoenix", references.Catalog("ffxvi_summons")?.Find("1")?.Name);
        Assert.Contains("15020", references.Catalog("ffxvi_maps")?.Find("602000")?.Attributes["variantNote"]);
    }

    [Fact]
    public void SecondaryBankPreservesZeroIdentityButEnablesItsFixedWidthQuantity()
    {
        const int SecondaryOffset = 33624;
        const int SecondaryCapacity = 100;
        var payload = FaithTestData.Items();
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(SecondaryOffset + 4), 123);
        var metadata = SecondaryOffset + SecondaryCapacity * 20;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(metadata), 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadata + 8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadata + 16 + SecondaryCapacity * 8), 1);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var fields = FaithViewFields.All(document.BuildView()).Where(field => field.ValueId.StartsWith("secondary/", StringComparison.Ordinal)).ToArray();
        var item = Assert.Single(fields, field => field.ValueId.EndsWith("/item", StringComparison.Ordinal));
        var quantity = Assert.Single(fields, field => field.ValueId.EndsWith("/quantity", StringComparison.Ordinal));
        Assert.False(quantity.Spec.ReadOnly);
        Assert.Equal(Confidence.Unknown, quantity.Confidence);
        Assert.True(document.ApplyEdits([new ValueEdit(quantity.ValueId, FieldValue.Of(124))]).Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        FaithWriteTests.AssertOnlyRanges(bytes, output, [(FaithContainer.HeaderSize + FaithBlock.HeaderSize + SecondaryOffset + sizeof(int), sizeof(int))]);
        using var reopened = _plugin.Open(FaithTestData.File(output));
        _ = reopened.BuildView();
        Assert.Equal(124, reopened.GetValue(quantity.ValueId).AsInt32());
        Assert.Equal("0", reopened.GetValue(item.ValueId).AsString());
        Assert.True(document.ApplyEdits([new ValueEdit(quantity.ValueId, FieldValue.Of(123))]).Succeeded);
        Assert.Equal("0", item.Value.AsString());
        Assert.True(item.Spec.ReadOnly);
        Assert.Equal("Unknown item 0", item.Label);
        Assert.False(document.ApplyEdits([new ValueEdit(item.ValueId, FieldValue.Of("1"))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    public void DuplicateItemTypesDisableWritesEvenWhenOnlyOneVersionIsKnown(ulong versionDelta)
    {
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()),
            (FaithSchema.ItemType, FaithSchema.ItemVersion + versionDelta, FaithTestData.Items()));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        Assert.False(document.CanEdit);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("inventory/0/quantity"));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
