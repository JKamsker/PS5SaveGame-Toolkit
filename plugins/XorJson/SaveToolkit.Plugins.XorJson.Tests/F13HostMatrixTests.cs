using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class F13HostMatrixTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void HostPreflightCoversEveryExistingPlannedScalarFamilyAndReplay(int encoding)
    {
        using var fixture = new HostFixture(softVersion: 2, encoding);
        _ = fixture.Session.BuildView();
        var query = Assert.IsAssignableFrom<IQueryableDocument>(fixture.Document);
        var level = ViewTraversal.Fields(fixture.Document).Single(field => field.Label == "Level");
        var attribute = Assert.Single(query.Query("character/attributes", new()).Rows);
        var inventory = query.Query("character/inventory", new()).Rows;
        Assert.NotEmpty(inventory);
        var sword = inventory[0];
        var cases = new[]
        {
            new PlannedCase("json/bulk-set", Parameters(("value", "2")), new ActionScope([level.ValueId])),
            new PlannedCase("character/set-attribute", new Dictionary<string, JsonElement>
            {
                ["id"] = JsonSerializer.SerializeToElement("Emeralds"), ["value"] = JsonSerializer.SerializeToElement(2),
            }, new ActionScope([attribute.Key])),
            new PlannedCase("character/set-stacks", NumberParameters("value", 2), new ActionScope([sword.Key])),
            new PlannedCase("character/replace-item", Parameters(("id", "SW.Item.HoneyBoots_Unique")), new ActionScope([sword.Key])),
            new PlannedCase("character/set-rarity", Parameters(("id", "SW.Rarity.Rare")), new ActionScope([sword.Key])),
        };

        foreach (var planned in cases)
        {
            var before = fixture.Document.Serialize().Single().Content.ToArray();
            var preview = fixture.Session.PlanAction(planned.ActionId, planned.Parameters, planned.Scope, expectedRevision: null,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            Assert.True(preview.Plan.Changes.Count == 1,
                $"{planned.ActionId}: {string.Join("; ", preview.Plan.SkippedRows.Select(row => row.Reason))}");
            var change = preview.Plan.Changes[0];
            Assert.False(change.Derived);
            Assert.True(change.OldExists);
            Assert.True(change.NewExists);
            Assert.Equal(change.OldValue, fixture.Document.GetValue(change.ValueId));
            Assert.Equal(before, fixture.Document.Serialize().Single().Content.ToArray());

            var applied = fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision, TestContext.Current.CancellationToken);
            Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(error => error.Message)));
            var changed = fixture.Document.Serialize().Single().Content.ToArray();
            Assert.Equal(fixture.Encode(Expected(planned.ActionId, fixture.OriginalText)), changed);
            Assert.Equal(change.NewValue, fixture.Document.GetValue(change.ValueId));
            using (var reopened = fixture.Plugin.Open(MemorySaveSet.SingleFile("hero.sav", changed)))
            {
                Assert.Single(Assert.IsAssignableFrom<IFieldMetadataDocument>(reopened).ResolveFieldMetadata([change.ValueId]));
                Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId));
                Assert.Equal(changed, reopened.Serialize().Single().Content.ToArray());
            }
            Assert.True(fixture.Session.Undo().Ok);
            Assert.Equal(before, fixture.Document.Serialize().Single().Content.ToArray());
            Assert.True(fixture.Session.Redo().Ok);
            Assert.Equal(changed, fixture.Document.Serialize().Single().Content.ToArray());
            Assert.True(fixture.Session.Undo().Ok);

            var empty = fixture.Session.PlanAction(planned.ActionId, planned.Parameters, new ActionScope([]), expectedRevision: null,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Empty(empty.Plan.Changes);
        }

        var documentScope = fixture.Session.PlanAction("json/bulk-set", Parameters(("value", "2")), new ActionScope(), expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(documentScope.PlanId);
        Assert.NotEmpty(documentScope.Plan.Changes);
        var malformed = fixture.Session.PlanAction("character/replace-item", Parameters(("id", "SW.Item.HoneyBoots_Unique")), new ActionScope(["value/999999"]), expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(malformed.PlanId);
        Assert.NotEmpty(malformed.Plan.Errors);
    }

    [Fact]
    public void HostQueryPlanningIsPureAndReadonlyProfilesRejectMetadataPreflight()
    {
        using var fixture = new HostFixture(softVersion: 2);
        _ = fixture.Session.BuildView();
        var queryScope = new ActionScope
        {
                QueryScope = new ActionQueryScope("character/inventory", new QueryableCollectionQuery { Text = "Beekeeper", Limit = 1 }, fixture.Session.State.Revision),
        };
        var before = fixture.Document.Serialize().Single().Content.ToArray();
        var preview = fixture.Session.PlanAction("character/set-stacks", NumberParameters("value", 2), queryScope, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(preview.PlanId is not null, string.Join("; ", preview.Plan.Errors.Select(error => error.Message)));
        Assert.Single(preview.Plan.Changes);
        Assert.Equal(before, fixture.Document.Serialize().Single().Content.ToArray());

        using var readonlyFixture = new HostFixture(softVersion: 1);
        _ = readonlyFixture.Session.BuildView();
        var level = ViewTraversal.Fields(readonlyFixture.Document).Single(field => field.Label == "Level");
        var rejected = readonlyFixture.Session.PlanAction("json/bulk-set", Parameters(("value", "2")), new ActionScope([level.ValueId]), expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(rejected.PlanId);
        Assert.NotEmpty(rejected.Plan.Errors);
        Assert.False(readonlyFixture.Session.State.CanUndo);
    }

    [Fact]
    public void MetadataOnlyResolutionKeepsLiveSessionsAndQueryStateIsolated()
    {
        using var fixture = new HostFixture(softVersion: 2);
        using var peer = new HostFixture(softVersion: 2);
        var initial = fixture.Session.State;
        var metadata = Assert.IsAssignableFrom<IFieldMetadataDocument>(fixture.Document);
        Assert.Equal(["value/8", "value/13"], metadata.ResolveFieldMetadata(["value/8", "value/13"]).Select(field => field.ValueId));
        Assert.Equal(initial, fixture.Session.State);
        Assert.Equal(fixture.Encode(fixture.OriginalText), fixture.Document.Serialize().Single().Content.ToArray());
        _ = fixture.Session.BuildView();
        var first = fixture.Session.QueryCollection("character/inventory", new(), null, TestContext.Current.CancellationToken);
        var state = fixture.Session.State;
        _ = metadata.ResolveFieldMetadata(["value/13", "value/8", "value/2147483647"]);
        var second = fixture.Session.QueryCollection("character/inventory", new(), null, TestContext.Current.CancellationToken);
        Assert.Equal(first.TotalCount, second.TotalCount);
        Assert.Equal(first.Rows.Select(row => row.Key), second.Rows.Select(row => row.Key));
        Assert.Equal(state, fixture.Session.State);
        Assert.True(fixture.Session.ApplyEdits([new("value/8", JsonSerializer.SerializeToElement(2))], null, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(FieldValue.ExactNumber("2"), fixture.Document.GetValue("value/8"));
        Assert.Equal(initial, peer.Session.State);
        Assert.Equal(peer.Encode(peer.OriginalText), peer.Document.Serialize().Single().Content.ToArray());
        Assert.Equal(fixture.Encode(fixture.OriginalText), fixture.File.ReadAllBytes());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.Equal(fixture.Encode(fixture.OriginalText), fixture.Document.Serialize().Single().Content.ToArray());
    }

    private static IReadOnlyDictionary<string, JsonElement> Parameters(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, JsonElement> NumberParameters(string key, int value) =>
        new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value) };

    private sealed record PlannedCase(string ActionId, IReadOnlyDictionary<string, JsonElement> Parameters, ActionScope Scope);

    private static string Expected(string action, string original) => action switch
    {
        "json/bulk-set" => original.Replace("\"Level\":1", "\"Level\":2", StringComparison.Ordinal),
        "character/set-attribute" => original.Replace("\"CurrentValue\":1", "\"CurrentValue\":2", StringComparison.Ordinal),
        "character/set-stacks" => original.Replace("\"None\"},\"StackCount\":1", "\"None\"},\"StackCount\":2", StringComparison.Ordinal),
        "character/replace-item" => original.Replace("SW.Item.HoneyBoots\"", "SW.Item.HoneyBoots_Unique\"", StringComparison.Ordinal),
        _ => original.Replace("SW.Rarity.Common", "SW.Rarity.Rare", StringComparison.Ordinal),
    };

    private sealed class HostFixture : IDisposable
    {
        private readonly WorkspaceScratch _scratch = new();
        private readonly WorkspaceStore _store;
        private readonly int _encoding;
        public string OriginalText { get; }
        public XorJsonPlugin Plugin { get; } = new();
        public DiskSaveFile File { get; }
        public ISaveDocument Document { get; }
        public DocumentSession Session { get; }

        public HostFixture(int softVersion, int encoding = -1)
        {
            _encoding = encoding;
            OriginalText = CharacterJson(softVersion);
            var references = new ReferencePackStore([Plugin]).Pin(Plugin, "PPSA16064");
            _store = new WorkspaceStore(new PluginCatalog([Plugin]), new WorkspaceStoreOptions { DataRoot = _scratch.Root });
            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var entry = zip.CreateEntry("PPSA16064/sdimg_Character0/hero.sav").Open();
                entry.Write(Encode(OriginalText));
            }
            var workspace = _store.Create("f13-host", [new ImportSource("fixture.zip", () => new MemoryStream(archive.ToArray()))]);
            File = workspace.Set.AllDiskFiles.Single();
            Document = Plugin.Open(File, references);
            Session = new DocumentSession("f13-host", File, Plugin, Document, references: references);
        }

        public byte[] Encode(string text) => _encoding < 0 ? Encoding.UTF8.GetBytes(text) : XorJsonTests.Encode(text, _encoding == 1);

        public void Dispose()
        {
            Session.Dispose();
            Document.Dispose();
            _store.Dispose();
            _scratch.Dispose();
        }

        private static string CharacterJson(int softVersion) => $"{{\"SerializeMeta\":{{\"HardFormat\":\"FCharacterSaveV1\",\"SoftVersion\":{softVersion},\"FormatHash\":3777917472}},\"CharacterSaveV1\":{{\"MetaData\":{{\"IsOnline\":false,\"Level\":1}},\"Ability\":{{\"Attributes\":[{{\"AttributeName\":\"Emeralds\",\"CurrentValue\":1}}]}},\"Inventory\":{{\"Entries\":[{{\"ItemData\":{{\"TypeTag\":\"SW.Item.HoneyBoots\",\"RarityTag\":\"SW.Rarity.Common\",\"TargetSlotOverride\":\"None\"}},\"StackCount\":1}},{{\"ItemData\":{{\"TypeTag\":\"SW.Item.Bow\",\"RarityTag\":\"SW.Rarity.Rare\",\"TargetSlotOverride\":\"Merchant\"}},\"StackCount\":1}}]}}}}}}";
    }
}
