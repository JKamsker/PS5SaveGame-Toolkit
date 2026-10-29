using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.References;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Ordinary/group sessions with real synthetic pinned packs for clean-close regressions.</summary>
internal sealed class CleanCloseFixture : IDisposable
{
    public CleanCloseFixture(bool grouped)
    {
        Plugin = grouped ? new GroupPlugin() : new CounterPlugin();
        Store = new WorkspaceStore(new PluginCatalog([Plugin]), new() { DataRoot = TestZips.TempDir() });
        Store.Packs.Import((PackBuilder.WithItems("Pinned close test") with { Plugins = [Plugin.Info.Id] }).Zip(), "close-test.zip");
        var bytes = grouped
            ? TestZips.Build(("PPSA99990/sdimg_SAVE/character.dat", GroupPlugin.Create("C", 10)),
                ("PPSA99990/sdimg_SAVE/profile.dat", GroupPlugin.Create("P", 20)))
            : CounterPlugin.Create(5);
        Workspace = Store.Create("clean close", [new ImportSource(grouped ? "dump.zip" : "save.dat", () => new MemoryStream(bytes))]);
        var anchor = Workspace.Set.AllDiskFiles.First(file => !grouped || file.FileName == "character.dat");
        if (grouped) Group = Workspace.OpenGroup(Assert.Single(Workspace.ProposeGroups(anchor.Id, Plugin.Info.Id)).Token);
        Session = Group?.Session ?? Workspace.Open(anchor.Id, Plugin.Info.Id);
        Field = grouped ? "character/balance" : "counter";
        EditedValue = grouped ? 11 : 6;
        _ = Session.BuildView();
        References = Workspace.References(Session.Id);
        Assert.NotEmpty(References.Packs);
    }

    public IGamePlugin Plugin { get; }
    public WorkspaceStore Store { get; }
    public Workspace Workspace { get; }
    public DocumentSession Session { get; }
    public DocumentGroupSession? Group { get; }
    public ReferenceContext References { get; }
    public string Field { get; }
    public int EditedValue { get; }
    public SessionEditResult Edit() => Session.ApplyEdits([new(Field, JsonSerializer.SerializeToElement(EditedValue))]);

    public void AssertRetained(DocumentState state, IReadOnlyList<FileOutput> outputs)
    {
        Assert.Same(Session, Workspace.FindDocument(Session.Id));
        Assert.Same(References, Workspace.References(Session.Id));
        Assert.Equal(state, Session.State);
        var current = Session.Serialize();
        Assert.Equal(outputs.Select(output => output.RelativePath), current.Select(output => output.RelativePath));
        for (var index = 0; index < outputs.Count; index++)
            Assert.Equal(outputs[index].Content.ToArray(), current[index].Content.ToArray());
        if (Group is not null)
        {
            Assert.Same(Group, Assert.Single(Workspace.DocumentGroups));
            foreach (var member in Group.Members)
                Assert.Throws<InvalidOperationException>(() => Workspace.Open(member.FileId, Plugin.Info.Id));
        }
        else Assert.Same(Session, Workspace.Open(Session.File.Id, Plugin.Info.Id));
    }

    public void AssertClosed()
    {
        Assert.Null(Workspace.FindDocument(Session.Id));
        Assert.Empty(Workspace.References(Session.Id).Packs);
        Assert.Empty(Workspace.DocumentGroups);
        Assert.Throws<ObjectDisposedException>(() => Session.BuildView());
        var reopened = Workspace.Open(Session.File.Id, Plugin.Info.Id);
        Assert.NotSame(Session, reopened);
    }

    public void Dispose() => Store.Dispose();
}
