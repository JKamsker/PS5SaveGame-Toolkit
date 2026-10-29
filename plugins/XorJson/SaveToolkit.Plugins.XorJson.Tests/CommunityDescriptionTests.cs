using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class CommunityDescriptionTests
{
    private const string CatalogId = "xorjson-effects";
    private const string OwnerDecision = "Rights reserved by Mojang Studios/Microsoft; used with attribution per owner decision 2026-10-29";
    private const string SnapshotHash = "fce93f92b4d8fcaf99b9d95069a0de34d7dc0694d81009aea44a61e232f580b6";

    [Fact]
    public void EveryEffectRetainsPinnedAdvisoryDescriptionsWithoutWriteLimits()
    {
        var plugin = new XorJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors); // Includes normalized resource hash validation.
        var references = store.Pin(plugin, "PPSA16064");
        var catalog = references.Catalog(CatalogId)!;
        var records = catalog.Search(new ReferenceQuery { Limit = 100 }).Items;
        Assert.Equal(60, records.Count);
        Assert.All(records, record =>
        {
            Assert.StartsWith("SW.Effect.", record.Key);
            Assert.Contains("PS5 behavior and serialized strengths are unverified", record.Description!);
            Assert.Contains("Reference only, not edit limits", record.Description!);
            Assert.Equal(OwnerDecision, record.Dataset.Attribution.License);
            Assert.Contains(SnapshotHash, record.Attributes["descriptionRevision"]);
            Assert.Contains("1.1.2.0 / Steam 25754144", record.Attributes["descriptionRevision"]);
            Assert.Contains("84ffee67b73e49d5b888e31257ce9767aa8ab826", record.Attributes["identifierSource"]);
            Assert.Contains("Exact unique English name match", record.Attributes["crosswalk"]);
            Assert.Equal(record.Source, record.Attributes["descriptionSource"]);
            Assert.StartsWith("https://www.dungeons.tools/2/effects/", record.Source!);
            Assert.Contains("dungeons.tools", record.AttributionText);
            Assert.Contains("Ishiaki", record.AttributionText);
            Assert.NotNull(record.AssetId);
            Assert.Contains("Game description (PC tier I):", record.Description!);
            Assert.Null(record.Limit);
            Assert.DoesNotContain("strength", record.Attributes.Keys);
            Assert.DoesNotContain("text", record.Attributes.Keys);
        });
        var bowyer = catalog.Find("SW.Effect.ArrowBurst")!;
        Assert.Equal("24", bowyer.Attributes["compatibleGearCount"]);
        Assert.Equal("Tier I: 20%; Tier II: 25%; Tier III: 30%", bowyer.Attributes["displayPercentages"]);
        Assert.StartsWith("Ammo recovery can yield an extra arrow.", bowyer.Description!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PickerAndFieldShowAttributedDescriptionsWhileTokenWritesRemainLossless(bool xor)
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        const string original = "{\"effect\":\"SW.Effect.ArrowBurst\",\"unknown\":-0.00E+0}";
        var bytes = XorJsonTests.Encode(original, xor);
        using var document = plugin.Open(MemorySaveSet.SingleFile("effects.sav", bytes, "PPSA16064"), references);
        var field = XorJsonTests.Fields(document).Single(value => value.Label == "effect");
        var spec = Assert.IsType<ReferenceSpec>(field.Spec);
        Assert.Equal(CatalogId, spec.CatalogId);
        Assert.Contains("Ammo recovery can yield an extra arrow.", field.Description!);
        Assert.Contains(OwnerDecision, field.Description!);
        var service = new ReferenceFieldService(references);
        var candidate = Assert.Single(service.Search(spec, new ReferenceQuery { Text = "Bowyer" }, revision: 0).Items);
        Assert.Contains("Tier III: 30%", candidate.Description!);
        Assert.Equal(OwnerDecision, candidate.Dataset!.Attribution.License);
        Assert.False(service.Validate(FieldValue.Of("SW.Effect.UnknownFuture"), spec).Ok);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());

        var edit = document.ApplyEdits([new(field.ValueId, FieldValue.Of("SW.Effect.Constitution"))]);
        Assert.True(edit.Succeeded);
        Assert.NotEmpty(edit.Warnings);
        var changed = document.Serialize()[0].Content.ToArray();
        Assert.Equal(XorJsonTests.Encode(original.Replace("SW.Effect.ArrowBurst", "SW.Effect.Constitution", StringComparison.Ordinal), xor), changed);
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("effects.sav", changed, "PPSA16064"), references);
        Assert.Contains("Bonus to incoming healing.", XorJsonTests.Fields(reopened).Single(value => value.Label == "effect").Description!);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingReferencesAndUnknownEffectsNeverInventDescriptions(bool withReferences)
    {
        var plugin = new XorJsonPlugin();
        var id = withReferences ? "SW.Effect.UnknownFuture" : "SW.Effect.ArrowBurst";
        var bytes = XorJsonTests.Encode("{\"effect\":\"" + id + "\"}");
        var file = MemorySaveSet.SingleFile("effects.sav", bytes, "PPSA16064");
        using var document = withReferences
            ? plugin.Open(file, new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064"))
            : plugin.Open(file);
        var field = Assert.Single(XorJsonTests.Fields(document));
        Assert.Equal(id, field.Value.AsString());
        Assert.DoesNotContain("dungeons.tools", field.Description!);
        Assert.DoesNotContain("Tier I:", field.Description!);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void Ps5SampleHasNoPopulatedEffectsToValidateCommunityTierSemantics()
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Single(file =>
            !file.IsSystemFile && file.Directory.Name.StartsWith("Character", StringComparison.Ordinal));
        var bytes = file.ReadAllBytes();
        using var json = JsonDocument.Parse(bytes);
        var entries = json.RootElement.GetProperty("CharacterSaveV1").GetProperty("Inventory").GetProperty("Entries");
        Assert.Equal(5, entries.GetArrayLength());
        foreach (var entry in entries.EnumerateArray())
            Assert.Equal(0, entry.GetProperty("ItemData").GetProperty("Effects").GetArrayLength());
        using var document = plugin.Open(file, references);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        Assert.DoesNotContain(XorJsonTests.Fields(document), field => field.Spec is ReferenceSpec { CatalogId: CatalogId });
    }
}
