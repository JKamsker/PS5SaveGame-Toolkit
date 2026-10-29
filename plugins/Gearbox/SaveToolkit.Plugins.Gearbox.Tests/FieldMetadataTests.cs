using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetedFieldsResolveWithoutBuildingAViewAndKeepPresentation(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands);
        var original = document.Serialize().Single().Content.ToArray();
        var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata(
            ["character/xp", "character/level", "ammo/0", "unknown/field"]);

        Assert.Equal(["character/xp", "character/level", "ammo/0"], metadata.Select(field => field.ValueId));
        Assert.All(metadata, field =>
        {
            Assert.Equal(FieldBindingKind.Primary, field.BindingKind);
            Assert.Equal(field.ValueId, field.CanonicalValueId);
            Assert.NotNull(field.Presentation);
            Assert.NotNull(field.Presentation!.Description);
        });
        Assert.Equal(document.Root.Integer("experience_points"), document.GetValue("character/xp").AsInt64());
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void MetadataRequestsAreBoundedAndMalformedRequestsDoNotMutate()
    {
        using var document = EditCoverageData.Create(false);
        var original = document.Serialize().Single().Content.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => ((IFieldMetadataDocument)document)
            .ResolveFieldMetadata(Enumerable.Repeat("character/xp", FieldMetadataLimits.MaxBatchSize + 1).ToArray()));
        Assert.Throws<ArgumentException>(() => ((IFieldMetadataDocument)document).ResolveFieldMetadata([""]));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupMetadataQualifiesMembersAndUsesCurrentCandidateAfterRestore(bool wonderlands)
    {
        using var group = GroupProtocolTests.Open(wonderlands);
        var original = group.Serialize().Select(output => output.Content.ToArray()).ToArray();
        var metadata = ((IFieldMetadataDocument)group).ResolveFieldMetadata(
            ["profile/unknown", "character/character/xp", "unknown/id"]);

        Assert.Equal(["character/character/xp"], metadata.Select(field => field.ValueId));
        Assert.Equal("character/character/xp", metadata[0].CanonicalValueId);
        Assert.All(metadata, field => Assert.Equal(FieldBindingKind.Primary, field.BindingKind));

        var xp = group.Character.Root.Integer("experience_points");
        Assert.True(group.ApplyEdits([new("character/character/xp", FieldValue.Of(xp + 1))]).Succeeded);
        Assert.Equal(xp + 1, group.GetValue("character/character/xp").AsInt64());
        _ = group.RestoreMemento(group.CaptureMemento());
        Assert.IsType<IntegerSpec>(((IFieldMetadataDocument)group)
            .ResolveFieldMetadata(["character/character/xp"]).Single().Spec);
        Assert.Equal(xp + 1, group.GetValue("character/character/xp").AsInt64());
        Assert.False(original[0].AsSpan().SequenceEqual(group.Serialize().First().Content.Span));
    }
}
