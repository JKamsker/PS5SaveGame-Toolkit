using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticMemberContextCloseoutTests
{
    [Fact]
    public void MixedCanonicalAndLegacyRowsRequestOnlyAdmittedCanonicalContext()
    {
        var canonical = Change("canonical") with { CanonicalValueId = "canonical" };
        var legacy = Change("legacy");
        string[]? requested = null;
        var context = new SemanticMemberContext("character", "character.dat");

        var result = SemanticMemberContextResolver.Resolve(ids =>
        {
            requested = ids.ToArray();
            return new Dictionary<string, SemanticMemberContext> { ["canonical"] = context };
        }, [canonical, legacy], 0, SemanticContextBudget.MaxResponseBytes, CancellationToken.None);

        Assert.Equal(["canonical"], requested);
        Assert.Equal(context, Assert.Single(result.Contexts).Value);
        Assert.True(result.Complete);
        Assert.Equal(0, result.OmittedCount);
        Assert.Null(result.Diagnostic);
    }

    [Fact]
    public void MixedSessionReviewRetainsBothProvenTransitionsAndPartialCoverage()
    {
        using var store = Store(new CounterPlugin());
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)));
        var workspace = Import(store, zip);
        var requests = new List<string[]>();
        using var session = new DocumentSession("mixed-context", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), new MixedDocument(), openOriginal: () => new MixedDocument(),
            memberContext: ids =>
            {
                requests.Add(ids.ToArray());
                return ids.ToDictionary(id => id, _ => new SemanticMemberContext("character", "character.dat"));
            });
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new("canonical", JsonSerializer.SerializeToElement(2))]).Ok);

        var review = session.GetChanges();

        Assert.False(review.Complete);
        Assert.Equal(2, review.Changes.Count);
        var primary = Assert.Single(review.Changes, change => change.ValueId == "canonical");
        var legacy = Assert.Single(review.Changes, change => change.ValueId == "legacy");
        Assert.Equal(1, primary.OriginalValue.AsInt32());
        Assert.Equal(2, primary.CurrentValue.AsInt32());
        Assert.Equal("character", primary.Member?.Role);
        Assert.Equal(2, legacy.OriginalValue.AsInt32());
        Assert.Equal(4, legacy.CurrentValue.AsInt32());
        Assert.Null(legacy.CanonicalValueId);
        Assert.Null(legacy.Member);
        Assert.Equal(["canonical"], Assert.Single(requests));
        Assert.NotEmpty(review.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedGroupSuffixRetainsCompleteValidPrefixWithoutInventingOmissions(bool foreign)
    {
        const string root = "PPSA99990/sdimg_SAVE/";
        var plugin = new GroupPlugin
        {
            ContextResponse = contexts => [.. contexts,
                contexts[0] with { ValueId = foreign ? "foreign" : contexts[0].ValueId }],
        };
        using var store = Store(plugin);
        var workspace = Import(store, TestZips.Build(
            (root + "character.dat", GroupPlugin.Create("C", 10)),
            (root + "profile.dat", GroupPlugin.Create("P", 20))));
        var file = workspace.Set.AllDiskFiles.Single(file => file.RelativePath == root + "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(file.Id, GroupPlugin.PluginId));
        var session = workspace.OpenGroup(offer.Token).Session;
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new("character/balance", JsonSerializer.SerializeToElement(11))]).Ok);

        var review = session.GetChanges();

        Assert.False(review.Complete);
        var change = Assert.Single(review.Changes);
        Assert.Equal("character/balance", change.ValueId);
        Assert.Equal("character", change.Member?.Role);
        Assert.Equal(root + "character.dat", change.Member?.RelativePath);
        Assert.Equal(10, change.OriginalValue.AsInt32());
        Assert.Equal(11, change.CurrentValue.AsInt32());
        Assert.Equal("character", Assert.Single(change.Aliases!).Member?.Role);
        Assert.Equal(0, Assert.IsType<SemanticReviewCoverage>(review.Coverage).OmittedCount);
        Assert.Contains(review.Warnings, warning => warning.Contains("more context rows than requested", StringComparison.Ordinal));
        Assert.All(review.Warnings, warning => Assert.True(warning.Length <= SemanticDiagnostics.MaxMessageChars));
        var live = Assert.Single(plugin.Opened, document => !document.IsDisposed);
        Assert.Equal(["character/balance"], Assert.Single(live.ContextRequests));
    }

    private static SemanticChange Change(string id) =>
        new(id, id, [id], FieldValue.Of(1), FieldValue.Of(2), true, true, true);

    private static WorkspaceStore Store(IGamePlugin plugin) =>
        new(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    private static Workspace Import(WorkspaceStore store, byte[] zip) =>
        store.Create("context-closeout", [new ImportSource("authored.zip", () => new MemoryStream(zip))]);

    private sealed class MixedDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;

        public MixedDocument() => RegisterField(new FieldMetadata("canonical", "Canonical", IntegerSpec.Int32),
            () => FieldValue.Of(_value), value => _value = value.AsInt32());

        public override string Title => "Mixed metadata and legacy context";
        public override ViewNode BuildView() => new SectionNode(Title, [Field("canonical", "Canonical"),
            Field("legacy", "Legacy dependent value", IntegerSpec.Int32, () => FieldValue.Of(_value * 2))]);
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new[] { (byte)_value })];
    }
}
