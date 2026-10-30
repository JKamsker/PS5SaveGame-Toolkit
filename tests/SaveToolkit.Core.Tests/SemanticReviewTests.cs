using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewTests : IDisposable
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin(), new RosterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(byte[] bytes, string plugin)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", bytes));
        var workspace = _store.Create("review", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin);
        _ = session.BuildView();
        return session;
    }

    [Fact]
    public void UnexposedReportedValues_AreTrackedAcrossUndoAndRedo()
    {
        using var session = Open(CounterPlugin.Create(5), "test.counter");
        Assert.True(session.InvokeAction("toggle-hidden", NoParameters).Ok);
        var change = session.GetChanges().Changes.Single(c => c.ValueId == "hidden.flag");
        Assert.Equal("false", change.OriginalValue.ToString());
        Assert.Equal("true", change.CurrentValue.ToString());
        Assert.Empty(change.Path);
        Assert.False(change.CanRevert);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.GetChanges().Changes);
        Assert.True(session.Redo().Ok);
        Assert.Contains(session.GetChanges().Changes, c => c.ValueId == "hidden.flag");
    }

    [Fact]
    public void ReadOnlyDerivedValueIsComparedEvenIfPluginDoesNotReportIt()
    {
        using var session = Open(CounterPlugin.Create(5), "test.counter");
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", JsonSerializer.SerializeToElement("7"))]).Ok);
        var log = session.GetChanges();
        Assert.Equal(2, log.Changes.Count);
        Assert.False(log.Changes.Single(c => c.ValueId == "checksum").CanRevert);
        Assert.True(session.RevertChange("counter", log.Revision).Ok);
        Assert.Empty(session.GetChanges().Changes);
    }

    [Fact]
    public void AddedFields_AreNotMistakenForOriginalValues_AfterFurtherEdits()
    {
        using var session = Open(RosterPlugin.Create((1, 5, "ann")), "test.roster");
        Assert.True(session.InvokeAction("add", new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("bob") }).Ok);
        Assert.True(session.ApplyEdits([new IncomingEdit("member/2/name", JsonSerializer.SerializeToElement("bobby"))]).Ok);
        Assert.True(session.GetChanges().Complete);
        var change = session.GetChanges().Changes.Single(c => c.ValueId == "member/2/name");
        Assert.False(change.OriginalExists);
        Assert.True(change.CurrentExists);
        Assert.Equal("bobby", change.CurrentValue.ToString());
        Assert.False(change.CanRevert);
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.GetChanges().Changes);
    }

    [Fact]
    public void EagerAndLazyReviewJsonRemainsByteIdentical()
    {
        using var session = Open(RosterPlugin.Create((1, 5, "ann")), "test.roster");
        _ = session.Expand("details/1");
        Assert.True(session.ApplyEdits([
            new IncomingEdit("member/1/name", JsonSerializer.SerializeToElement("anne")),
            new IncomingEdit("member/1/level", JsonSerializer.SerializeToElement("6")),
        ]).Ok);
        var expected = new
        {
            Changes = new object[]
            {
                new { ValueId = "member/1/name", Label = "Name", Path = new[] { "section:Roster", "row:m1" },
                    OriginalValue = FieldValue.Of("ann"), CurrentValue = FieldValue.Of("anne"),
                    OriginalExists = true, CurrentExists = true, CanRevert = true },
                new { ValueId = "member/1/level", Label = "Level", Path = new[] { "section:Roster", "section:details/1" },
                    OriginalValue = FieldValue.Of(5L), CurrentValue = FieldValue.Of(6L),
                    OriginalExists = true, CurrentExists = true, CanRevert = true },
            },
            Complete = true, Warnings = Array.Empty<string>(), Revision = 1L,
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(expected, options),
            JsonSerializer.SerializeToUtf8Bytes(session.GetChanges(), options));
    }

    [Fact]
    public void TargetedMetadataCapturesUnloadedOriginalWithCanonicalLabelAndPath()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1 }));
        using var workspace = _store.Create("targeted review", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        using var current = new TargetedMetadataDocument(1);
        TargetedMetadataDocument? original = null;
        using var session = new DocumentSession("targeted", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, openOriginal: () => original = new TargetedMetadataDocument(1, failView: true));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        var change = Assert.Single(session.GetChanges().Changes);
        Assert.Equal("Canonical quantity", change.Label);
        Assert.Equal(["section:canonical"], change.Path);
        Assert.True(change.CanRevert);
        Assert.Equal("1", change.OriginalValue.ToString());
        Assert.Equal("2", change.CurrentValue.ToString());
        Assert.Equal(1, original!.BuildViews);
    }

    [Fact]
    public void TargetedMetadataBudgetMarksReviewIncompleteWithoutInventingAChange()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1 }));
        using var workspace = _store.Create("targeted budget", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        using var current = new TargetedMetadataDocument(1);
        using var session = new DocumentSession("targeted-budget", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, new DocumentSessionOptions { MaxOriginalBytes = 256 },
            () => new TargetedMetadataDocument(1));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        var log = session.GetChanges();
        Assert.False(log.Complete);
        Assert.Empty(log.Changes);
    }

    [Fact]
    public void FailedLaterTargetedBatchDoesNotPublishAnEarlierBaselineBatch()
    {
        var review = new SemanticReview(new());
        using var first = new BatchCaptureDocument();
        review.CaptureTargetedOriginals(() => first, ["first"], CancellationToken.None);
        Assert.True(review.TryOriginal("first", out _));
        var beforeFailure = review.CaptureState();

        using var failing = new BatchCaptureDocument(failSecondBatch: true);
        review.CaptureTargetedOriginals(() => failing,
            Enumerable.Range(0, 201).Select(index => $"new{index}"), CancellationToken.None);
        Assert.True(review.TryOriginal("first", out _));
        Assert.False(review.TryOriginal("new0", out _));
        var afterFailure = review.CaptureState();
        Assert.NotNull(beforeFailure.Original);
        Assert.NotNull(afterFailure.Original);
        Assert.Equal(beforeFailure.Original.Bytes, afterFailure.Original!.Bytes);
        Assert.Equal(beforeFailure.Original.Complete, afterFailure.Original.Complete);
        Assert.Equal(beforeFailure.Original.QueryWarnings, afterFailure.Original.QueryWarnings);
        Assert.Equal(beforeFailure.TargetedIds, afterFailure.TargetedIds);
        Assert.Equal(beforeFailure.Original.Fields.Keys, afterFailure.Original.Fields.Keys);
        foreach (var id in beforeFailure.Original.Fields.Keys)
        {
            var beforeField = beforeFailure.Original.Fields[id];
            var afterField = afterFailure.Original.Fields[id];
            Assert.Equal(beforeField.Value, afterField.Value);
            Assert.Equal(beforeField.Path, afterField.Path);
            Assert.Equal(beforeField.Label, afterField.Label);
            Assert.Equal(beforeField.ReadOnly, afterField.ReadOnly);
        }
        Assert.NotEmpty(afterFailure.CaptureDiagnostics);
        Assert.False(review.Query(first, _ => { }, _ => { }, 1).Complete);

        using var retry = new BatchCaptureDocument();
        review.CaptureTargetedOriginals(() => retry,
            Enumerable.Range(0, 201).Select(index => $"new{index}"), CancellationToken.None);
        Assert.True(review.TryOriginal("new0", out _));
    }

    [Fact]
    public void MissingTargetedMetadataReportsTheKnownOmissionCount()
    {
        var review = new SemanticReview(new());
        using var document = new MissingMetadataDocument();

        review.CaptureTargetedOriginals(() => document, ["missing-a", "missing-b"], CancellationToken.None);
        var state = review.CaptureState();
        Assert.NotNull(state.Original);
        var log = review.Query(() => document, document, _ => { }, _ => { }, 1, CancellationToken.None);
        Assert.False(log.Complete);
        Assert.Equal(2, log.Coverage?.OmittedCount);
        Assert.False(log.Coverage?.OmittedCountIsLowerBound);
    }

    [Fact]
    public void CurrentTargetedMetadataResolvesBeforeAFailingViewWalk()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1 }));
        using var workspace = _store.Create("current-targeted-first", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        using var current = new OrderingDocument();
        using var session = new DocumentSession("current-targeted-first", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, openOriginal: () => new OrderingDocument());
        _ = session.BuildView();
        current.Events.Clear();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        current.Events.Clear();
        current.FailView = true;

        var review = session.GetChanges();
        Assert.False(review.Complete);
        Assert.Equal("metadata", current.Events.First());
        Assert.Contains("view", current.Events);
        Assert.Equal("2", Assert.Single(review.Changes, change => change.ValueId == "hidden").CurrentValue.ToString());
    }

    [Fact]
    public void FailedOriginalWalkDoesNotHideStructuralAdditionsAfterASecondEdit()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1 }));
        using var workspace = _store.Create("structural-incomplete", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        using var current = new StructuralReviewDocument();
        using var session = new DocumentSession("structural-incomplete", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, openOriginal: () => new StructuralReviewDocument(failView: true));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("value", JsonSerializer.SerializeToElement(2))]).Ok);
        Assert.True(session.InvokeAction("add", NoParameters).Ok);
        var secondEdit = session.ApplyEdits([new IncomingEdit("value", JsonSerializer.SerializeToElement(3))]);
        if (!secondEdit.Ok) throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(secondEdit));

        var review = session.GetChanges();
        Assert.False(review.Complete);
        Assert.Contains(review.Warnings, warning => warning.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TargetedMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value;
        private readonly bool _failView;
        public int BuildViews { get; private set; }

        public TargetedMetadataDocument(int value = 1, bool failView = false)
        {
            _value = value;
            _failView = failView;
            RegisterField(new FieldMetadata("hidden", "Canonical quantity", IntegerSpec.Int32)
            {
                Path = ["section:canonical"],
                Presentation = new FieldPresentation { Confidence = Confidence.Inferred, Warning = "Review before exporting." },
            }, () => FieldValue.Of(_value), field => _value = field.AsInt32());
        }

        public override string Title => "Targeted metadata";
        public override ViewNode BuildView()
        {
            BuildViews++;
            if (_failView) throw new InvalidOperationException("Unrelated original view failure.");
            return new TextNode("No rendered fields");
        }
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];
    }

    private sealed class BatchCaptureDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly bool _failSecondBatch;

        public BatchCaptureDocument(bool failSecondBatch = false)
        {
            _failSecondBatch = failSecondBatch;
            RegisterField(new FieldMetadata("first", "First", IntegerSpec.Int32), () => FieldValue.Of(1), _ => { });
            for (var index = 0; index < 201; index++)
                RegisterField(new FieldMetadata($"new{index}", $"new{index}", IntegerSpec.Int32),
                    () => FieldValue.Of(index), _ => { });
        }

        public override string Title => "Batch capture";
        public override ViewNode BuildView() => new TextNode("No fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            if (_failSecondBatch && ids.Contains("new200", StringComparer.Ordinal))
                throw new InvalidOperationException("resolver failed in second batch");
            return base.ResolveFieldMetadata(ids);
        }
    }

    private sealed class StructuralReviewDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        private bool _added;
        private readonly bool _failView;

        public StructuralReviewDocument(bool failView = false)
        {
            _failView = failView;
            RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32),
                () => FieldValue.Of(_value), value => _value = value.AsInt32());
        }

        public override string Title => "Structural review";
        public override ViewNode BuildView()
        {
            if (_failView) throw new InvalidOperationException("Original view unavailable.");
            RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32),
                () => FieldValue.Of(_value), value => _value = value.AsInt32());
            return new SectionNode("Review", [Field("value", "Value"), new ActionNode("add", "Add")]);
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            actionId == "add" ? Structural(() => _added = true, "Added") : throw new KeyNotFoundException(actionId);

        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot((_value, _added), 8);
        protected override void LoadSnapshot(DocumentMemento snapshot) => (_value, _added) =
            ((SnapshotMemento<(int Value, bool Added)>)snapshot).State;
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids) =>
            base.ResolveFieldMetadata(ids);
    }

    private sealed class OrderingDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public List<string> Events { get; } = [];
        public bool FailView { get; set; }

        public OrderingDocument() => RegisterField(new FieldMetadata("hidden", "Hidden", IntegerSpec.Int32),
            () => FieldValue.Of(_value), value => _value = value.AsInt32());

        public override string Title => "Ordering";
        public override ViewNode BuildView()
        {
            Events.Add("view");
            if (FailView) throw new InvalidOperationException("Current view unavailable.");
            return new TextNode("No rendered fields");
        }

        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            Events.Add("metadata");
            return base.ResolveFieldMetadata(ids);
        }
    }

    private sealed class MissingMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public MissingMetadataDocument() => RegisterField(new FieldMetadata("known", "Known", IntegerSpec.Int32),
            () => FieldValue.Of(1), _ => { });

        public override string Title => "Missing metadata";
        public override ViewNode BuildView() => new TextNode("No fields");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids) =>
            base.ResolveFieldMetadata(ids.Where(id => id == "known").ToArray());
    }
}
