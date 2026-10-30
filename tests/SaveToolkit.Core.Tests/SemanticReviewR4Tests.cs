using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR4Tests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Fact]
    public void OriginalRenderedProjectionsAreEnrichedBeforeCanonicalComparison()
    {
        using var current = new ProjectionDocument();
        using var session = Open(current, "projection", () => new ProjectionDocument());
        _ = session.BuildView();
        Assert.True(session.GetChanges().Complete);

        Assert.True(session.ApplyEdits([new IncomingEdit("primary", JsonSerializer.SerializeToElement(2))]).Ok);
        var change = Assert.Single(session.GetChanges().Changes);
        Assert.Equal("primary", change.ValueId);
        Assert.Equal("Canonical primary", change.Label);
        Assert.Equal(["canonical", "primary"], change.Path);
        Assert.True(change.CanRevert);
    }

    [Fact]
    public void FailedHiddenTargetRemainsIncompleteAfterUnrelatedTargetSucceeds()
    {
        using var current = new FailureTrackingDocument();
        var failA = true;
        using var session = Open(current, "failure-tracking", () => new FailureTrackingDocument(failA));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden-a", JsonSerializer.SerializeToElement(2))]).Ok);
        var first = session.GetChanges();
        Assert.False(first.Complete);
        Assert.Contains(first.Warnings, warning => warning.Contains("hidden-a", StringComparison.Ordinal));

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden-b", JsonSerializer.SerializeToElement(3))]).Ok);
        var second = session.GetChanges();
        Assert.False(second.Complete);
        Assert.Contains(second.Warnings, warning => warning.Contains("hidden-a", StringComparison.Ordinal));
        Assert.True(second.Changes.Single(change => change.ValueId == "hidden-b").CanRevert);
        Assert.False(second.Changes.Single(change => change.ValueId == "hidden-a").CanRevert);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpecDriftRevokesIndividualRevertAuthority(bool typeDrift)
    {
        using var current = new SpecDriftDocument(typeDrift);
        using var session = Open(current, "spec-drift", () => new SpecDriftDocument(typeDrift));
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("value", JsonSerializer.SerializeToElement(2))]).Ok);

        current.Drift = true;
        var review = session.GetChanges();
        Assert.False(review.Complete);
        Assert.Empty(review.Changes);
        Assert.Contains(review.Warnings, warning => warning.Contains("authority changed", StringComparison.Ordinal));
    }

    [Fact]
    public void TargetedEagerFieldMergesRenderedNavigationWithoutReplacingMetadata()
    {
        using var current = new NavigationMetadataDocument(f8: false);
        using var session = Open(current, "navigation-eager", () => new NavigationMetadataDocument(f8: false));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("quantity", JsonSerializer.SerializeToElement(2))]).Ok);
        var change = Assert.Single(session.GetChanges().Changes);
        Assert.Equal(["section:Eager"], change.Path);
        Assert.Null(change.Target);
        Assert.True(change.CanNavigate);
        Assert.Equal("Authoritative quantity", change.Label);
    }

    [Fact]
    public void TargetedQueryableFieldMergesRenderedNavigationAndCollectionTarget()
    {
        using var current = new NavigationMetadataDocument(f8: true);
        using var session = Open(current, "navigation-f8", () => new NavigationMetadataDocument(f8: true));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("quantity", JsonSerializer.SerializeToElement(2))]).Ok);
        var change = Assert.Single(session.GetChanges().Changes);
        Assert.NotEmpty(current.Queries);
        Assert.NotNull(change.Target);
        Assert.Equal(["section:Rows", "row:row-1"], change.Path);
        Assert.Equal("rows", change.Target?.CollectionId);
        Assert.True(change.CanNavigate);
    }

    [Fact]
    public void PartialTargetedReviewRetainsCanonicalPresentationAndCompletenessWarning()
    {
        using var current = new PartialTargetedDocument();
        using var session = Open(current, "partial-targeted", () => new PartialTargetedDocument { FailView = true });
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        current.FailView = true;
        var review = session.GetChanges();
        var change = Assert.Single(review.Changes);
        Assert.False(review.Complete);
        Assert.Equal("hidden", change.CanonicalValueId);
        Assert.Equal("Inferred", change.Presentation?.Confidence.ToString());
        Assert.Equal("Review before exporting.", change.Presentation?.Warning);
    }

    [Fact]
    public void PresentationResolverFailureReturnsIncompleteReviewWithBoundedDiagnostic()
    {
        using var current = new PartialTargetedDocument();
        using var session = Open(current, "presentation-failure", () => new PartialTargetedDocument());
        _ = session.BuildView();
        Assert.True(session.GetChanges().Complete);

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        current.FailView = true;
        current.ResetMetadataCalls();
        current.ThrowAfterFirstMetadataCall = true;
        var review = session.GetChanges();
        Assert.False(review.Complete);
        Assert.Contains(review.Warnings, warning => warning.Contains("Presentation metadata coverage is incomplete", StringComparison.Ordinal));
        Assert.Single(review.Changes);
    }

    private DocumentSession Open(ISaveDocument document, string name, Func<ISaveDocument> openOriginal) =>
        new(name, _store.Create(name, [new ImportSource("save.dat", () => new MemoryStream([1]))])
            .Set.AllDiskFiles.Single(), new CounterPlugin(), document, openOriginal: openOriginal);

    private sealed class ProjectionDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public ProjectionDocument()
        {
            RegisterField(new FieldMetadata("primary", "Canonical primary", IntegerSpec.Int32)
            { Path = ["canonical", "primary"] }, () => FieldValue.Of(_value), value => _value = value.AsInt32());
            foreach (var id in new[] { "derived-one", "derived-two" })
                RegisterField(new FieldMetadata(id, "Canonical primary", IntegerSpec.Int32 with { ReadOnly = true })
                {
                    BindingKind = FieldBindingKind.Derived, CanonicalValueId = "primary", Path = ["canonical", id],
                }, () => FieldValue.Of(_value));
        }

        public override string Title => "Projection";
        public override ViewNode BuildView() => new SectionNode("Values", [
            Field("primary", "Rendered primary"), Field("derived-one", "Rendered derived one"),
            Field("derived-two", "Rendered derived two")]);
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];
    }

    private sealed class FailureTrackingDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _a = 1;
        private int _b = 1;
        private readonly bool _failA;

        public FailureTrackingDocument(bool failA = false) : this(failA, true) { }
        private FailureTrackingDocument(bool failA, bool register)
        {
            _failA = failA;
            if (!register) return;
            RegisterField(new FieldMetadata("hidden-a", "Hidden A", IntegerSpec.Int32), () => FieldValue.Of(_a), value => _a = value.AsInt32());
            RegisterField(new FieldMetadata("hidden-b", "Hidden B", IntegerSpec.Int32), () => FieldValue.Of(_b), value => _b = value.AsInt32());
        }

        public override string Title => "Failure tracking";
        public override ViewNode BuildView() => new TextNode("Hidden values");
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_a, (byte)_b })];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            if (_failA && ids.Contains("hidden-a", StringComparer.Ordinal)) throw new InvalidOperationException("hidden-a resolver failed");
            return base.ResolveFieldMetadata(ids);
        }
    }

    private sealed class SpecDriftDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public bool Drift { get; set; }
        public override string Title => "Spec drift";
        public override ViewNode BuildView() => Field("value", "Value");
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];

        public SpecDriftDocument() : this(false) { }

        public SpecDriftDocument(bool typeDrift) : this(typeDrift, true) { }
        private SpecDriftDocument(bool typeDrift, bool register)
        {
            if (register)
                RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32), () => FieldValue.Of(_value), value => _value = value.AsInt32());
            TypeDrift = typeDrift;
        }

        private bool TypeDrift { get; }
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            var fields = base.ResolveFieldMetadata(ids);
            if (!Drift) return fields;
            return fields.Select(field => field with
            {
                Spec = TypeDrift ? new TextSpec() : new IntegerSpec(0, 1),
            }).ToArray();
        }
    }

    private sealed class NavigationMetadataDocument : BoundSaveDocument, IFieldMetadataDocument, IQueryableDocument
    {
        private int _value = 1;
        private readonly bool _f8;
        public List<QueryableCollectionQuery> Queries { get; } = [];
        public override string Title => "Navigation metadata";
        public IReadOnlyList<QueryableCollection> Collections => [
            new("rows", "Rows", [new("value", "Value", IntegerSpec.Int32)], 1)];

        public NavigationMetadataDocument() : this(false) { }

        public NavigationMetadataDocument(bool f8)
        {
            _f8 = f8;
            RegisterField(new FieldMetadata("quantity", "Authoritative quantity", IntegerSpec.Int32),
                () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }

        public override ViewNode BuildView() => _f8
            ? new SectionNode("Rows", [new QueryableTableNode("rows", "Rows", [new("value", "Value", IntegerSpec.Int32)], 1)])
            : new SectionNode("Eager", [Field("quantity", "Rendered quantity")]);

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Queries.Add(query);
            return new(collectionId, [new("value", "Value", IntegerSpec.Int32)],
                [new TableRow("row-1", new Dictionary<string, ViewNode>
                { ["value"] = Field("quantity", "Rendered quantity") })], 1, 1, 0, query.Limit);
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];
    }

    private sealed class PartialTargetedDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public bool FailView { get; set; }
        public bool ThrowAfterFirstMetadataCall { get; set; }
        private int _metadataCalls;

        public override string Title => "Partial targeted";
        public override ViewNode BuildView() => FailView ? throw new InvalidOperationException("Current view unavailable.")
            : new TextNode("No rendered fields");

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            _metadataCalls++;
            if (ThrowAfterFirstMetadataCall && _metadataCalls > 1)
                throw new InvalidOperationException("malformed presentation metadata");
            return base.ResolveFieldMetadata(ids);
        }

        public void ResetMetadataCalls() => _metadataCalls = 0;

        public PartialTargetedDocument()
        {
            RegisterField(new FieldMetadata("hidden", "Canonical quantity", IntegerSpec.Int32)
            {
                Path = ["section:canonical"],
                Presentation = new FieldPresentation { Confidence = Confidence.Inferred, Warning = "Review before exporting." },
            }, () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];
    }
}
