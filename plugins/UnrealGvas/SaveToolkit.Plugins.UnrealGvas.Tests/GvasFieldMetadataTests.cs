using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasFieldMetadataTests
{
    [Fact]
    public void ResolvesOnlyRequestedScalarsBeforeAnyViewExpansion()
    {
        using var document = new UnrealGvasPlugin().Open(
            MemorySaveSet.SingleFile("nested.sav", GvasFixture.Nested()));
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);

        var fields = metadata.ResolveFieldMetadata(["property/0/0/0/0"]);

        var field = Assert.Single(fields);
        Assert.Equal("property/0/0/0/0", field.ValueId);
        Assert.Equal("Item0", document.GetValue(field.ValueId).AsString());
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("property/0/0/1/0"));
        Assert.Equal(GvasFixture.Nested(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ValidatesBatchBeforeRegisteringAndDeduplicatesExactIds()
    {
        using var document = new UnrealGvasPlugin().Open(
            MemorySaveSet.SingleFile("nested.sav", GvasFixture.Nested()));
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        const string id = "property/0/0/0/0";

        Assert.Single(metadata.ResolveFieldMetadata([id, id]));
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.ResolveFieldMetadata(
            Enumerable.Repeat(id, FieldMetadataLimits.MaxBatchSize + 1).ToArray()));
        Assert.Throws<ArgumentException>(() => metadata.ResolveFieldMetadata([id, ""]));
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("property/0/0/1/0"));
        Assert.Empty(metadata.ResolveFieldMetadata(["property/0/0/0"]));
        Assert.Empty(metadata.ResolveFieldMetadata(["property/does-not-exist"]));
    }

    [Fact]
    public void MetadataUsesEffectiveReadOnlyPolicyForProtectedScalars()
    {
        using var document = new UnrealGvasPlugin().Open(
            MemorySaveSet.SingleFile("jedi.sav", TitleFixture.Scalars("jedi"), "PPSA02199"));
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);

        var fields = metadata.ResolveFieldMetadata(["property/2", "property/4", "property/1"])
            .ToDictionary(field => field.ValueId, StringComparer.Ordinal);

        Assert.False(fields["property/2"].Spec.ReadOnly);
        Assert.True(fields["property/4"].Spec.ReadOnly);
        Assert.True(fields["property/1"].Spec.ReadOnly);
        Assert.True(document.ApplyEdits([new("property/2", FieldValue.Of(19L))]).Succeeded);
        Assert.False(document.ApplyEdits([new("property/4", FieldValue.Of(19L))]).Succeeded);
    }

    [Fact]
    public void QueryableWrapperForwardsMetadataWithoutChangingBytes()
    {
        var bytes = TitleFixture.Settings();
        using var document = new UnrealGvasPlugin().Open(
            MemorySaveSet.SingleFile("settings.sav", bytes, "PPSA02199"));
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);

        var field = Assert.Single(metadata.ResolveFieldMetadata(["property/0"]));

        Assert.Equal("property/0", field.ValueId);
        Assert.Equal(1.25, document.GetValue(field.ValueId).AsDouble());
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void StructuralChangesRetireMetadataUntilFreshModelResolution()
    {
        var original = GvasFixture.Nested();
        using var document = new UnrealGvasPlugin().Open(
            MemorySaveSet.SingleFile("nested.sav", original));
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        const string retained = "property/0/3/0";
        const string removed = "property/0/3/1";
        Assert.True(document.GetValue(Assert.Single(
            metadata.ResolveFieldMetadata([retained])).ValueId).AsBool());

        var outcome = document.InvokeAction(
            "gvas/structure/remove-last/property/0/3", new Dictionary<string, FieldValue>());

        Assert.True(outcome.StructureChanged);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(retained));
        Assert.Empty(metadata.ResolveFieldMetadata([removed]));
        Assert.Equal("property/0/3/0", Assert.Single(metadata.ResolveFieldMetadata([retained])).ValueId);

        _ = document.RestoreMemento(outcome.Revert!);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(retained));
        Assert.Equal("property/0/3/0", Assert.Single(metadata.ResolveFieldMetadata([retained])).ValueId);
        Assert.Equal("property/0/3/1", Assert.Single(metadata.ResolveFieldMetadata([removed])).ValueId);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
