using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class GoldDiscoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryGameplayCopyRetainsGoldOccurrencesWithoutInventingPartyOwnership(bool backup)
    {
        var file = Assert.Single(SampleSaves.Load("PPSA18463").AllDiskFiles,
            file => file.FileName == "AutoSave_0.lsv" && file.Directory.IsBackup == backup);
        var source = SampleLiveNewAgeTests.Source(file.ReadAllBytes());
        var candidates = Assert.IsType<LsmfInventoryGraph>(source.Inventory).Gold;
        Assert.Equal(4, candidates.Count);
        Assert.Equal([2L, 2L, 4L], candidates.Where(gold => gold.Stack is not null)
            .Select(gold => gold.Stack!.Original).Order());
        Assert.Equal(7, candidates.Select(gold => gold.EntityRows.Count).Distinct().Single());
        Assert.All(candidates, gold => Assert.Null(gold.OwnerBodyEntityRow));
        Assert.All(candidates, gold => Assert.NotNull(gold.ContainerRow));
        Assert.Single(candidates, gold => gold.Stack is null);

        using var document = new BaldursGate3Plugin().Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var table = queryable.Collections.Single(collection => collection.Id == source.Prefix + "/gold");
        var page = queryable.Query(table.Id, new() { Limit = 10 });
        Assert.Equal(4, page.Rows.Count);
        var quantities = page.Rows.Select(row => row.Cells["quantity"]).OfType<FieldNode>().ToArray();
        Assert.Equal(3, quantities.Length);
        Assert.All(quantities, field =>
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(1L))]).Succeeded);
        });
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SyntheticLsfJoinsRepeatedGuidOccurrencesByExplicitStackAndSlotReferences()
    {
        var (bytes, resource, source) = GoldDiscoveryFixture.Create();
        var gold = LsmfGoldDiscovery.Read(source.Access, resource, source.Inventory!.Entries, source.Stacks);
        Assert.Equal(2, gold.Count);
        Assert.Equal([5L, 4L], gold.Select(candidate => candidate.Stack!.Original));
        Assert.All(gold, candidate => Assert.False(candidate.IsAmbiguous));
        Assert.All(gold, candidate => Assert.Null(candidate.OwnerBodyEntityRow));
        Assert.Equal(bytes, LsfWriter.Write(bytes, new Dictionary<int, byte[]>()));
        Assert.Equal(bytes, LsfBinary.Read(bytes).Rebuild(new Dictionary<int, byte[]>()));
    }

    [Fact]
    public void MixedTemplateMeaningsAndConflictingCreatorMappingsAreNotGoldEvidence()
    {
        var (_, resource, source) = GoldDiscoveryFixture.Create();
        var statsIndex = resource.Attributes.ToList().FindIndex(attribute => attribute.Name == "Stats");
        var mixed = resource with { Attributes = resource.Attributes.Select((attribute, index) =>
            index == statsIndex ? attribute with { Data = "OBJ_Potion\0"u8.ToArray() } : attribute).ToArray() };
        Assert.Empty(LsmfGoldDiscovery.Read(source.Access, mixed, source.Inventory!.Entries, source.Stacks));
        var duplicateSlots = source.Inventory.Entries.Concat(source.Inventory.Entries).ToArray();
        Assert.All(LsmfGoldDiscovery.Read(source.Access, resource, duplicateSlots, source.Stacks),
            candidate => Assert.True(candidate.IsAmbiguous));
        var duplicateStacks = source.Stacks.Concat(source.Stacks).ToArray();
        Assert.All(LsmfGoldDiscovery.Read(source.Access, resource, source.Inventory.Entries, duplicateStacks),
            candidate => Assert.Null(candidate.Stack));

        var conflicting = new ResourceNode("Creator", 4);
        conflicting.Attributes.Add(resource.Attributes.Count);
        conflicting.Attributes.Add(resource.Attributes.Count + 1);
        var entity = resource.Attributes.First(attribute => attribute.Name == "Entity");
        var conflict = resource with
        {
            Nodes = [.. resource.Nodes, conflicting],
            Attributes = [.. resource.Attributes, entity, new ResourceAttribute("TemplateID", LarianAttributeType.UUID, new byte[16])],
        };
        Assert.Single(LsmfGoldDiscovery.Read(source.Access, conflict, source.Inventory.Entries, source.Stacks));
    }

    [Fact]
    public void MissingContributionsDoNotBecomeSingleCoinsAndDuplicateReferencesStayUnknown()
    {
        var (_, resource, source) = GoldDiscoveryFixture.Create();
        var missing = LsmfGoldDiscovery.Read(source.Access, resource, source.Inventory!.Entries, []);
        Assert.Equal(2, missing.Count);
        Assert.All(missing, candidate =>
        {
            Assert.Null(candidate.Stack);
            Assert.True(candidate.IsAmbiguous);
        });
        var duplicateSlots = source.Inventory.Entries.Concat(source.Inventory.Entries).ToArray();
        var ambiguous = LsmfGoldDiscovery.Read(source.Access, resource, duplicateSlots, source.Stacks);
        Assert.Equal(2, ambiguous.Count);
        Assert.All(ambiguous, candidate =>
        {
            Assert.Null(candidate.ContainerRow);
            Assert.Null(candidate.OwnerBodyEntityRow);
            Assert.True(candidate.IsAmbiguous);
        });
    }
}
