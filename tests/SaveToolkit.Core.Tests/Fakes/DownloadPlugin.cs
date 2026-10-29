using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Fault injection for host download verification; no real plugin failure is implied.</summary>
internal sealed class DownloadPlugin : IGamePlugin
{
    private readonly CounterPlugin _inner = new();
    public PluginInfo Info => _inner.Info;
    public bool CanEdit { get; set; } = true;
    public int SerializeCalls { get; private set; }
    public int OpenCalls { get; private set; }
    public Action<ISaveFile, IReferenceContext>? OnOpen { get; set; }
    public Action? BeforeBuildView { get; set; }
    public Func<IReadOnlyList<FileOutput>, IReadOnlyList<FileOutput>>? Outputs { get; set; }
    public ProbeResult Probe(ISaveFile file) => _inner.Probe(file);
    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);
    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        OpenCalls++;
        OnOpen?.Invoke(file, references);
        return new DownloadDocument(this, _inner.Open(file));
    }

    private sealed class DownloadDocument(DownloadPlugin owner, ISaveDocument inner) : ISaveDocument
    {
        public string Title => inner.Title;
        public bool CanEdit => owner.CanEdit;
        public ViewNode BuildView()
        {
            owner.BeforeBuildView?.Invoke();
            return inner.BuildView();
        }
        public FieldValue GetValue(string valueId) => inner.GetValue(valueId);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => inner.ApplyEdits(edits);
        public IReadOnlyList<FileOutput> Serialize()
        {
            owner.SerializeCalls++;
            var outputs = inner.Serialize();
            return owner.Outputs?.Invoke(outputs) ?? outputs;
        }
        public void Dispose() => inner.Dispose();
    }
}
