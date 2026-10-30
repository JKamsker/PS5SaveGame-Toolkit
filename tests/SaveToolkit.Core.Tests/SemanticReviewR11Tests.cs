using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR11Tests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Theory]
    [InlineData("none")]
    [InlineData("readonly")]
    [InlineData("range")]
    [InlineData("failure")]
    [InlineData("omission")]
    public void AnotherEditPublishesRecoveredOriginalIntoCurrentAuthority(string drift)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1, 1 }));
        using var workspace = _store.Create("recovery during B", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        using var current = new RecoveryDocument();
        var opens = 0;
        using var session = new DocumentSession("recovery-B", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, openOriginal: () => new RecoveryDocument { FailOriginalA = opens++ == 0 });
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([Edit("A", 2)]).Ok);
        Assert.True(session.ApplyEdits([Edit("B", 3)]).Ok);
        current.Drift = drift;
        var before = current.Serialize().Single().Content.ToArray();
        var revision = session.State.Revision;
        var review = session.GetChanges();
        var change = review.Changes.SingleOrDefault(change => change.ValueId == "A");

        if (drift is "none" or "readonly" or "range")
        {
            Assert.NotNull(change);
            Assert.True(change.OriginalExists);
            Assert.True(change.CurrentExists);
            Assert.Equal("1", change.OriginalValue.ToString());
            Assert.Equal("2", change.CurrentValue.ToString());
            Assert.Equal(drift == "none", change.CanRevert);
        }
        else Assert.True(change is null || !change.CanRevert);

        if (drift == "none")
        {
            Assert.True(review.Complete);
            Assert.True(session.RevertChange("A", revision).Ok);
            Assert.Equal(new byte[] { 1, 3 }, current.Serialize().Single().Content.ToArray());
        }
        else
        {
            Assert.False(review.Complete);
            Assert.NotEmpty(session.RevertChange("A", revision).Errors);
            Assert.Equal(revision, session.State.Revision);
            Assert.Equal(before, current.Serialize().Single().Content.ToArray());
            current.Drift = "none";
            Assert.True(session.Undo().Ok);
            Assert.Equal(new byte[] { 2, 1 }, current.Serialize().Single().Content.ToArray());
            Assert.True(session.Undo().Ok);
            Assert.Equal(new byte[] { 1, 1 }, current.Serialize().Single().Content.ToArray());
        }
    }

    [Fact]
    public void EnabledGroupStillRejectsMissingCanonicalDescriptors()
    {
        using var staged = new StagedGroupDocument(() => new MissingMetadataGroup(),
            candidate => candidate.Serialize(), () => { }, 4096);
        Assert.True(new FieldMetadataIndex(staged).Enabled);
        var field = new SemanticField("A", "A", [], FieldValue.Of(1), false, Spec: IntegerSpec.Int32);
        var snapshot = new SemanticSnapshot(new(StringComparer.Ordinal) { ["A"] = field }, true,
            SemanticSnapshot.Estimate(field));

        var outcome = SemanticTargetedAuthority.ApplyCanonicalMetadata(staged, snapshot, 4096, CancellationToken.None);

        Assert.False(outcome.Applied);
        Assert.Contains("A", outcome.IncompleteIds);
        Assert.Equal(field, snapshot.Fields["A"]);
    }

    [Fact]
    public void ReplacementPathIsChargedBeforeTheNextCaptureAndCancellation()
    {
        const long limit = 4096;
        using var document = new PathDocument();
        var a = new SemanticField("A", "A", [], FieldValue.Of(1), true);
        var snapshot = new SemanticSnapshot(new(StringComparer.Ordinal) { ["A"] = a }, true,
            SemanticSnapshot.Estimate(a));
        var capture = new SemanticTargetedCapture(limit);

        var first = capture.Capture(document, snapshot, ["A"], CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(3064, snapshot.Bytes);
        Assert.Equal(SemanticSnapshot.Estimate(snapshot.Fields["A"]), snapshot.Bytes);
        var before = snapshot.Clone();
        document.PathLengthA = 1900;
        var replacement = capture.Capture(document, snapshot, ["A"], CancellationToken.None);
        Assert.False(replacement.Succeeded);
        Assert.Equal(before.Bytes, snapshot.Bytes);
        Assert.Equal(before.Fields["A"].Path, snapshot.Fields["A"].Path);
        document.PathLengthA = 1400;
        var rejected = capture.Capture(document, snapshot, ["B"], CancellationToken.None);
        Assert.False(rejected.Succeeded);
        Assert.Equal(before.Bytes, snapshot.Bytes);
        Assert.Equal(before.Fields.Keys, snapshot.Fields.Keys);
        Assert.Equal(before.Fields["A"].Path, snapshot.Fields["A"].Path);
        Assert.Equal(before.Fields["A"].Spec, snapshot.Fields["A"].Spec);
        Assert.Equal(before.Fields["A"].Value, snapshot.Fields["A"].Value);
        Assert.Equal(before.Complete, snapshot.Complete);

        document.PathLengthB = 100;
        using var cancellation = new CancellationTokenSource();
        document.AfterResolve = () => cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => capture.Capture(document, snapshot, ["B"], cancellation.Token));
        Assert.Equal(before.Bytes, snapshot.Bytes);
        Assert.Single(snapshot.Fields);
        document.AfterResolve = null;
        var retry = capture.Capture(document, snapshot, ["B"], CancellationToken.None);
        Assert.True(retry.Succeeded);
        Assert.Equal(snapshot.Fields.Values.Sum(SemanticSnapshot.Estimate), snapshot.Bytes);
        Assert.InRange(snapshot.Bytes, 0, limit);
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));

    private sealed class RecoveryDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal) { ["A"] = 1, ["B"] = 1 };
        public bool FailOriginalA { get; init; }
        public string Drift { get; set; } = "none";

        public RecoveryDocument()
        {
            foreach (var id in _values.Keys) Register(id);
        }

        public override string Title => "Recovery";
        public override ViewNode BuildView() => new TextNode("Hidden A and B");
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_values["A"], (byte)_values["B"] })];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (valueIds.Contains("A", StringComparer.Ordinal) && (FailOriginalA || Drift == "failure"))
                throw new InvalidOperationException("A metadata unavailable");
            var ids = valueIds.Where(id => id != "A" || Drift != "omission").ToArray();
            foreach (var id in ids) Register(id);
            return base.ResolveFieldMetadata(ids);
        }

        private void Register(string id)
        {
            var spec = id == "A" && Drift == "range" ? new IntegerSpec(2, 10) : IntegerSpec.Int32;
            if (id == "A" && Drift == "readonly") spec = spec with { ReadOnly = true };
            RegisterField(new FieldMetadata(id, "Canonical " + id, spec), () => FieldValue.Of(_values[id]),
                spec.ReadOnly ? null : value => _values[id] = value.AsInt32());
        }
    }

    private sealed class MissingMetadataGroup : BoundSaveDocument, IGroupedSaveDocument, IFieldMetadataDocument
    {
        public override string Title => "Enabled metadata group";
        public override ViewNode BuildView() => new TextNode("No view");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public DocumentMemento CaptureMemento() => DocumentMemento.Snapshot(0, 1);
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) => [];
    }

    private sealed class PathDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public int PathLengthA { get; set; } = 1400;
        public int PathLengthB { get; set; } = 500;
        public Action? AfterResolve { get; set; }
        public PathDocument()
        {
            RegisterField("A", IntegerSpec.Int32, () => FieldValue.Of(1), _ => { });
            RegisterField("B", IntegerSpec.Int32, () => FieldValue.Of(1), _ => { });
        }

        public override string Title => "Replacement path";
        public override ViewNode BuildView() => new TextNode("No view");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            var fields = valueIds.Select(id => new FieldMetadata(id, id, IntegerSpec.Int32)
            {
                Path = [new string('p', id == "A" ? PathLengthA : PathLengthB)],
            }).ToArray();
            AfterResolve?.Invoke();
            return fields;
        }
    }
}
