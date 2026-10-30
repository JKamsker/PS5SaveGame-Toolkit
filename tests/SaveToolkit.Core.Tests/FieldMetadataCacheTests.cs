using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class FieldMetadataCacheTests(ITestOutputHelper output)
{
    private const int CachedFields = 5_000;
    private const long LookupAllocationLimit = 32 * 1024;
    private const int InitialFields = FieldMetadataIndex.BatchSize + 2;

    [Fact]
    public void RefreshingOneDescriptorDoesNotCopyFiveThousandUnrelatedDescriptors()
    {
        using var document = new CacheDocument(CachedFields);
        var index = new FieldMetadataIndex(document);
        var ids = Enumerable.Range(0, CachedFields).Select(Id).ToArray();
        foreach (var batch in ids.Chunk(FieldMetadataIndex.BatchSize)) index.ResolveAll(batch);
        var first = index.Cached(ids[0]);
        var allocated = AllocationProbe.Measure(() => index.ResolveAll([ids[^1]]));

        output.WriteLine($"One metadata refresh with {CachedFields} cached fields: {allocated.MinimumBytes} B.");
        allocated.AssertAtMost(LookupAllocationLimit, "Refreshing one descriptor must stage only its metadata closure.");
        Assert.Equal(CachedFields, index.Ids.Count());
        Assert.Same(first, index.Cached(ids[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedLaterBatchPublishesNoEarlierReplacementsRemovalsOrNewDescriptors(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var document = new CacheDocument(InitialFields + 1);
        var index = new FieldMetadataIndex(document);
        var initialIds = Enumerable.Range(0, InitialFields).Select(Id).ToArray();
        index.ResolveAll(initialIds);
        var previous = initialIds.Select(index.Cached).ToArray();
        document.LabelPrefix = "Updated ";
        document.MissingId = Id(1);
        document.OnGet = id =>
        {
            if (id != Id(InitialFields)) return;
            if (cancel) cancellation.Cancel();
            else throw new InvalidOperationException("Later descriptor getter failed.");
        };
        var allIds = initialIds.Append(Id(InitialFields)).ToArray();
        if (cancel) Assert.Throws<OperationCanceledException>(() => index.ResolveAll(allIds, cancellation.Token));
        else Assert.Throws<InvalidOperationException>(() => index.ResolveAll(allIds));

        Assert.Equal(InitialFields, index.Ids.Count());
        for (var i = 0; i < initialIds.Length; i++) Assert.Same(previous[i], index.Cached(initialIds[i]));
        Assert.Null(index.Cached(Id(InitialFields)));

        document.OnGet = null;
        index.ResolveAll(allIds);
        Assert.Null(index.Cached(Id(1)));
        Assert.Equal("Updated " + Id(InitialFields), index.Cached(Id(InitialFields))!.Label);
        Assert.Equal(InitialFields, index.Ids.Count());
    }

    private static string Id(int index) => "field/" + index;

    private sealed class CacheDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private static readonly IntegerSpec Spec = IntegerSpec.Int32 with { ReadOnly = true };
        public string LabelPrefix { get; set; } = "Original ";
        public string? MissingId { get; set; }
        public Action<string>? OnGet { get; set; }
        public override string Title => "Metadata cache fixture";

        public CacheDocument(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var id = Id(i);
                var value = FieldValue.Of(i);
                RegisterField(new FieldMetadata(id, id, Spec), () => { OnGet?.Invoke(id); return value; });
            }
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
            valueIds.Where(id => id != MissingId).Select(id => new FieldMetadata(id, LabelPrefix + id, Spec)
            { CanonicalValueId = id }).ToArray();

        public override ViewNode BuildView() => new SectionNode(Title, []);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
