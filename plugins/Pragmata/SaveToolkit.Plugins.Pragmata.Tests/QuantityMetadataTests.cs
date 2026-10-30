using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class QuantityMetadataTests
{
    [Fact]
    public void ResolverPublishesOnlyRequestedUniqueQuantitiesWithoutRenderingOrChangingBytes()
    {
        var original = SyntheticInventory.Create(251);
        using var document = new PragmataDocument("data000.bin", original);
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var quantities = ImportedQuantityTests.Quantities(document);
        Assert.All(quantities, node => Assert.False(document.HasPrimaryBinding(node)));
        var ids = quantities.Take(199).Select(node => node.Path).Append("unknown").ToArray();
        var metadata = resolver.ResolveFieldMetadata(ids);
        Assert.Equal(ids.Take(199), metadata.Select(field => field.ValueId));
        Assert.All(metadata, field =>
        {
            Assert.Equal(FieldBindingKind.Primary, field.BindingKind);
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(IntegerSpec.Int32, field.Spec);
            Assert.False(string.IsNullOrEmpty(field.Label));
            _ = document.GetValue(field.ValueId);
        });
        Assert.All(quantities.Take(199), node => Assert.True(document.HasPrimaryBinding(node)));
        Assert.All(quantities.Skip(199), node => Assert.False(document.HasPrimaryBinding(node)));
        Assert.Single(resolver.ResolveFieldMetadata([quantities[0].Path, quantities[0].Path, "unknown"]));
        Assert.Empty(resolver.ResolveFieldMetadata([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.ResolveFieldMetadata(quantities.Take(201).Select(node => node.Path).ToArray()));
        Assert.Empty(resolver.ResolveFieldMetadata([document.Layout.Items[0].Find(KnownHashes.ItemId)!.Path]));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void RawAndInspectLegacyIdsAreReadOnlyDerivedBindingsOverTheCanonicalQuantity()
    {
        var original = SyntheticInventory.Create(251);
        using var document = new PragmataDocument("data000.bin", original);
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var quantity = ImportedQuantityTests.Quantities(document)[^1];
        string[] ids = [quantity.Path, $"raw/{quantity.Path}", $"inspect/value/{quantity.Path}"];
        var metadata = resolver.ResolveFieldMetadata(ids);
        Assert.Equal(ids, metadata.Select(field => field.ValueId));
        Assert.Equal(FieldBindingKind.Primary, metadata[0].BindingKind);
        Assert.Equal(quantity.Path, metadata[0].CanonicalValueId);
        Assert.All(metadata.Skip(1), field =>
        {
            Assert.Equal(FieldBindingKind.Derived, field.BindingKind);
            Assert.Equal(quantity.Path, field.CanonicalValueId);
            Assert.Equal(metadata[0].Spec with { ReadOnly = true }, field.Spec);
            Assert.Equal(document.Read(quantity), document.GetValue(field.ValueId));
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(17))]).Succeeded);
        });
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        var changed = document.ApplyEdits([new(quantity.Path, FieldValue.Of(17))]);
        Assert.True(changed.Succeeded);
        Assert.Equal(ids.Order(StringComparer.Ordinal), changed.Changes.Select(change => change.ValueId).Order(StringComparer.Ordinal));
        Assert.All(ids, id => Assert.Equal(FieldValue.Of(17), document.GetValue(id)));
        Assert.True(document.ApplyEdits([new(quantity.Path, changed.Changes[0].OldValue)]).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void EveryQuantityProjectionReusesTheCanonicalIdAndExactMetadataSpec()
    {
        using var document = new PragmataDocument("data000.bin", SyntheticInventory.Create(251));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var quantity = ImportedQuantityTests.Quantities(document)[^1];
        var metadata = Assert.Single(resolver.ResolveFieldMetadata([quantity.Path]));
        var projections = new[] { document.Field(quantity), document.Field(quantity, raw: true), document.References.Primitive(quantity) };
        Assert.All(projections, field =>
        {
            Assert.Equal(quantity.Path, field.ValueId);
            Assert.True(FieldSpecMetadata.Agree(metadata.Spec, field.Spec));
            Assert.Equal(document.GetValue(metadata.ValueId), field.Value);
        });
        Assert.Single(document.ApplyEdits([new(quantity.Path, FieldValue.Of(17))]).Changes);
    }

    [Fact]
    public void ExposedNonQuantityAliasesKeepReplayMetadataAndStableReferencePolicies()
    {
        using var document = new PragmataDocument("data000.bin", SyntheticInventory.Create(251));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var itemId = document.Layout.Items[0].Find(KnownHashes.ItemId)!;
        var primitive = document.References.Primitive(itemId);
        var reference = document.References.ItemField(itemId);
        var raw = document.Field(itemId, raw: true);
        var metadata = resolver.ResolveFieldMetadata([primitive.ValueId, reference.ValueId, raw.ValueId]);
        Assert.Equal(3, metadata.Count);
        Assert.All(metadata, field => _ = document.GetValue(field.ValueId));
        Assert.True(FieldSpecMetadata.Agree(primitive.Spec, metadata[0].Spec));
        Assert.True(FieldSpecMetadata.Agree(reference.Spec, metadata[1].Spec));
        Assert.True(FieldSpecMetadata.Agree(raw.Spec, metadata[2].Spec));
        var referenceSpec = Assert.IsType<ReferenceSpec>(reference.Spec);
        Assert.Same(referenceSpec.Assess, Assert.IsType<ReferenceSpec>(document.References.ItemField(itemId).Spec).Assess);
    }
}
