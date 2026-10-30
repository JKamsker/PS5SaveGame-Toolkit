using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewR10Tests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Fact]
    public void RecoveredReportedFieldCannotRegainRevertAuthorityAfterCurrentRejection()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1 }));
        using var workspace = _store.Create("authority recovery", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        using var current = new RecoveryDocument();
        var opens = 0;
        using var session = new DocumentSession("authority-recovery", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, openOriginal: () => new RecoveryDocument { FailMetadata = opens++ == 0 });
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        var recovered = Assert.Single(session.GetChanges().Changes);
        Assert.True(recovered.CanRevert);

        current.ReadOnly = true;
        var revision = session.GetChanges().Revision;
        var rejected = Assert.Single(session.GetChanges().Changes);
        Assert.Equal("2", rejected.CurrentValue.ToString());
        Assert.False(rejected.CanRevert);
        var beforeBytes = current.Serialize().Single().Content.ToArray();
        var beforeRevision = session.GetChanges().Revision;

        var revert = session.RevertChange("hidden", revision);

        Assert.NotEmpty(revert.Errors);
        Assert.Equal(beforeRevision, revert.State.Revision);
        Assert.Equal(beforeBytes, current.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void OversizedPathIsRejectedBeforeColdWarmAndTargetedCopies()
    {
        using var cold = new PathMetadataDocument { Warm = true };
        var coldIndex = new FieldMetadataIndex(cold, 4_096);

        Assert.Throws<InvalidOperationException>(() => coldIndex.ResolveAll(["path"]));
        Assert.Equal(0, cold.GetterCalls);
        Assert.Equal(0, cold.Path.EnumerationCount);

        using var document = new PathMetadataDocument();
        var index = new FieldMetadataIndex(document, 4_096);
        index.ResolveAll(["path"]);
        Assert.NotNull(index.Cached("path"));
        document.Warm = true;
        Assert.Throws<InvalidOperationException>(() => index.ResolveAll(["path"]));
        Assert.NotNull(index.Cached("path"));
        Assert.Equal(1, document.GetterCalls);
        Assert.Equal(0, document.Path.EnumerationCount);

        var snapshot = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal), true, 0);
        var result = new SemanticTargetedCapture(4_096).Capture(document, snapshot, ["path"],
            CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(1, document.GetterCalls);
        Assert.Equal(0, document.Path.EnumerationCount);
    }

    [Fact]
    public void PathContainerAndNestedWrapperCostsHaveExactAdmissionBoundary()
    {
        var oversizedPath = Enumerable.Repeat(string.Empty, SemanticMetadataBudget.MaxPathSegments + 1).ToArray();
        var pathField = new SemanticField("path", "Path", oversizedPath, FieldValue.Of(1), false,
            CanonicalValueId: "path", Spec: IntegerSpec.Int32);
        Assert.False(SemanticSnapshot.TryEstimate(pathField, 4_096, out _));

        var nested = new SemanticField("nested", "Nested", [], FieldValue.Of(1), false,
            CanonicalValueId: "nested", Spec: new NullableSpec(IntegerSpec.Int32));
        Assert.True(SemanticSnapshot.TryEstimate(nested, long.MaxValue, out var cost));
        Assert.True(SemanticSnapshot.TryEstimate(nested, cost, out _));
        Assert.False(SemanticSnapshot.TryEstimate(nested, cost - 1, out _));
    }

    [Fact]
    public void AuthorityAndAggregateWarningsStayBoundedAndUnicodeSafe()
    {
        var id = new string('I', 100_000);
        var original = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            [id] = new SemanticField(id, "Original", [], FieldValue.Of(1), false,
                CanonicalValueId: id, Spec: IntegerSpec.Int32),
        }, true, 0);
        var current = new SemanticSnapshot(new Dictionary<string, SemanticField>(StringComparer.Ordinal)
        {
            [id] = new SemanticField(id, "Current", [], FieldValue.Of(2), true,
                CanonicalValueId: id, Spec: IntegerSpec.Int32 with { ReadOnly = true }),
        }, true, 0);

        SemanticTargetedAuthority.Validate(original, current,
            new ReferenceFieldService(ReferenceContext.None),
            retainRejectedIds: new HashSet<string>([id], StringComparer.Ordinal));
        var aggregate = new AggregateException(
            new InvalidOperationException(string.Concat(Enumerable.Repeat("😀", 400))),
            new InvalidOperationException(new string('C', 10_000)));
        var coverage = new SemanticReviewCoverageTracker();
        coverage.RecordOriginalFailures([], SemanticDiagnostics.Exception("capture failed", aggregate));

        var warnings = current.QueryWarnings.Concat(coverage.Diagnostics()).ToArray();
        Assert.NotEmpty(warnings);
        Assert.All(warnings, warning =>
        {
            Assert.InRange(warning.Length, 0, SemanticDiagnostics.MaxMessageChars);
            Assert.False(warning.Length > 0 && char.IsHighSurrogate(warning[^1]));
        });
        Assert.Contains(current.QueryWarnings, warning => warning.Contains(id[..8], StringComparison.Ordinal));
    }

    private sealed class PathMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public bool Warm { get; set; }
        public int GetterCalls { get; private set; }
        public CountingPath Path { get; } = new(1_000_000);

        public PathMetadataDocument() => RegisterField("path", IntegerSpec.Int32, () =>
        {
            GetterCalls++;
            return FieldValue.Of(_value);
        }, value => _value = value.AsInt32());

        public override string Title => "Path metadata";
        public override ViewNode BuildView() => new TextNode("No rendered metadata");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
            valueIds.Select(id => new FieldMetadata(id, "Path", IntegerSpec.Int32)
            {
                Path = Warm ? Path : ["small"],
            }).ToArray();
    }

    private sealed class RecoveryDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public bool FailMetadata { get; init; }
        public bool ReadOnly { get; set; }

        public RecoveryDocument() => RegisterField("hidden", IntegerSpec.Int32,
            () => FieldValue.Of(_value), value => _value = value.AsInt32());

        public override string Title => "Recovery authority";
        public override ViewNode BuildView() => new TextNode("No rendered metadata");
        public override IReadOnlyList<FileOutput> Serialize() =>
            [new("save.dat", BitConverter.GetBytes(_value))];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (FailMetadata && valueIds.Contains("hidden", StringComparer.Ordinal))
                throw new InvalidOperationException("transient original metadata failure");
            return valueIds.Select(id => new FieldMetadata(id, "Recovered hidden", IntegerSpec.Int32 with { ReadOnly = ReadOnly })
            {
                Path = ["canonical", "hidden"],
            }).ToArray();
        }
    }

    private sealed class CountingPath(int count) : IReadOnlyList<string>
    {
        public int Count { get; } = count;
        public int EnumerationCount { get; private set; }
        public string this[int index] => string.Empty;
        public IEnumerator<string> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Oversized path must be rejected before enumeration.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
