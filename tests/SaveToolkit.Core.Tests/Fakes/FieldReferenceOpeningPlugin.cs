using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

/// <summary>Test-only fault switches exercise failed eager responses without private saves.</summary>
internal sealed class FieldReferenceOpeningPlugin(string id) : IGamePlugin, IDocumentGroupPlugin
{
    public PluginInfo Info { get; } = new(id, "Reference opening", "1.0.0");
    public bool OversizedSingle { get; set; }
    public bool OversizedGroup { get; set; }
    public List<Document> Opened { get; } = [];
    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Title);
    public ISaveDocument Open(ISaveFile file) => Create([file], OversizedSingle);
    public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) =>
        [new("pair", "Pair", [new("first", anchor.RelativePath), new("second", anchor.Sibling("second.dat")!.RelativePath)])];
    public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references) => [];
    public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references) =>
        Create(input.Members.Select(member => member.File).ToArray(), OversizedGroup);

    private Document Create(IReadOnlyList<ISaveFile> files, bool oversized)
    {
        var document = new Document(files, oversized);
        Opened.Add(document);
        return document;
    }

    internal sealed class Document(IReadOnlyList<ISaveFile> files, bool oversized) : BoundSaveDocument, IGroupedSaveDocument
    {
        private readonly string _text = oversized ? new string('"', 4000) : "exact value";
        public bool IsDisposed { get; private set; }
        public int Views { get; private set; }
        public override string Title => "Opening candidate";
        public override ViewNode BuildView()
        {
            Views++;
            return new SectionNode(Title, [Field("text", "Canonical", new TextSpec { ReadOnly = true }, () => FieldValue.Of(_text)),
                .. FieldReferenceBudgetDocument.References(200)]);
        }
        public override IReadOnlyList<FileOutput> Serialize() => files.Select(file => new FileOutput(file.RelativePath, file.ReadAllBytes())).ToArray();
        public DocumentMemento CaptureMemento() => DocumentMemento.Snapshot(0, sizeof(int));
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
    }
}
