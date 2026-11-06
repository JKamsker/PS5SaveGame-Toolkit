using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

/// <summary>
/// A review walk registers every visited field, and each registration resolves field metadata.
/// That resolution used to copy and rebuild the whole metadata cache, so one review of a metadata
/// document was quadratic in its field count (Gearbox: 40 s natively, 190 s in WebAssembly, for a
/// 304-byte save). Allocations are deterministic, so the gate is their growth, not wall time.
/// </summary>
[Collection(typeof(AllocationTestCollection))]
public sealed class MetadataReviewScalingTests(ITestOutputHelper output) : IDisposable
{
    private const int Small = 500;
    private const int Large = 4 * Small;
    // Linear work grows 4x for 4x the fields and an O(n log n) cache a little more; the quadratic
    // walk grew 14.5x (3.9x after the fix). Allow generous headroom for fixed costs without admitting n^2.
    private const double MaxGrowth = 7;
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Fact]
    public void ReviewAllocationsGrowLinearlyWithMetadataFieldCount()
    {
        var small = MeasureReview(Small);
        var large = MeasureReview(Large);
        var growth = (double)large / small;
        output.WriteLine($"{Small} fields: {small} B; {Large} fields: {large} B; growth {growth:F2}x");
        Assert.True(growth < MaxGrowth,
            $"One review allocated {growth:F2}x as much for {Large / Small}x the fields ({small} B -> {large} B); expected near-linear growth.");
    }

    [Fact]
    public void ReviewOfLargeMetadataDocumentReportsTheEditOnly()
    {
        using var session = Open(Large, out var document);
        Assert.True(session.ApplyEdits([new IncomingEdit("registered/0", JsonSerializer.SerializeToElement(8))]).Ok);
        Assert.True(session.ApplyEdits([new IncomingEdit("registered/1", JsonSerializer.SerializeToElement(9))]).Ok);
        var resolvedBefore = document.ResolvedIds;
        var started = Stopwatch.GetTimestamp();
        var log = session.GetChanges(TestContext.Current.CancellationToken);
        output.WriteLine($"{Large} fields: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms (diagnostic only)");
        // Legacy fields publish no descriptor, so coverage is honestly reported incomplete; the
        // proven published edits must still be listed exactly.
        Assert.Contains(log.Warnings, warning => warning.Contains("no descriptor was published for 'legacy/", StringComparison.Ordinal));
        Assert.Equal(["registered/0", "registered/1"], log.Changes.Select(change => change.ValueId).Order(StringComparer.Ordinal));
        // Plugin work: each rendered field is resolved a bounded number of times per review.
        Assert.InRange(document.ResolvedIds - resolvedBefore, 1, 8L * 2 * Large);
    }

    private long MeasureReview(int fields)
    {
        DocumentSession? session = null;
        var result = AllocationProbe.Measure(() => _ = session!.GetChanges(TestContext.Current.CancellationToken),
            warmupRuns: 1, measuredRuns: 3, beforeRun: () =>
            {
                session = Open(fields, out _);
                Assert.True(session.ApplyEdits([new IncomingEdit("registered/0", JsonSerializer.SerializeToElement(8))]).Ok);
            }, afterRun: () => session?.Dispose());
        return result.MinimumBytes;
    }

    private DocumentSession Open(int fields, out ScaledMetadataDocument current)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)));
        var workspace = _store.Create("metadata-scaling", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        current = new ScaledMetadataDocument(fields);
        var session = new DocumentSession("metadata-scaling", workspace.Set.AllDiskFiles.Single(), new CounterPlugin(), current,
            openOriginal: () => new ScaledMetadataDocument(fields));
        _ = session.BuildView();
        return session;
    }

    /// <summary>Published (registered) and legacy (view-only) editable fields, as metadata plugins mix them.</summary>
    private sealed class ScaledMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly int[] _registered;
        private readonly int[] _legacy;

        public ScaledMetadataDocument(int fields)
        {
            _registered = new int[fields];
            _legacy = new int[fields];
            Bind();
        }

        public override string Title => "Scaled metadata";
        public long ResolvedIds { get; private set; }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            ResolvedIds += valueIds.Count;
            return base.ResolveFieldMetadata(valueIds);
        }

        public override ViewNode BuildView() => new SectionNode(Title,
        [
            new SectionNode("Published", [.. Enumerable.Range(0, _registered.Length).Select(i => Field($"registered/{i}", $"Published {i}"))]),
            new SectionNode("Legacy", [.. Enumerable.Range(0, _legacy.Length).Select(i => Field($"legacy/{i}", $"Legacy {i}",
                new IntegerSpec(0, 100), () => FieldValue.Of(_legacy[i]), value => _legacy[i] = value.AsInt32()))]),
        ]);

        public override IReadOnlyList<FileOutput> Serialize() => [];

        private void Bind()
        {
            for (var i = 0; i < _registered.Length; i++)
            {
                var index = i;
                RegisterField(new FieldMetadata($"registered/{i}", $"Published {i}", new IntegerSpec(0, 100)),
                    () => FieldValue.Of(_registered[index]), value => _registered[index] = value.AsInt32());
            }
        }
    }
}
