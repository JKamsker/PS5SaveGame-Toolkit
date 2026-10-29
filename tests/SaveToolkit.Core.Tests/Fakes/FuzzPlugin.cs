using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Instrumented parser for exercising harness policy independently of real formats.</summary>
public sealed class FuzzPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.fuzz", "Fuzz harness test", "1.0.0");
    public bool Matches { get; init; } = true;
    public string? ThrowDuring { get; init; }
    public Func<Exception> Failure { get; init; } = () => new InvalidDataException();
    public Func<Exception>? DisposeFailure { get; init; }
    public int ProbeCalls { get; private set; }
    public int OpenCalls { get; private set; }
    public int ViewCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public List<string> Queries { get; } = [];
    public Action<ISaveFile>? InspectFile { get; init; }

    public ProbeResult Probe(ISaveFile file)
    {
        ProbeCalls++;
        InspectFile?.Invoke(file);
        ThrowIf("probe");
        return Matches ? ProbeResult.Match(ProbeScore.Title) : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file)
    {
        OpenCalls++;
        ThrowIf("open");
        return new Document(this);
    }

    private void ThrowIf(string stage)
    {
        if (ThrowDuring == stage) throw Failure();
    }

    private sealed class Document(FuzzPlugin plugin) : BoundSaveDocument, IQueryableDocument
    {
        public override string Title => "Synthetic fuzz document";
        public IReadOnlyList<QueryableCollection> Collections => [new("empty", "Empty", [], 0), new("items", "Items", [], 1000)];

        public override ViewNode BuildView()
        {
            plugin.ViewCalls++;
            plugin.ThrowIf("view");
            return new TextNode("Synthetic");
        }

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Assert.Equal(0, query.Offset);
            Assert.Null(query.Cursor);
            Assert.Equal(QueryableCollectionLimits.DefaultPageSize, query.Limit);
            Assert.True(query.IncludeEmpty);
            plugin.Queries.Add(collectionId);
            plugin.ThrowIf("query");
            return new(collectionId, [], [], 0, 0, 0, query.Limit);
        }

        public override IReadOnlyList<FileOutput> Serialize() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            plugin.DisposeCalls++;
            if (plugin.DisposeFailure is { } failure) throw failure();
            plugin.ThrowIf("dispose");
            base.Dispose(disposing);
        }
    }
}
