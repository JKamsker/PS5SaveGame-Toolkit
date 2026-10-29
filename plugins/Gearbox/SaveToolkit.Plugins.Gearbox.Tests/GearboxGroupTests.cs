using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GearboxGroupTests
{
    private readonly GearboxPlugin _plugin = new();

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EveryGenerationAndBackup_ExplicitPairsWritableViewsAndFreshSnapshotRestoration(string title)
    {
        var set = SampleSaves.Load(title);
        var characters = set.AllDiskFiles.Where(f => f.FileName == "1").ToArray();
        Assert.Equal(6, characters.Length);
        foreach (var anchor in characters)
        {
            var proposal = Assert.Single(_plugin.ProposeGroups(anchor));
            Assert.Empty(proposal.Errors);
            Assert.Contains(proposal.Members, m => m.RelativePath == anchor.RelativePath);
            var input = Input(set, proposal);
            Assert.All(input.Members, m => {
                Assert.Equal(anchor.Directory.Generation, m.File.Directory.Generation);
                Assert.Equal(anchor.Directory.IsBackup, m.File.Directory.IsBackup);
                Assert.Equal(anchor.Directory.TitleId, m.File.Directory.TitleId);
            });
            Assert.Equal(proposal.Members, Assert.Single(_plugin.ProposeGroups(input.Members.Single(m => m.Role == "profile").File)).Members);
            using var group = _plugin.OpenGroup(input, IReferenceContext.Empty);
            Assert.True(group.CanEdit);
            var view = group.BuildView();
            Assert.NotEmpty(JsonSerializer.Serialize<ViewNode>(view));
            var nodes = Walk(group, view).ToArray();
            var fields = nodes.OfType<FieldNode>().ToArray();
            var rowKeys = nodes.OfType<TableNode>().SelectMany(t => t.Rows.Select(r => r.Key)).ToArray();
            Assert.Equal(rowKeys.Length, rowKeys.Distinct().Count());
            Assert.NotEmpty(fields);
            Assert.Equal(fields.Length, fields.Select(f => f.ValueId).Distinct().Count());
            Assert.Contains(fields, f => f.ValueId == "character/character/xp");
            Assert.Contains(fields, f => f.ValueId.StartsWith("profile/", StringComparison.Ordinal));
            Assert.Contains(fields, f => !f.Spec.ReadOnly);
            Assert.All(fields, f => Assert.Equal(f.Value, group.GetValue(f.ValueId)));
            var collections = ((IQueryableDocument)group).Collections;
            Assert.Equal(collections.Count, collections.Select(c => c.Id).Distinct().Count());
            var query = new QueryableCollectionQuery { Limit = 2 };
            var page = ((IQueryableDocument)group).Query("character/inventory_items", query);
            var profile = ((IQueryableDocument)group).Query("profile/bank_inventory_list", query);
            Assert.All(page.Rows.Concat(profile.Rows), row => Assert.All(row.Cells.Values.OfType<FieldNode>(), f => Assert.True(f.Spec.ReadOnly)));
            var detail = Assert.IsType<SectionNode>(page.Rows[0].Cells["detail"]);
            Assert.NotEmpty(group.ExpandNode(detail.LazyId!));
            var snapshot = group.CaptureMemento();
            Assert.True(snapshot.SizeBytes >= input.Members.Sum(m => m.File.Length));
            using var restored = _plugin.OpenGroup(input, IReferenceContext.Empty);
            var inverse = restored.RestoreMemento(snapshot);
            _ = restored.BuildView();
            Assert.Equal(page.Rows.Select(r => r.Key), ((IQueryableDocument)restored).Query("character/inventory_items", query).Rows.Select(r => r.Key));
            Assert.NotEmpty(restored.ExpandNode(detail.LazyId!));
            _ = restored.RestoreMemento(inverse);
            _ = restored.BuildView();
            foreach (var output in restored.Serialize()) Assert.Equal(((ISaveSet)set).FindFile(output.RelativePath)!.ReadAllBytes(), output.Content.ToArray());
            Assert.Equal(2, restored.Serialize().Count);
            Assert.False(group.ApplyEdits([new("character/character/xp", FieldValue.Of(-1L))]).Succeeded);
            Assert.Throws<KeyNotFoundException>(() => group.InvokeAction("transfer/deposit", new Dictionary<string, FieldValue>()));
            foreach (var output in group.Serialize()) Assert.Equal(((ISaveSet)set).FindFile(output.RelativePath)!.ReadAllBytes(), output.Content.ToArray());
        }
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void InvalidGenerationRolesBackupAndForeignSnapshots_AreRejected(string title)
    {
        var set = SampleSaves.Load(title);
        var character = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup && f.Directory.Generation == 0);
        var profile = set.AllDiskFiles.First(f => f.FileName == "profile" && !f.Directory.IsBackup && f.Directory.Generation == 0);
        var otherGeneration = set.AllDiskFiles.First(f => f.FileName == "profile" && !f.Directory.IsBackup && f.Directory.Generation == 1);
        var backup = set.AllDiskFiles.First(f => f.FileName == "profile" && f.Directory.IsBackup && f.Directory.Generation == 0);
        foreach (var input in new DocumentGroupInput[] {
            new([new("character", character), new("profile", otherGeneration)]),
            new([new("character", character), new("profile", backup)]),
            new([new("profile", character), new("character", profile)]),
            new([new("character", character), new("character", profile)]),
            new([new("character", character)]),
        })
        {
            Assert.NotEmpty(_plugin.ValidateGroup(input, IReferenceContext.Empty));
            Assert.Throws<SaveFormatException>(() => _plugin.OpenGroup(input, IReferenceContext.Empty));
        }
        using var first = _plugin.OpenGroup(new([new("character", character), new("profile", profile)]), IReferenceContext.Empty);
        var secondCharacter = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup && f.Directory.Generation == 1);
        using var second = _plugin.OpenGroup(new([new("character", secondCharacter), new("profile", otherGeneration)]), IReferenceContext.Empty);
        Assert.Throws<InvalidOperationException>(() => second.RestoreMemento(first.CaptureMemento()));
        Assert.All(second.Serialize(), output => Assert.Equal(((ISaveSet)set).FindFile(output.RelativePath)!.ReadAllBytes(), output.Content.ToArray()));
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void F7Host_OwnershipQueriesConflictsAndCompleteUnchangedExport(string title)
    {
        var set = SampleSaves.Load(title);
        using var workspace = new Workspace("gearbox-groups", "Grouped samples", set, new PluginCatalog([_plugin]), new ReferencePackStore([_plugin]));
        var character = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        var offer = Assert.Single(workspace.ProposeGroups(character.Id, "gearbox"));
        Assert.Empty(offer.Errors);
        var profile = set.FindById(offer.Members.Single(m => m.Role == "profile").FileId)!;
        var single = workspace.Open(profile.Id, "gearbox");
        _ = single.BuildView();
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
        Assert.True(single.ApplyEdits([new("keys/2420693941", JsonSerializer.SerializeToElement("1"))]).Ok);
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token, single.Id));
        Assert.True(single.Undo().Ok);
        var group = workspace.OpenGroup(offer.Token, single.Id);
        var session = group.Session;
        _ = session.BuildView();
        Assert.True(session.CanEdit);
        Assert.Throws<InvalidOperationException>(() => workspace.Open(profile.Id, "gearbox"));
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(character.Id, "gearbox")).Token));
        var page = session.QueryCollection("character/inventory_items", new() { Limit = 1 });
        Assert.Single(page.Rows);
        var detail = Assert.IsType<SectionNode>(page.Rows[0].Cells["detail"]);
        Assert.NotEmpty(session.Expand(detail.LazyId!));
        Assert.False(session.ApplyEdits([new("character/character/xp", JsonSerializer.SerializeToElement("-1"))]).Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Equal(0, session.State.Revision);
        Assert.All(session.Serialize(), output => Assert.Equal(((ISaveSet)set).FindFile(output.RelativePath)!.ReadAllBytes(), output.Content.ToArray()));
        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.Everything });
        Assert.Empty(plan.Overrides);
        using var zip = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, zip);
        zip.Position = 0;
        using var archive = new ZipArchive(zip);
        foreach (var member in group.Members)
        {
            using var entry = archive.GetEntry(member.RelativePath)!.Open();
            using var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            Assert.Equal(set.FindById(member.FileId)!.ReadAllBytes(), bytes.ToArray());
        }
        Assert.True(workspace.Close(session.Id));
        Assert.NotNull(workspace.Open(profile.Id, "gearbox"));
    }

    [Fact]
    public void MissingUnsupportedAmbiguousAndMultipleCharacters_AreExplicit()
    {
        var bytes = GearboxTestData.CreateContainer(false, false);
        var profile = GearboxTestData.CreateContainer(false, true);
        var missing = MemorySaveSet.SingleFile("1", bytes, "PPSA01463");
        Assert.NotEmpty(Assert.Single(_plugin.ProposeGroups(missing)).Errors);
        Assert.Empty(_plugin.ProposeGroups(MemorySaveSet.SingleFile("1", bytes, "PPSA01494")));
        var nested = MemorySaveSet.SingleFile("nested/1", bytes, "PPSA01463");
        Assert.NotEmpty(Assert.Single(_plugin.ProposeGroups(nested)).Errors);
        var set = new MemorySaveSet().With("PPSA01463", "TEST", new Dictionary<string, byte[]> { ["1"] = bytes, ["2"] = bytes, ["profile"] = profile });
        var files = set.Directories[0].Files;
        var alternatives = _plugin.ProposeGroups(files.Single(f => f.FileName == "profile"));
        Assert.Equal(2, alternatives.Count);
        Assert.All(alternatives, p => Assert.Empty(p.Errors));
        Assert.NotEqual(alternatives[0].Members[0].RelativePath, alternatives[1].Members[0].RelativePath);
        set.With("PPSA01463", "SECOND-PROFILE", new Dictionary<string, byte[]> { ["profile"] = profile });
        var ambiguous = _plugin.ProposeGroups(files[0]);
        Assert.Equal(2, ambiguous.Count);
        Assert.All(ambiguous, p => { Assert.Contains("unverified profile choice", p.Title); Assert.Empty(p.Errors); });
        Assert.NotEqual(ambiguous[0].Members[1].RelativePath, ambiguous[1].Members[1].RelativePath);
        var wrongRole = new MemorySaveSet().With("PPSA01463", "TEST", new Dictionary<string, byte[]> { ["1"] = bytes, ["profile"] = bytes });
        Assert.NotEmpty(Assert.Single(_plugin.ProposeGroups(wrongRole.Directories[0].Files[0])).Errors);
        var foreign = MemorySaveSet.SingleFile("profile", profile, "PPSA01463");
        Assert.NotEmpty(_plugin.ValidateGroup(new([new("character", files[0]), new("profile", foreign)]), IReferenceContext.Empty));
        var mismatchedBackup = new MemorySaveSet().With("PPSA01463", "CHAR", new Dictionary<string, byte[]> { ["1"] = bytes })
            .With("PPSA01463", "PROFILE", new Dictionary<string, byte[]> { ["profile"] = profile }, backup: true);
        Assert.NotEmpty(Assert.Single(_plugin.ProposeGroups(mismatchedBackup.Directories[0].Files[0])).Errors);
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void DuplicateSerialRows_RetainDistinctIdsAcrossFreshGroupSnapshots(string title)
    {
        var source = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        var container = GearboxContainer.Parse(source.ReadAllBytes(), title, false);
        var root = container.Decode();
        var items = root.Children("inventory_items").Take(2).ToArray();
        var serial = (byte[])items[0].Last("item_serial_number")!.Value!;
        items[1].Last("item_serial_number")!.Set(serial.ToArray());
        var bytes = container.Encode(root);
        var profile = source.Directory.SaveSet.AllFiles.Single(f => f.FileName == "profile" && !f.Directory.IsBackup && f.Directory.Generation == source.Directory.Generation);
        var set = new MemorySaveSet().With(title, "CHAR", new Dictionary<string, byte[]> { ["1"] = bytes })
            .With(title, "PROFILE", new Dictionary<string, byte[]> { ["profile"] = profile.ReadAllBytes() });
        var proposal = Assert.Single(_plugin.ProposeGroups(set.Directories[0].Files[0]));
        var input = Input(set, proposal);
        using var group = _plugin.OpenGroup(input, IReferenceContext.Empty);
        var page = ((IQueryableDocument)group).Query("character/inventory_items", new() { Limit = 2 });
        Assert.Equal(2, page.Rows.Count);
        Assert.NotEqual(page.Rows[0].Key, page.Rows[1].Key);
        Assert.Equal(Assert.IsType<FieldNode>(page.Rows[0].Cells["name"]).Value, Assert.IsType<FieldNode>(page.Rows[1].Cells["name"]).Value);
        using var restored = _plugin.OpenGroup(input, IReferenceContext.Empty);
        _ = restored.RestoreMemento(group.CaptureMemento());
        Assert.Equal(page.Rows.Select(r => r.Key), ((IQueryableDocument)restored).Query("character/inventory_items", new() { Limit = 2 }).Rows.Select(r => r.Key));
        Assert.Throws<InvalidOperationException>(() => restored.RestoreMemento(DocumentMemento.Snapshot("foreign", 1)));
        Assert.Equal(bytes, restored.Serialize().Single(o => o.RelativePath.EndsWith("/1", StringComparison.Ordinal)).Content.ToArray());
    }

    internal static DocumentGroupInput Input(ISaveSet set, DocumentGroupProposal proposal) =>
        new(proposal.Members.Select(m => new DocumentGroupFile(m.Role, set.FindFile(m.RelativePath)!)).ToArray());

    internal static IEnumerable<ViewNode> Walk(ISaveDocument document, ViewNode node)
    {
        yield return node;
        var children = node switch
        {
            SectionNode section when section.LazyId?.Contains("/raw", StringComparison.Ordinal) == true => [],
            SectionNode section when section.LazyId is { } id => document.ExpandNode(id),
            SectionNode section => section.Children,
            TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children).ToArray(),
            TableNode table => table.Rows.SelectMany(r => r.Cells.Values).ToArray(),
            _ => Array.Empty<ViewNode>(),
        };
        foreach (var child in children)
            foreach (var descendant in Walk(document, child)) yield return descendant;
    }
}
