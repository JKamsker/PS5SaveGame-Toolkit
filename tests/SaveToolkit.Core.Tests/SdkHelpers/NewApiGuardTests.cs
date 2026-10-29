using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class NewApiGuardTests
{
    [Fact]
    public void MetadataResolutionEnforcesBatchBoundBeforeDeduplicationAndNeverReadsValues()
    {
        using var document = new PublishedDocument();
        document.Publish(new("value", "Value", IntegerSpec.Int32), () => throw new InvalidOperationException("Must not read."));
        Assert.Single(document.ResolveFieldMetadata(Enumerable.Repeat("value", FieldMetadataLimits.MaxBatchSize).ToArray()));
        Assert.Empty(document.ResolveFieldMetadata([]));
        Assert.Empty(document.ResolveFieldMetadata(["unknown"]));
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => document.ResolveFieldMetadata(
            Enumerable.Repeat("value", FieldMetadataLimits.MaxBatchSize + 1).ToArray()));
        Assert.Equal("valueIds", error.ParamName);
        Assert.Contains("200", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidMetadataCannotReplaceAnExistingBinding()
    {
        using var document = new PublishedDocument();
        document.Publish(new("value", "Value", IntegerSpec.Int32), () => FieldValue.Of(1));
        Assert.Throws<ArgumentNullException>(() => document.Publish(null!, () => FieldValue.Of(2)));
        Assert.Throws<ArgumentNullException>(() => document.Publish(new("value", "Value", null!), () => FieldValue.Of(2)));
        Assert.Throws<ArgumentNullException>(() => document.Publish(new("value", "Value", IntegerSpec.Int32), null!));
        Assert.Throws<ArgumentException>(() => document.Publish(new("", "Value", IntegerSpec.Int32), () => FieldValue.Of(2)));
        Assert.Throws<ArgumentException>(() => document.Publish(new("value", "", IntegerSpec.Int32), () => FieldValue.Of(2)));
        Assert.Throws<ArgumentException>(() => document.Publish(new("value", "Value", IntegerSpec.Int32)
            { BindingKind = (FieldBindingKind)int.MaxValue }, () => FieldValue.Of(2)));
        Assert.Equal(FieldValue.Of(1), document.GetValue("value"));
        Assert.Single(document.ResolveFieldMetadata(["value"]));
    }

    [Fact]
    public void ResolutionRejectsNullInputsAndReturnsAnImmutableDescriptorList()
    {
        using var document = new PublishedDocument();
        document.Publish(new("value", "Value", IntegerSpec.Int32), () => FieldValue.Of(1));
        Assert.Throws<ArgumentNullException>(() => document.ResolveFieldMetadata(null!));
        Assert.Throws<ArgumentException>(() => document.ResolveFieldMetadata([""]));
        Assert.Throws<ArgumentNullException>(() => document.ResolveFieldMetadata([null!]));
        var fields = document.ResolveFieldMetadata(["value", "value"]);
        Assert.Single(fields);
        Assert.Throws<NotSupportedException>(() => ((IList<FieldMetadata>)fields)[0] = fields[0] with { Label = "Changed" });
    }

    [Fact]
    public void RebindingDerivedStorageWithASetterIsRejectedEvenWhenTheSpecStillAgrees()
    {
        using var document = new PublishedDocument();
        document.PublishDerived();
        Assert.Single(document.ResolveFieldMetadata(["value"]));
        document.ReplaceDerivedWithSetter();
        Assert.Throws<InvalidOperationException>(() => document.ResolveFieldMetadata(["value"]));
    }

    [Fact]
    public void SpecHelpersRejectNullWithNamedArguments()
    {
        Assert.Equal("spec", Assert.Throws<ArgumentNullException>(() => FieldSpecMetadata.Snapshot(null!)).ParamName);
        Assert.Equal("first", Assert.Throws<ArgumentNullException>(() => FieldSpecMetadata.Agree(null!, IntegerSpec.Int32)).ParamName);
        Assert.Equal("second", Assert.Throws<ArgumentNullException>(() => FieldSpecMetadata.Agree(IntegerSpec.Int32, null!)).ParamName);
    }

    [Fact]
    public void MemberConstructionRejectsNullCallbacksAndNullRouterEntries()
    {
        using var document = new PublishedDocument();
        DocumentMemberIdentityPolicy[] invalid =
        [
            new() { ToGroupId = null! }, new() { TryToLocalId = null! }, new() { QueryRowIdentity = null! },
            new() { TableRowIdentity = null! }, new() { TableCellScope = null! }, new() { ExpansionScope = null! },
            new() { NavigationPath = null! }, new() { CollectionTitle = null! }, new() { CollectionColumn = null! },
        ];
        foreach (var policy in invalid)
            Assert.Equal("identityPolicy", Assert.Throws<ArgumentException>(() => new DocumentMemberAdapter("a", document, policy)).ParamName);
        Assert.Equal("members", Assert.Throws<ArgumentException>(() => new DocumentMemberRouter([null!])).ParamName);
        var router = new DocumentMemberRouter([new("a", document)]);
        Assert.Equal("id", Assert.Throws<ArgumentNullException>(() => router.Resolve(null!)).ParamName);
    }

    private sealed class PublishedDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public override string Title => "Published";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public void Publish(FieldMetadata metadata, Func<FieldValue> get) => RegisterField(metadata, get, _ => { });
        public void PublishDerived() => RegisterField(new("value", "Derived", IntegerSpec.Int32 with { ReadOnly = true })
            { BindingKind = FieldBindingKind.Derived }, () => FieldValue.Of(1));
        public void ReplaceDerivedWithSetter() => RegisterField("value", IntegerSpec.Int32 with { ReadOnly = true },
            () => FieldValue.Of(1), _ => { });
    }
}
