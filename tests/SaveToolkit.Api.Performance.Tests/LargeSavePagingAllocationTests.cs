using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.EldenRing;
using SaveToolkit.Plugins.FinalFantasy7;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Api.Performance.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class LargeSavePagingAllocationTests(ITestOutputHelper output)
{
    private const int PageSize = 50;
    private const int MaxExpandedSections = 12;
    private const long CancellationOverheadBytes = 64 * 1024;

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA03169")]
    public void PagingAfterReviewPreservesThePageWithoutCopyingTheReviewBaseline(string title)
    {
        IGamePlugin plugin = title == "PPSA03169" ? new EldenRingPlugin() : new FinalFantasy7Plugin();
        var file = SampleSaves.Load(title).AllDiskFiles
            .Where(file => !file.Directory.IsBackup && plugin.Probe(file).IsMatch).MaxBy(file => file.Length);
        if (file is null) Assert.Skip($"Gameplay sample unavailable: {title}.");
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        var measurement = new ApiMeasurement(output, title + "-session");
        var opening = AllocationProbe.MeasureOpen(() => new DocumentSession("paging", file, plugin,
            plugin.Open(file, references), references: references), out var opened, session => session.BuildView());
        using var session = opened;
        measurement.Record("open-first-view", opening.MinimumBytes);
        // Bound discovery like the existing HTTP workflow probe; raw explorers can span the whole save.
        var expansions = 0;
        _ = ReviewSampleViews.Walk(session.BuildView(),
            lazy => expansions++ < MaxExpandedSections ? session.Expand(lazy) : [],
            new HashSet<string>(StringComparer.Ordinal)).Count();
        var collection = session.QueryableCollections.MaxBy(collection => collection.TotalCount);
        Assert.NotNull(collection);
        var query = new QueryableCollectionQuery
        {
            Offset = checked((int)Math.Max(0, collection.TotalCount - PageSize)), Limit = PageSize,
        };
        var before = JsonSerializer.SerializeToUtf8Bytes(session.QueryCollection(collection.Id, query));
        _ = session.GetChanges();
        var state = session.State;
        using var cancellation = new CancellationTokenSource();
        var uncancellable = AllocationProbe.Measure(() => session.QueryCollection(collection.Id, query), out _);
        var cancellable = AllocationProbe.Measure(() => session.QueryCollection(collection.Id, query,
            state.Revision, cancellation.Token), out var page);
        measurement.Describe($"{file.Length} bytes; {collection.Id}; {collection.TotalCount} rows");
        measurement.Record("page-after-review-no-token", uncancellable.MinimumBytes);
        measurement.Record("page-after-review-token", cancellable.MinimumBytes);
        cancellable.AssertAtMost(uncancellable.MinimumBytes + CancellationOverheadBytes,
            "Paging cancellation must not clone the unrelated semantic review baseline.");
        Assert.Equal(before, JsonSerializer.SerializeToUtf8Bytes(page));
        Assert.Equal(state, session.State);
    }
}
