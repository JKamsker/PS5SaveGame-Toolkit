using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Benchmarks;

internal sealed class BenchmarkFixture : IDisposable
{
    public const string ScratchVariable = "SAVETOOLKIT_BENCHMARK_SCRATCH";
    public const int PageSize = 50;
    public const string ValueColumn = "value";
    private readonly string _scratchRoot;
    private readonly string _root;

    public BenchmarkFixture(DataSize size, SaveFormat format)
    {
        var scratch = Environment.GetEnvironmentVariable(ScratchVariable)
            ?? throw new InvalidOperationException("Run through scripts/bench.ps1 to configure benchmark scratch.");
        _scratchRoot = Path.GetFullPath(scratch);
        _root = Path.Combine(_scratchRoot, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        try
        {
            Data = new SyntheticData(size, format);
            var set = Import(Path.Combine(_root, "workspace"));
            Workspace = new Workspace("benchmark", "Synthetic only", set, new PluginCatalog([Data.Plugin]));
            File = set.AllDiskFiles.First();
            QuerySession = OpenSession();
            QuerySession.BuildView();
            CollectionId = QuerySession.QueryableCollections.First(collection => collection.TotalCount == Data.RowCount).Id;
            var firstPage = QuerySession.QueryCollection(CollectionId, new() { Limit = 1 });
            var value = firstPage.Rows[0].Cells[ValueColumn];
            var scalar = value is FieldNode field ? field : ((SectionNode)value).Children.OfType<FieldNode>().Single();
            ValueId = scalar.ValueId;
            var number = FieldValue.ExactNumber("-1");
            var edited = scalar.Value.Kind == FieldValueKind.Union ? FieldValue.Union(scalar.Value.AsUnion().Type, number) : number;
            Edits = [new IncomingEdit(ValueId, JsonSerializer.SerializeToElement(edited))];
            ExportSession = Workspace.Open(File.Id, Data.Plugin.Info.Id);
            PrepareSession(ExportSession);
            Stage(ExportSession);
            SpoolPath = Path.Combine(_root, "export.zip");
            Verify();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public SyntheticData Data { get; } = null!;
    public Workspace Workspace { get; } = null!;
    public DiskSaveFile File { get; } = null!;
    public DocumentSession QuerySession { get; } = null!;
    public DocumentSession ExportSession { get; } = null!;
    public string CollectionId { get; } = "";
    public string ValueId { get; } = "";
    public IncomingEdit[] Edits { get; } = [];
    public string SpoolPath { get; } = "";

    public DiskSaveSet Import(string destination) => SaveSetImporter.Import(
        [new ImportSource("synthetic.zip", () => new MemoryStream(Data.ZipBytes, writable: false))], destination);

    public DocumentSession OpenSession() => new("benchmark", File, Data.Plugin, Data.Plugin.Open(File));

    public void PrepareSession(DocumentSession session)
    {
        session.BuildView();
        session.QueryCollection(CollectionId, new() { Limit = 1 });
    }

    public void Stage(DocumentSession session)
    {
        var result = session.ApplyEdits(Edits);
        if (!result.Ok) throw new InvalidOperationException("Synthetic edit was rejected: "
            + string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    public long Export()
    {
        var plan = WorkspaceExporter.Plan(Workspace, new ExportOptions { Scope = ExportScope.Everything });
        using var spool = new FileStream(SpoolPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        WorkspaceExporter.WriteZip(plan, spool);
        return spool.Length;
    }

    private void Verify()
    {
        if (Workspace.Set.AllDiskFiles.Count() != Data.SaveCount || !Data.Plugin.Probe(File).IsMatch)
            throw new InvalidOperationException("Synthetic import/probe mismatch.");
        var document = Data.Plugin.Open(File);
        using var clean = new DocumentSession("verify", File, Data.Plugin, document);
        if (!document.Serialize().Single().Content.Span.SequenceEqual(Data.SaveBytes))
            throw new InvalidOperationException("Synthetic no-op bytes changed.");
        PrepareSession(clean);
        var page = clean.QueryCollection(CollectionId, new() { Offset = Data.RowCount - PageSize, Limit = PageSize });
        if (page.Rows.Count != PageSize || page.TotalCount != Data.RowCount)
            throw new InvalidOperationException("Synthetic page mismatch.");
        Stage(clean);
        var changes = clean.GetChanges();
        if (changes.Changes.Count != 1 || changes.Changes[0].ValueId != ValueId)
            throw new InvalidOperationException("Synthetic semantic review did not find the staged edit.");
        if (changes.Complete != (Data.RowCount < DocumentSessionOptions.Default.MaxReviewQueryRows))
            throw new InvalidOperationException("Synthetic review completeness did not match its configured budget.");
        if (!clean.Undo().Ok || clean.State.IsDirty
            || !document.Serialize().Single().Content.Span.SequenceEqual(Data.SaveBytes))
            throw new InvalidOperationException("Synthetic undo did not restore the exact input.");
        Export();
        using var zip = ZipFile.OpenRead(SpoolPath);
        if (zip.Entries.Count != Data.SaveCount) throw new InvalidOperationException("Synthetic export count mismatch.");
        var entry = zip.GetEntry(File.RelativePath) ?? throw new InvalidOperationException("Edited export missing.");
        using var input = entry.Open();
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        if (!bytes.ToArray().AsSpan().SequenceEqual(ExportSession.Serialize().Single().Content.Span))
            throw new InvalidOperationException("Synthetic export lost the edit.");
    }

    public void Dispose()
    {
        QuerySession?.Dispose();
        Workspace?.Dispose();
        if (Path.GetDirectoryName(_root) != _scratchRoot)
            throw new InvalidOperationException("Refusing to delete benchmark data outside its scratch root.");
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
