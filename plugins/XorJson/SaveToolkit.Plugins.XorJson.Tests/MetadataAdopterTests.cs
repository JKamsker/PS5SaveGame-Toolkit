using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class MetadataAdopterTests
{
    private static readonly string[] DirectMetadataIds = ["value/3", "value/1", "unknown", "value/3"];
    private static readonly string[] RepeatedMetadataId = ["value/3"];
    private static readonly string[] OversizedMetadataId = ["value/1"];
    private static readonly string[] GroupMetadataIds = ["character/value/8", "global/value/2", "foreign/value/8", "character/value/8"];
    private static readonly string[] GroupContextIds = ["global/value/2", "character/value/8", "unknown/value/1"];
    private static readonly string[] DirectExpectedIds = ["value/3", "value/1"];
    private static readonly string[] GroupExpectedIds = ["character/value/8", "global/value/2"];
    private static readonly string[] ContextExpectedIds = ["global/value/2", "character/value/8"];
    private static readonly string[] RetainedId = ["value/3"];
    private static readonly string[] RemovedId = ["value/2"];

    [Fact]
    public void DirectMetadataIsTargetedSelfCanonicalAndAvailableBeforeView()
    {
        var bytes = XorJsonTests.Encode("""{"first":"same","second":"same","number":1.2300E+02}""");
        using var document = XorJsonTests.Open(bytes);
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);

        var before = document.Serialize()[0].Content.ToArray();
        var fields = resolver.ResolveFieldMetadata(DirectMetadataIds);
        var repeated = resolver.ResolveFieldMetadata(RepeatedMetadataId);

        Assert.Equal(DirectExpectedIds, fields.Select(field => field.ValueId));
        Assert.All(fields, field =>
        {
            Assert.Equal(FieldBindingKind.Primary, field.BindingKind);
            Assert.Equal(field.ValueId, field.CanonicalValueId);
            Assert.Null(field.Path);
            Assert.NotNull(field.Presentation);
            Assert.Contains("$.member(", field.Presentation!.Description);
        });
        Assert.NotSame(fields[0].Spec, repeated[0].Spec);
        Assert.Equal(before, document.Serialize()[0].Content.ToArray());
        Assert.Equal(FieldValue.ExactNumber("1.2300E+02"), document.GetValue("value/3"));
        Assert.Equal(FieldValue.Of("same"), document.GetValue("value/1"));
    }

    [Fact]
    public void MetadataValidatesTheCompleteBoundedRequestBeforeLookup()
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("""{"a":1,"b":2}"""));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);

        Assert.Throws<ArgumentNullException>(() => resolver.ResolveFieldMetadata(null!));
        Assert.Throws<ArgumentException>(() => resolver.ResolveFieldMetadata(["value/1", ""]));
        Assert.Single(resolver.ResolveFieldMetadata(Enumerable.Repeat("value/1", FieldMetadataLimits.MaxBatchSize).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.ResolveFieldMetadata(Enumerable.Repeat("value/1", FieldMetadataLimits.MaxBatchSize + 1).ToArray()));
    }

    [Fact]
    public void OversizedNumbersRemainReadOnlyInspectionMetadata()
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("""{"number":1e1000000001,"other":2}"""));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var field = Assert.Single(resolver.ResolveFieldMetadata(OversizedMetadataId));

        Assert.True(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Unknown, field.Presentation?.Confidence);
        Assert.Contains("exact-number", field.Presentation?.Warning, StringComparison.Ordinal);
        Assert.False(document.ApplyEdits([new("value/1", FieldValue.Of("2"))]).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralRestoreRebindsMetadataToTheCurrentTokenModel(bool xor)
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("""{"rows":[1],"keep":2}""", xor));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var retained = Assert.Single(resolver.ResolveFieldMetadata(RetainedId));

        var removed = document.InvokeAction("json/remove/2", new Dictionary<string, FieldValue>());
        Assert.Empty(resolver.ResolveFieldMetadata(RemovedId));
        Assert.Single(resolver.ResolveFieldMetadata(RetainedId));
        Assert.Equal(FieldValue.ExactNumber("2"), document.GetValue("value/3"));
        Assert.NotSame(retained.Spec, Assert.Single(resolver.ResolveFieldMetadata(RetainedId)).Spec);

        _ = document.RestoreMemento(removed.Revert!);
        Assert.Single(resolver.ResolveFieldMetadata(RemovedId));
        Assert.Equal(FieldValue.ExactNumber("1"), document.GetValue("value/2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupMetadataAndContextUseCurrentMemberIdentity(bool xor)
    {
        using var fixture = new GroupIdentityFixture(xor);
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(fixture.Group);
        var context = Assert.IsAssignableFrom<IGroupedFieldContext>(fixture.Group);
        var before = fixture.Group.Serialize().Select(output => (output.RelativePath, Bytes: output.Content.ToArray())).ToArray();

        var fields = metadata.ResolveFieldMetadata(GroupMetadataIds);
        Assert.Equal(GroupExpectedIds, fields.Select(field => field.ValueId));
        Assert.All(fields, field => Assert.Equal(field.ValueId, field.CanonicalValueId));

        var contexts = context.ResolveFieldContext(GroupContextIds);
        Assert.Equal(ContextExpectedIds, contexts.Select(value => value.ValueId));
        Assert.Equal(("global", fixture.Input.Members.Single(member => member.Role == "global").File.RelativePath),
            (contexts[0].Role, contexts[0].RelativePath));
        Assert.Equal(("character", fixture.Input.Members.Single(member => member.Role == "character").File.RelativePath),
            (contexts[1].Role, contexts[1].RelativePath));
        Assert.All(contexts, value => Assert.Null(value.Provenance));

        var after = fixture.Group.Serialize().Select(output => (output.RelativePath, Bytes: output.Content.ToArray())).ToArray();
        Assert.Equal(before.Select(value => value.RelativePath), after.Select(value => value.RelativePath));
        Assert.Equal(before.SelectMany(value => value.Bytes), after.SelectMany(value => value.Bytes));
    }
}
