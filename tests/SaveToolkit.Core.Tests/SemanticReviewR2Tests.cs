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

public sealed class SemanticReviewR2Tests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Fact]
    public void DuplicateIdWithinWalkKeepsFirstMetadataAndLaterQueryableNavigation()
    {
        using var current = new DuplicateNavigationDocument();
        using var session = Open(current, () => new DuplicateNavigationDocument());
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("quantity", JsonSerializer.SerializeToElement(2))]).Ok);
        var change = Assert.Single(session.GetChanges().Changes);

        Assert.Equal("Authoritative quantity", change.Label);
        Assert.Equal(["section:Root", "section:Rows", "row:row-1"], change.Path);
        Assert.True(change.CanNavigate);
        Assert.Equal("rows", change.Target?.CollectionId);
        Assert.Equal("row-1", change.Target?.RowKey);
        Assert.Equal(change.Path, change.NavigationTarget?.Path);
    }

    [Fact]
    public void MemberContextOnlyResolvesCanonicalChangedRows()
    {
        using var current = new LargeMetadataDocument(5_000);
        var requests = new List<string[]>();
        using var session = Open(current, () => new LargeMetadataDocument(5_000), ids =>
        {
            requests.Add(ids.ToArray());
            return ids.ToDictionary(id => id,
                id => new SemanticMemberContext("character", "character.dat", "synthetic", "fixture"),
                StringComparer.Ordinal);
        });
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("field-0042", JsonSerializer.SerializeToElement(2))]).Ok);
        var log = session.GetChanges();

        Assert.True(log.Complete);
        Assert.Single(log.Changes);
        Assert.Single(requests);
        Assert.Equal(["field-0042"], requests[0]);
    }

    [Fact]
    public void MemberContextBoundRetainsAllChangedRowsAndReportsOmissions()
    {
        const int changed = FieldMetadataLimits.MaxClosureIds + 8;
        using var current = new LargeMetadataDocument(changed);
        var requests = new List<string[]>();
        using var session = Open(current, () => new LargeMetadataDocument(changed), ids =>
        {
            requests.Add(ids.ToArray());
            return ids.ToDictionary(id => id,
                id => new SemanticMemberContext("character", "character.dat", "synthetic", "fixture"),
                StringComparer.Ordinal);
        });
        _ = session.BuildView();

        var edits = Enumerable.Range(0, changed)
            .Select(index => new IncomingEdit($"field-{index:0000}", JsonSerializer.SerializeToElement(2)))
            .ToArray();
        // The edit endpoint itself keeps the foundation's 4096-ID admission bound. Two
        // ordinary edits still exercise a review with more changed identities than the
        // group-context resolver may request in one bounded query.
        Assert.True(session.ApplyEdits(edits[..FieldMetadataLimits.MaxClosureIds]).Ok);
        Assert.True(session.ApplyEdits(edits[FieldMetadataLimits.MaxClosureIds..]).Ok);
        var log = session.GetChanges();

        Assert.False(log.Complete);
        Assert.Equal(changed, log.Changes.Count);
        Assert.True(requests.Sum(batch => batch.Length) <= FieldMetadataLimits.MaxClosureIds);
        Assert.True(log.Coverage?.OmittedCount >= 8);
        Assert.True(log.Coverage?.OmittedCountIsLowerBound);
        Assert.Contains(log.Warnings, warning => warning.Contains("Group member context is bounded", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedMemberContextRetainsChangeAndReportsCoverage()
    {
        using var current = new LargeMetadataDocument(1);
        using var session = Open(current, () => new LargeMetadataDocument(1), _ => new Dictionary<string, SemanticMemberContext>
        {
            ["foreign-id"] = new("character", "character.dat"),
        });
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("field-0000", JsonSerializer.SerializeToElement(2))]).Ok);

        var log = session.GetChanges();

        Assert.False(log.Complete);
        Assert.Single(log.Changes);
        Assert.True(log.Coverage?.OmittedCount >= 1);
        Assert.True(log.Coverage?.OmittedCountIsLowerBound);
        Assert.Contains(log.Warnings, warning => warning.Contains("malformed canonical context", StringComparison.Ordinal));
    }

    [Fact]
    public void MemberContextCancellationIsNotConvertedToPartialSuccess()
    {
        using var current = new LargeMetadataDocument(1);
        using var session = Open(current, () => new LargeMetadataDocument(1), _ =>
            throw new OperationCanceledException("context cancelled"));
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("field-0000", JsonSerializer.SerializeToElement(2))]).Ok);

        Assert.Throws<OperationCanceledException>(() => session.GetChanges());
    }

    private DocumentSession Open(ISaveDocument current, Func<ISaveDocument> original,
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, SemanticMemberContext>>? memberContext = null)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)));
        var workspace = _store.Create("semantic-r2", [new ImportSource("review.zip", () => new MemoryStream(zip))]);
        return new DocumentSession("semantic-r2", workspace.Set.AllDiskFiles.Single(), new CounterPlugin(), current,
            openOriginal: original, memberContext: memberContext);
    }

    private sealed class DuplicateNavigationDocument : BoundSaveDocument, IFieldMetadataDocument, IQueryableDocument
    {
        private int _value = 1;

        public DuplicateNavigationDocument()
        {
            RegisterField(new FieldMetadata("quantity", "Authoritative quantity", IntegerSpec.Int32),
                () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }

        public override string Title => "Duplicate navigation";
        public IReadOnlyList<QueryableCollection> Collections =>
            [new("rows", "Rows", [new("value", "Value", IntegerSpec.Int32)], 1)];

        public override ViewNode BuildView() => new SectionNode("Root", [
            Field("quantity", "Rendered summary"),
            new SectionNode("Rows", [new QueryableTableNode("rows", "Rows", [new("value", "Value", IntegerSpec.Int32)], 1)]),
        ]);

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
            new(collectionId, [new("value", "Value", IntegerSpec.Int32)],
                [new("row-1", new Dictionary<string, ViewNode>
                { ["value"] = Field("quantity", "Rendered row") })], 1, 1, 0, query.Limit);

        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new[] { (byte)_value })];
    }

    private sealed class LargeMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly int[] _values;

        public LargeMetadataDocument(int count)
        {
            _values = new int[count];
            for (var index = 0; index < count; index++)
            {
                var captured = index;
                RegisterField(new FieldMetadata($"field-{index:0000}", $"Field {index}", IntegerSpec.Int32)
                { CanonicalValueId = $"field-{index:0000}", Path = ["section:fields"] },
                    () => FieldValue.Of(_values[captured]), value => _values[captured] = value.AsInt32());
            }
        }

        public override string Title => "Large metadata";

        public override ViewNode BuildView() => new SectionNode("Fields",
            Enumerable.Range(0, _values.Length)
                .Select(index => (ViewNode)Field($"field-{index:0000}", $"Field {index}"))
                .ToArray());

        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", _values.Select(value => (byte)value).ToArray())];
    }
}
