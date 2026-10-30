using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class CleanReplacementLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCleanOwnerCleanup_RemovesDeadOwnerAndDisposesUnpublishedCandidate(bool groupReplacement)
    {
        var inner = new GroupPlugin();
        var plugin = new FailingCleanupPlugin(inner);
        var alternate = new AlternatePlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin, alternate]), new() { DataRoot = TestZips.TempDir() });
        const string root = "PPSA99990/sdimg_SAVE/";
        var workspace = store.Create("failed clean replacement",
        [
            new ImportSource(root + "character.dat", () => new MemoryStream(GroupPlugin.Create("C", 10))),
            new ImportSource(root + "profile.dat", () => new MemoryStream(GroupPlugin.Create("P", 20))),
        ]);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat");
        var original = workspace.Open(anchor.Id, plugin.Info.Id);
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, plugin.Info.Id));

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (groupReplacement) workspace.OpenGroup(offer.Token, original.Id);
            else workspace.Open(anchor.Id, AlternatePlugin.Id);
        });

        Assert.Throws<ObjectDisposedException>(() => original.BuildView());
        Assert.Empty(workspace.Documents);
        Assert.Empty(workspace.DocumentGroups);
        Assert.All(inner.Opened, document => Assert.True(document.IsDisposed));
        if (!groupReplacement) Assert.True(Assert.Single(alternate.Opened).IsDisposed);

        // A failed cleanup must neither consume the confirmation nor leave a dead path owner.
        var retry = workspace.OpenGroup(offer.Token);
        Assert.Same(retry.Session, Assert.Single(workspace.Documents));
        Assert.Single(inner.Opened, document => !document.IsDisposed);
    }

    private sealed class FailingCleanupPlugin(GroupPlugin inner) : IGamePlugin, IDocumentGroupPlugin
    {
        private int _opens;
        public PluginInfo Info => inner.Info;
        public ProbeResult Probe(ISaveFile file) => inner.Probe(file);
        public ISaveDocument Open(ISaveFile file) => Interlocked.Increment(ref _opens) == 1
            ? new FailingCleanupDocument() : inner.Open(file);
        public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) => inner.ProposeGroups(anchor);
        public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references) => inner.ValidateGroup(input, references);
        public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references) => inner.OpenGroup(input, references);
    }

    private sealed class FailingCleanupDocument : BoundSaveDocument
    {
        public override string Title => "Failing cleanup";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw new InvalidOperationException("Injected member cleanup failure.");
        }
    }

    private sealed class AlternatePlugin : IGamePlugin
    {
        public const string Id = "test.alternate";
        public PluginInfo Info { get; } = new(Id, "Alternate", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public List<CandidateDocument> Opened { get; } = [];
        public ISaveDocument Open(ISaveFile file)
        {
            var document = new CandidateDocument();
            Opened.Add(document);
            return document;
        }
    }

    private sealed class CandidateDocument : BoundSaveDocument
    {
        public bool IsDisposed { get; private set; }
        public override string Title => "Valid candidate";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
    }
}
