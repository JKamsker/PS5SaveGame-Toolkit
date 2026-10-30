using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class MetadataAdopterTests
{
    private static readonly string[] DirectMetadataIds = ["value/3", "value/1", "unknown", "value/2", "value/3"];
    private static readonly string[] RepeatedMetadataId = ["value/3"];
    private static readonly string[] OversizedMetadataId = ["value/1"];
    private static readonly string[] GroupMetadataIds = ["character/value/8", "character/value/08", "global/value/2", "foreign/value/8", "character/value/8"];
    private static readonly string[] GroupContextIds = ["global/value/2", "character/value/08", "character/value/8", "unknown/value/1"];
    private static readonly string[] DirectExpectedIds = ["value/3", "value/1", "value/2"];
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
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        Assert.Equal(FieldValue.ExactNumber("1.2300E+02"), document.GetValue("value/3"));
        Assert.Equal(FieldValue.Of("same"), document.GetValue("value/1"));
        Assert.Equal(document.GetValue("value/1"), document.GetValue("value/2"));
        Assert.NotEqual(fields[1].CanonicalValueId, fields[2].CanonicalValueId);
    }

    [Fact]
    public void MetadataValidatesTheCompleteBoundedRequestBeforeLookup()
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("""{"a":1,"b":2}"""));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);

        Assert.Throws<ArgumentNullException>(() => resolver.ResolveFieldMetadata(null!));
        Assert.Throws<ArgumentException>(() => resolver.ResolveFieldMetadata(["value/1", ""]));
        Assert.Throws<ArgumentNullException>(() => resolver.ResolveFieldMetadata(["value/1", null!]));
        Assert.Single(resolver.ResolveFieldMetadata(Enumerable.Repeat("value/1", FieldMetadataLimits.MaxBatchSize).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.ResolveFieldMetadata(Enumerable.Repeat("value/1", FieldMetadataLimits.MaxBatchSize + 1).ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoncanonicalIdsStayUnknownAndCannotWrite(bool xor)
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("{\"a\":1}", xor));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var before = document.Serialize().Single().Content.ToArray();

        var fields = resolver.ResolveFieldMetadata(["value/1", "value/01", "value/+1", "value/0001"]);

        Assert.Equal(["value/1"], fields.Select(field => field.ValueId));
        Assert.False(document.ApplyEdits([new("value/01", FieldValue.ExactNumber("2"))]).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
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

    [Fact]
    public void UnsupportedCharacterProfileMetadataRemainsReadOnlyBeforeView()
    {
        const string text = "{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":1,\"FormatHash\":3777917472},\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":false},\"Level\":1}}";
        using var document = new XorJsonPlugin().Open(
            MemorySaveSet.SingleFile("hero.sav", Encoding.UTF8.GetBytes(text)));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var before = document.Serialize().Single().Content.ToArray();

        var field = Assert.Single(resolver.ResolveFieldMetadata(["value/8"]));

        Assert.True(field.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new("value/8", FieldValue.ExactNumber("2"))]).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyMemberNamesUseDistinctTypedPathLabelsAndRemainLossless(bool xor)
    {
        var original = "{\"\":1,\"\":2,\"keep\":\"é\"}";
        using var document = XorJsonTests.Open(XorJsonTests.Encode(original, xor));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);

        var fields = resolver.ResolveFieldMetadata(["value/1", "value/2"]);

        Assert.Equal(["$.member(\"\",0)", "$.member(\"\",1)"], fields.Select(field => field.Label));
        Assert.All(fields, field => Assert.Contains("member(\"\"", field.Presentation?.Description, StringComparison.Ordinal));
        Assert.True(document.ApplyEdits([new("value/1", FieldValue.ExactNumber("3"))]).Succeeded);
        Assert.Equal(XorJsonTests.Encode("{\"\":3,\"\":2,\"keep\":\"é\"}", xor),
            document.Serialize().Single().Content.ToArray());
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
    public void StructuralPublicationAddsCurrentMetadataAndFailedWritesKeepIt(bool xor)
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("{\"items\":[1]}", xor));
        var resolver = Assert.IsAssignableFrom<IFieldMetadataDocument>(document);
        var originalBytes = document.Serialize().Single().Content.ToArray();

        var addition = document.InvokeAction("json/add/1", new Dictionary<string, FieldValue>
        {
            ["json"] = FieldValue.Of("2")
        });
        const string addedId = "value/3"; // Parse allocated 0..2; committed scalar addition gets the next identity.
        Assert.Single(resolver.ResolveFieldMetadata([addedId]));
        Assert.Equal(FieldValue.ExactNumber("2"), document.GetValue(addedId));
        Assert.Single(resolver.ResolveFieldMetadata([addedId]));
        var afterAddition = document.Serialize().Single().Content.ToArray();

        Assert.Throws<SaveFormatException>(() => document.InvokeAction(
            "json/add/1", new Dictionary<string, FieldValue> { ["json"] = FieldValue.Of("1,2") }));
        Assert.Equal(afterAddition, document.Serialize().Single().Content.ToArray());
        Assert.Single(resolver.ResolveFieldMetadata([addedId]));

        _ = document.RestoreMemento(addition.Revert!);
        Assert.Empty(resolver.ResolveFieldMetadata([addedId]));
        var restoredId = XorJsonTests.Fields(document).Single(field => field.Value == FieldValue.ExactNumber("1")).ValueId;
        Assert.Single(resolver.ResolveFieldMetadata([restoredId]));
        Assert.Equal(originalBytes, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupMetadataAndContextUseCurrentMemberIdentity(bool xor)
    {
        using var fixture = new GroupIdentityFixture(xor);
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(fixture.Group);
        var context = Assert.IsAssignableFrom<IGroupedFieldContext>(fixture.Group);
        var before = fixture.Input.Members.Select(member => (RelativePath: member.File.RelativePath, Bytes: member.File.ReadAllBytes())).ToArray();

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
        Assert.False(fixture.Group.ApplyEdits([new("global/value/0002", FieldValue.ExactNumber("49"))]).Succeeded);
        Assert.Throws<ArgumentNullException>(() => metadata.ResolveFieldMetadata(["global/value/2", null!]));
        Assert.Throws<ArgumentException>(() => context.ResolveFieldContext(["global/value/2", ""]));
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.ResolveFieldMetadata(Enumerable.Repeat("global/value/2", 201).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.ResolveFieldContext(Enumerable.Repeat("global/value/2", 201).ToArray()));
        Assert.Single(context.ResolveFieldContext(Enumerable.Repeat("global/value/2", 200).ToArray()));

        var after = fixture.Group.Serialize().Select(output => (output.RelativePath, Bytes: output.Content.ToArray())).ToArray();
        Assert.Equal(before.Select(value => value.RelativePath), after.Select(value => value.RelativePath));
        foreach (var member in before) Assert.Equal(member.Bytes, after.Single(value => value.RelativePath == member.RelativePath).Bytes);

        var snapshot = fixture.Group.CaptureMemento();
        Assert.True(fixture.Group.ApplyEdits([new("global/value/2", FieldValue.ExactNumber("49"))]).Succeeded);
        _ = fixture.Group.RestoreMemento(snapshot);
        var restored = metadata.ResolveFieldMetadata(GroupExpectedIds);
        Assert.Equal(GroupExpectedIds, restored.Select(field => field.ValueId));
        Assert.NotSame(fields[1].Spec, restored[1].Spec);
        Assert.Equal(FieldValue.ExactNumber("50"), fixture.Group.GetValue("global/value/2"));
        Assert.Equal(contexts, context.ResolveFieldContext(GroupContextIds));
        fixture.AssertOriginalBytes(fixture.Group);
    }

    [Fact]
    public void GroupEmptyMemberNamesUseCurrentIdentityAndPreserveOtherMemberBytes()
    {
        var set = new MemorySaveSet()
            .With("PPSA16064", "Character0", new Dictionary<string, byte[]>
            {
                ["hero.sav"] = Encoding.UTF8.GetBytes("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472},\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":false},\"\":1,\"\":2}}"),
            })
            .With("PPSA16064", "GlobalSaveDataDefault", new Dictionary<string, byte[]>
            {
                ["global.sav"] = XorJsonTests.Encode("{\"blobs\":[],\"volume\":50}"),
            });
        var files = set.Directories.SelectMany(directory => directory.Files).ToArray();
        using var group = new XorJsonPlugin().OpenGroup(new([
            new("character", files[0]), new("global", files[1]),
        ]), IReferenceContext.Empty);
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(group);
        var context = Assert.IsAssignableFrom<IGroupedFieldContext>(group);
        var before = group.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray());

        var fields = metadata.ResolveFieldMetadata(["character/value/8", "character/value/9"]);
        Assert.Equal(["character/value/8", "character/value/9"], fields.Select(field => field.ValueId));
        Assert.Equal(["$.member(\"CharacterSaveV1\",0).member(\"\",0)", "$.member(\"CharacterSaveV1\",0).member(\"\",1)"], fields.Select(field => field.Label));
        var contexts = context.ResolveFieldContext(["character/value/8", "character/value/9"]);
        Assert.All(contexts, value =>
        {
            Assert.Equal("character", value.Role);
            Assert.Null(value.Provenance);
        });

        var edit = group.ApplyEdits([new("character/value/8", FieldValue.ExactNumber("3"))]);
        Assert.True(edit.Succeeded);
        var changed = group.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
        Assert.Equal(Encoding.UTF8.GetBytes("{\"SerializeMeta\":{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":2,\"FormatHash\":3777917472},\"CharacterSaveV1\":{\"MetaData\":{\"IsOnline\":false},\"\":3,\"\":2}}"), changed[files[0].RelativePath]);
        Assert.Equal(before[files[1].RelativePath], changed[files[1].RelativePath]);
    }

}
