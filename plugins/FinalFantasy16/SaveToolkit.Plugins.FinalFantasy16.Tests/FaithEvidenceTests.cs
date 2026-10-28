using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithEvidenceTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData("ffxvi_items", 234)]
    [InlineData("ffxvi_maps", 107)]
    [InlineData("ffxvi_summons", 10)]
    [InlineData("ffxvi_skills", 3)]
    [InlineData("ffxvi_commands", 7)]
    [InlineData("ffxvi_feats", 8)]
    [InlineData("ffxvi_abilities", 48)]
    public void EmbeddedFallbackMatchesEveryPinnedRecordIncludingEvidence(string id, int count)
    {
        var store = new ReferencePackStore([_plugin]);
        Assert.Empty(store.LoadErrors);
        var host = Assert.IsAssignableFrom<IReferenceCatalog>(store.Pin(_plugin, "PPSA10665").Catalog(id));
        Assert.Equal(count, host.Count);
        for (var offset = 0; offset < count; offset += 200)
        {
            foreach (var record in host.Search(new ReferenceQuery { Offset = offset, Limit = 200 }).Items)
            {
                var fallback = Assert.IsType<ReferenceRecord>(FaithEmbeddedCatalogs.Find(id, record.Key));
                Assert.Equal(record.Name, fallback.Name);
                Assert.Equal(record.Category, fallback.Category);
                Assert.Equal(record.Source, fallback.Source);
                Assert.Equal(record.Description, fallback.Description);
                Assert.Equal(record.AttributionText, fallback.AttributionText);
                Assert.Equal(record.Attributes.OrderBy(pair => pair.Key), fallback.Attributes.OrderBy(pair => pair.Key));
            }
        }
    }

    [Fact]
    public void PluginCatalogueUsesDeclaredCategoryAndKeyOrdering()
    {
        var context = new SaveToolkit.Core.References.ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        var catalog = new FaithCatalog(context);
        Assert.Equal(["39", "45", "49"], catalog.SkillReferences().Select(record => record.Key));
        var abilities = catalog.Abilities();
        Assert.Equal(abilities.OrderBy(record => record.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Key, StringComparer.Ordinal).Select(record => record.Key), abilities.Select(record => record.Key));
    }

    [Fact]
    public void NamesCannotTurnNexSkillIdsIntoSavedRanks()
    {
        var skills = new byte[FaithSchema.SkillLength];
        skills[39] = 2;
        var bytes = FaithTestData.Container((FaithSchema.SkillType, FaithSchema.SkillVersion, skills));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var fields = FaithViewFields.All(document.BuildView()).ToDictionary(field => field.ValueId);
        var reference = fields["skills/references/39"];
        Assert.Equal("Wings of Light", reference.Label);
        Assert.Equal("39", reference.Value.AsString());
        Assert.Contains("MIT", reference.Description);
        Assert.Contains("No association", reference.Description);
        Assert.Equal(Confidence.Inferred, reference.Confidence);
        Assert.Equal("Byte 39", fields["skills/ranks/39"].Label);
        Assert.Equal(Confidence.Unknown, fields["skills/ranks/39"].Confidence);
        Assert.Equal(2, fields["skills/ranks/39"].Value.AsInt32());
        Assert.True(reference.Spec.ReadOnly);
        Assert.False(fields["skills/ranks/39"].Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new ValueEdit(reference.ValueId, FieldValue.Of("45"))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(1, "Phoenix", "Phoenix Shift")]
    [InlineData(7, "Odin", "Arm of Darkness")]
    [InlineData(8, "Bahamut", null)] // Commented-out mod code is not a vanilla mapping.
    [InlineData(999, null, null)]
    public void FeatContextKeepsReferenceRelationshipsSeparateFromStoredCommands(int summon, string? eikon, string? feat)
    {
        var player = new byte[FaithSchema.LegacyPlayerLength];
        BinaryPrimitives.WriteInt32LittleEndian(player.AsSpan(40), summon);
        BinaryPrimitives.WriteInt32LittleEndian(player.AsSpan(52), 201);
        var bytes = FaithTestData.Container((FaithSchema.PlayerType, FaithSchema.PlayerVersion, player));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var fields = FaithViewFields.All(document.BuildView()).DistinctBy(field => field.ValueId).ToDictionary(field => field.ValueId);
        Assert.Contains(eikon ?? "Summon slot", fields["context/summons/0"].Label);
        var reference = fields["context/feats/0"];
        Assert.Contains(feat ?? "Reference feat", reference.Label);
        Assert.Equal(FaithCatalog.Key(summon), reference.Value.AsString()); // Still a summon ID, not a command.
        Assert.Equal(feat is null ? Confidence.Unknown : Confidence.Inferred, reference.Confidence);
        Assert.Contains("Phoenix Shift", fields["context/commands/0"].Label);
        Assert.Equal("201", fields["context/commands/0"].Value.AsString());
        Assert.True(reference.Spec.ReadOnly);
        Assert.False(fields["context/commands/0"].Spec.ReadOnly);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void GrowthBranchesAreStoredPairsWithoutComputedTargets()
    {
        var player = new byte[FaithSchema.LegacyPlayerLength];
        int[] values = [42, 13891, 10, 123];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(player.AsSpan(i * sizeof(int)), values[i]);
        var bytes = FaithTestData.Container((FaithSchema.PlayerType, FaithSchema.PlayerVersion, player));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var fields = FaithViewFields.All(document.BuildView()).Where(field => field.ValueId.StartsWith("player/", StringComparison.Ordinal))
            .DistinctBy(field => field.ValueId).ToArray();
        Assert.Equal(values, fields.Select(field => field.Value.AsInt32()));
        Assert.All(fields, field => Assert.False(field.Spec.ReadOnly));
        Assert.False(document.ApplyEdits([new ValueEdit("player/level", FieldValue.Of(43))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UpgradeDiagnosticsDoNotCalculateCapsAndTrackManualEdits()
    {
        var items = FaithTestData.Items();
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(16), 506);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, items));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var view = document.BuildView();
        var fields = FaithViewFields.All(view).GroupBy(field => field.ValueId).ToDictionary(group => group.Key, group => group.First());
        Assert.Equal("Morganbeard Extract", fields["capacity/506/item"].Label);
        Assert.Contains("UseItem.layout", fields["capacity/506/item"].Description);
        Assert.True(fields["capacity/506/main/1"].Spec.ReadOnly);
        Assert.Equal(567, document.GetValue("capacity/506/main/1").AsInt32());
        var quantity = fields["inventory/1/quantity"];
        Assert.Equal(int.MaxValue, Assert.IsType<IntegerSpec>(quantity.Spec).Max);
        Assert.Contains("Legacy experimental policy", quantity.Warning);
        Assert.Contains("No dependent flags", quantity.Warning);
        Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        Assert.Contains(Actions(view), action => action.ActionId == FaithQuantityPlanning.ActionId && action.SupportsPlanning);
        Assert.Contains(Actions(view), action => action.ActionId == FaithCatalog.LookupActionId && !action.Mutates);
        Assert.False(document.ApplyEdits([new ValueEdit("capacity/506/main/1", FieldValue.Of(0))]).Succeeded);
        var outcome = document.ApplyEdits([new ValueEdit(quantity.ValueId, FieldValue.Of(0))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(0, document.GetValue("capacity/506/main/1").AsInt32());
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        const int UpgradeStackOffset = 52; // Header + block header + second 16-byte record + quantity word.
        for (var offset = 0; offset < bytes.Length; offset++)
            if (offset is not (>= 4 and < 8) && (offset < UpgradeStackOffset || offset >= UpgradeStackOffset + sizeof(int)))
                Assert.Equal(bytes[offset], output[offset]);
        Assert.True(document.ApplyEdits([new ValueEdit(quantity.ValueId, FieldValue.Of(567))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static IEnumerable<ActionNode> Actions(ViewNode node) => node switch
    {
        ActionNode action => [action],
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(Actions),
        SectionNode section => section.Children.SelectMany(Actions),
        TableNode table => table.Rows.SelectMany(row => row.Cells.Values).SelectMany(Actions),
        _ => [],
    };
}
