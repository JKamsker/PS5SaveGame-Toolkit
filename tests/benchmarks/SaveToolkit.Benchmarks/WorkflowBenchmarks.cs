using BenchmarkDotNet.Attributes;
using System.Diagnostics.CodeAnalysis;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Benchmarks;

// BenchmarkDotNet generates a subclass for its out-of-process runner, so this class cannot be sealed.
[MemoryDiagnoser]
[SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet owns the lifetime through GlobalCleanup and IterationCleanup.")]
public class WorkflowBenchmarks
{
    private BenchmarkFixture _fixture = null!;
    private DocumentSession? _session;
    private string _importPath = "";
    private QueryableCollectionQuery _pageQuery = null!;

    [Params(DataSize.Small, DataSize.Medium, DataSize.Large)]
    public DataSize Size { get; set; }

    [Params(SaveFormat.GenericJson, SaveFormat.XorJson)]
    public SaveFormat Format { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new BenchmarkFixture(Size, Format);
        _pageQuery = new() { Offset = _fixture.Data.RowCount / 2, Limit = BenchmarkFixture.PageSize };
        // Measure a warmed middle page, including session binding/validation.
        _fixture.QuerySession.QueryCollection(_fixture.CollectionId, _pageQuery);
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture?.Dispose();

    [IterationSetup(Target = nameof(ImportZip))]
    public void PrepareImport() => _importPath = Path.Combine(
        Environment.GetEnvironmentVariable(BenchmarkFixture.ScratchVariable)!, "import-" + Guid.NewGuid().ToString("N"));

    [IterationCleanup(Target = nameof(ImportZip))]
    public void CleanupImport()
    {
        var scratch = Path.GetFullPath(Environment.GetEnvironmentVariable(BenchmarkFixture.ScratchVariable)!);
        if (Path.GetDirectoryName(_importPath) != scratch)
            throw new InvalidOperationException("Refusing to delete an import outside benchmark scratch.");
        if (Directory.Exists(_importPath)) Directory.Delete(_importPath, recursive: true);
    }

    [Benchmark]
    public DiskSaveSet ImportZip() => _fixture.Import(_importPath);

    [Benchmark]
    public ViewNode OpenDocument()
    {
        using var session = _fixture.OpenSession();
        return session.BuildView();
    }

    [Benchmark]
    public QueryableCollectionPage PageTable() => _fixture.QuerySession.QueryCollection(_fixture.CollectionId, _pageQuery);

    [IterationSetup(Target = nameof(ApplyUndo))]
    public void PrepareSession()
    {
        _session = _fixture.OpenSession();
        _fixture.PrepareSession(_session);
    }

    [IterationSetup(Target = nameof(SemanticReview))]
    public void PrepareReview()
    {
        PrepareSession();
        _fixture.Stage(_session!);
    }

    [IterationCleanup(Targets = [nameof(SemanticReview), nameof(ApplyUndo)])]
    public void CleanupSession() => _session?.Dispose();

    [Benchmark]
    public SemanticChangeLog SemanticReview() => _session!.GetChanges();

    [Benchmark]
    public DocumentState ApplyUndo()
    {
        _fixture.Stage(_session!);
        var undone = _session!.Undo();
        if (!undone.Ok || undone.State.IsDirty) throw new InvalidOperationException("Undo failed.");
        return undone.State;
    }

    [Benchmark]
    public long ExportSpooled() => _fixture.Export();
}
