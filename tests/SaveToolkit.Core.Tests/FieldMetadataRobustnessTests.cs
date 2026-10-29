using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldMetadataRobustnessTests
{
    [Theory]
    [InlineData("Quantity")]
    [InlineData("quantity/unknown")]
    [InlineData("alias/missing")]
    public void UnknownCanonicalIdRejectsTheEntireMixedEditBatch(string id)
    {
        using var fixture = new MetadataSessionFixture();
        var before = fixture.Document.Serialize().Single().Content.ToArray();
        var result = fixture.Session.ApplyEdits([Edit("quantity", 7), Edit(id, 8)]);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.ValueId == id);
        Assert.Empty(fixture.Document.ResolveFieldMetadata([id]));
        Assert.Equal(before, fixture.Document.Serialize().Single().Content.ToArray());
        Assert.Equal(0, fixture.Document.Writes);
        Assert.False(result.State.CanUndo);
        Assert.False(result.State.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DerivedAliasAndAliasOfAliasCannotBeWrittenOrReplayed(bool secondAlias)
    {
        using var fixture = new AliasFixture();
        var id = secondAlias ? "alias/second" : "alias/first";
        Assert.False(fixture.Session.ApplyEdits([Edit("primary", 7), Edit(id, 8)]).Ok);
        Assert.Equal(2, fixture.Document.Value);
        Assert.Equal(0, fixture.Document.ApplyCalls);
        Assert.False(fixture.Document.ApplyEdits([new(id, FieldValue.Of(8))]).Succeeded);
        Assert.Equal(2, fixture.Document.Value);
        Assert.True(fixture.Session.ApplyEdits([Edit("primary", 7)]).Ok);
        Assert.Equal(7, fixture.Document.GetValue("alias/second").AsInt32());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(2, fixture.Document.GetValue("alias/second").AsInt32());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(7, fixture.Document.Value);
        Assert.Equal(3, fixture.Document.SetterCalls);
    }

    [Fact]
    public void PluginReportedAliasCycleRejectsPreflightBeforeAnyMutation()
    {
        using var fixture = new AliasFixture(cycle: true);
        var result = fixture.Session.ApplyEdits([Edit("primary", 7), Edit("alias/first", 8)]);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Message.Contains("Alias getter cycle", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Document.ApplyCalls);
        Assert.Equal(0, fixture.Document.SetterCalls);
        Assert.Equal(2, fixture.Document.Value);
        Assert.False(result.State.CanUndo);
    }

    [Fact]
    public void AliasGetterWithAnUnknownTargetRejectsPreflightBeforeAnyMutation()
    {
        using var fixture = new AliasFixture(unknownTarget: true);
        var result = fixture.Session.ApplyEdits([Edit("primary", 7), Edit("alias/second", 8)]);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Message.Contains("Unknown field 'missing'", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Document.ApplyCalls);
        Assert.Equal(0, fixture.Document.SetterCalls);
        Assert.Equal(2, fixture.Document.Value);
        Assert.False(result.State.CanUndo);
    }

    [Theory]
    [InlineData("range", false)]
    [InlineData("range", true)]
    [InlineData("readonly", false)]
    [InlineData("readonly", true)]
    [InlineData("hint", false)]
    [InlineData("hint", true)]
    public void ConflictingPublishedSpecIsRejectedInEitherDiscoveryOrder(string conflict, bool viewFirst)
    {
        using var document = new AliasDocument(false);
        var spec = conflict switch
        {
            "range" => new IntegerSpec(0, 9),
            "readonly" => AliasDocument.PrimarySpec with { ReadOnly = true },
            "hint" => AliasDocument.PrimarySpec with { Unit = "Different unit" },
            _ => throw new ArgumentOutOfRangeException(nameof(conflict)),
        };
        var index = new ViewIndex(document);
        var node = new FieldNode("primary", "Projection", spec, FieldValue.Of(2));
        if (viewFirst)
        {
            // A view-only publication can become canonical later in the same document.
            document.PublishMetadata = false;
            index.Add(node);
            document.PublishMetadata = true;
            Assert.Throws<InvalidOperationException>(() => index.Spec("primary"));
        }
        else
        {
            _ = index.Spec("primary");
            Assert.Throws<InvalidOperationException>(() => index.Add(node));
        }
        Assert.Equal(0, document.SetterCalls);
    }

    [Fact]
    public void DerivedMetadataCannotAcquireAnIndependentSetterThroughStorageReplacement()
    {
        using var document = new AliasDocument(false);
        document.ReplaceDerivedStorageWithSetter();
        Assert.Throws<InvalidOperationException>(() => document.ResolveFieldMetadata(["alias/first"]));
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));

    private sealed class AliasFixture : IDisposable
    {
        private readonly WorkspaceStore _store = new(new PluginCatalog([new PlanningPlugin()]),
            new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        public AliasDocument Document { get; }
        public DocumentSession Session { get; }

        public AliasFixture(bool cycle = false, bool unknownTarget = false)
        {
            Document = new(cycle, unknownTarget);
            var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 2 }));
            var workspace = _store.Create("aliases", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
            Session = new("aliases", workspace.Set.AllDiskFiles.Single(), new PlanningPlugin(), Document,
                openOriginal: () => new AliasDocument(cycle, unknownTarget));
        }

        public void Dispose() { Session.Dispose(); _store.Dispose(); }
    }

    private sealed class AliasDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public static readonly IntegerSpec PrimarySpec = new(0, 10);
        private static readonly string[] AliasIds = ["alias/first", "alias/second"];
        private static readonly IntegerSpec DerivedSpec = PrimarySpec with { ReadOnly = true };
        private readonly HashSet<string> _activeGetters = new(StringComparer.Ordinal);
        public int Value { get; private set; } = 2;
        public int SetterCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public bool PublishMetadata { get; set; } = true;
        public override string Title => "Alias fixture";

        public AliasDocument(bool cycle, bool unknownTarget = false)
        {
            RegisterField(new FieldMetadata("primary", "Primary", PrimarySpec), () => FieldValue.Of(Value),
                value => { Value = value.AsInt32(); SetterCalls++; });
            RegisterField(new FieldMetadata("alias/first", "First alias", DerivedSpec) { BindingKind = FieldBindingKind.Derived },
                () => ReadAlias("alias/first", cycle ? "alias/second" : unknownTarget ? "missing" : "primary"));
            RegisterField(new FieldMetadata("alias/second", "Second alias", DerivedSpec) { BindingKind = FieldBindingKind.Derived },
                () => ReadAlias("alias/second", "alias/first"));
        }

        // SDK-02 has no alias-target property or graph walker. The plugin must detect getter cycles;
        // this fixture bounds recursion so the test can verify Core contains the reported failure.
        private FieldValue ReadAlias(string id, string target)
        {
            if (!_activeGetters.Add(id)) throw new InvalidOperationException("Alias getter cycle.");
            try { return GetValue(target); }
            finally { _activeGetters.Remove(id); }
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids) =>
            PublishMetadata ? base.ResolveFieldMetadata(ids) : [];
        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) { ApplyCalls++; return base.ApplyEdits(edits); }
        public override ViewNode BuildView() => new TextNode("No fields rendered");
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)Value })];
        protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied) =>
            applied.SelectMany(change => AliasIds
                .Select(id => new ValueChange(id, change.OldValue, change.NewValue)));
        public void ReplaceDerivedStorageWithSetter() => RegisterField("alias/first", DerivedSpec,
            () => FieldValue.Of(Value), value => { Value = value.AsInt32(); SetterCalls++; });
    }
}
