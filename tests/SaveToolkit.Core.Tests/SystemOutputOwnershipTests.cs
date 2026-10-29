using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SystemOutputOwnershipTests
{
    private const string Root = "PPSA99997/sdimg_SAVE/";
    private const string SfoPath = Root + "sce_sys/param.sfo";

    [Fact]
    public void DeclaredSystemOutput_IsAcceptedAndReparsedByOverlay()
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: true);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateWorkspace(store, plugin);
        var session = OpenAndEdit(workspace, plugin);
        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.ChangedFiles });
        Assert.Equal([2], plan.Overrides[session.File.RelativePath].ToArray());
        Assert.True(plan.Overrides.ContainsKey(SfoPath));
        var overlay = new OverlaySaveSet(workspace.Set, plan.Overrides);
        Assert.Equal("New", overlay.Directories.Single().ParamSfo?.SubTitle);
    }

    [Fact]
    public void UndeclaredSystemOutput_IsRejectedWithoutPublishing()
    {
        var plugin = new OutputPlugin(declareSystemOutput: false, writeSystemOutput: true);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateWorkspace(store, plugin);
        var session = OpenAndEdit(workspace, plugin);
        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));
        Assert.Contains("undeclared system file", error.Message, StringComparison.Ordinal);
        Assert.True(session.State.IsDirty);
        Assert.Equal([1], session.File.ReadAllBytes());
    }

    [Fact]
    public void DeclaredOutput_IsReservedAcrossSessions()
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: false);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = store.Create("system ownership", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "first.dat", [1]), (Root + "second.dat", [1]), (SfoPath, CreateSfo("Old"))))) ]);
        var first = workspace.Set.AllDiskFiles.Single(f => f.FileName == "first.dat");
        var second = workspace.Set.AllDiskFiles.Single(f => f.FileName == "second.dat");
        _ = workspace.Open(first.Id, plugin.Info.Id);
        var error = Assert.Throws<InvalidOperationException>(() => workspace.Open(second.Id, plugin.Info.Id));
        Assert.Contains(SfoPath, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangedDeclaredSource_IsRejectedBeforeExport()
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: true);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateWorkspace(store, plugin);
        var session = OpenAndEdit(workspace, plugin);
        var sfo = workspace.Set.AllDiskFiles.Single(f => f.PathInDirectory.Equals("sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));
        Tamper(sfo, CreateSfo("Native"));
        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));
        Assert.Contains("changed since the document was opened", error.Message, StringComparison.Ordinal);
        Assert.True(session.State.IsDirty);
    }

    [Fact]
    public void DeclaredSourceChangedBetweenPluginOpenAndBinding_IsRejectedAndDisposed()
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: true);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateWorkspace(store, plugin);
        var sfo = workspace.Set.AllDiskFiles.Single(f => f.PathInDirectory.Equals("sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));
        plugin.AfterOpen = () => Tamper(sfo, CreateSfo("Changed"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            workspace.Open(workspace.Set.AllDiskFiles.Single(f => f.FileName == "slot.dat").Id, plugin.Info.Id));

        Assert.Contains("changed while the document was opening", error.Message, StringComparison.Ordinal);
        Assert.Empty(workspace.Documents);
        Assert.True(plugin.LastOpenedDisposed);
    }

    [Theory]
    [InlineData("sce_sys/param.sfo", "sce_sys/param.sfo")]
    [InlineData("sce_sys/missing.sfo", "sce_sys/missing.sfo")]
    [InlineData("sce_sys/*.sfo", "sce_sys/*.sfo")]
    [InlineData("other/param.sfo", "other/param.sfo")]
    [InlineData("../sce_sys/param.sfo", "../sce_sys/param.sfo")]
    public void InvalidAdditionalDeclarations_AreRejectedAtOpen(string declaration, string expected)
    {
        var plugin = new OutputPlugin(false, false) { Declarations = [declaration, declaration] };
        if (declaration != "sce_sys/param.sfo") plugin.Declarations = [declaration];
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateWorkspace(store, plugin);

        var error = Assert.Throws<SaveFormatException>(() =>
            workspace.Open(workspace.Set.AllDiskFiles.Single(f => f.FileName == "slot.dat").Id, plugin.Info.Id));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Empty(workspace.Documents);
    }

    [Fact]
    public void CloseReleasesDeclaredOutputForReacquisition()
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: false);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = store.Create("system ownership", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "first.dat", [1]), (Root + "second.dat", [1]), (SfoPath, CreateSfo("Old"))))) ]);
        var first = workspace.Set.AllDiskFiles.Single(f => f.FileName == "first.dat");
        var second = workspace.Set.AllDiskFiles.Single(f => f.FileName == "second.dat");
        var session = workspace.Open(first.Id, plugin.Info.Id);
        Assert.True(workspace.CloseClean(session.Id));

        _ = workspace.Open(second.Id, plugin.Info.Id);
    }

    [Fact]
    public void InvalidOverriddenSfo_IsRejectedAndOriginalMetadataRemains()
    {
        using var store = new WorkspaceStore(new PluginCatalog([]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateWorkspace(store, new OutputPlugin(false, false));
        var overrides = new Dictionary<string, ReadOnlyMemory<byte>> { [SfoPath] = new byte[] { 0, 1, 2 } };
        Assert.Throws<SaveFormatException>(() => new OverlaySaveSet(workspace.Set, overrides));
        Assert.Equal("Old", workspace.Set.Directories.Single().ParamSfo?.SubTitle);
    }

    [Fact]
    public void GroupDeclaredOutput_IsRequiredInTheSingleStagedTransaction()
    {
        var plugin = new GroupPlugin { DeclareSystemOutput = true };
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = store.Create("group system output", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root.Replace("99997", "99990") + "character.dat", GroupPlugin.Create("C", 1)),
                (Root.Replace("99997", "99990") + "profile.dat", GroupPlugin.Create("P", 2)),
                (Root.Replace("99997", "99990") + "sce_sys/param.sfo", CreateSfo("Old"))))) ]);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        var session = workspace.OpenGroup(offer.Token).Session;
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("character/balance", System.Text.Json.JsonSerializer.SerializeToElement(3))]).Ok);

        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.ChangedFiles });

        Assert.Contains(plan.Overrides.Keys, path => path.EndsWith("sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroupMetadataChangesSurviveUndoRedoAndFreshCandidateValidation()
    {
        var plugin = new GroupPlugin { DeclareSystemOutput = true };
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateGroupWorkspace(store, plugin);
        var group = OpenGroup(workspace);
        _ = group.BuildView();
        var original = workspace.Set.AllDiskFiles.Single(f => f.PathInDirectory.EndsWith("param.sfo", StringComparison.OrdinalIgnoreCase)).ReadAllBytes();

        Assert.True(group.ApplyEdits([new IncomingEdit("metadata/subtitle", System.Text.Json.JsonSerializer.SerializeToElement("New"))]).Ok);
        var changed = group.Serialize().Single(o => o.RelativePath.EndsWith("param.sfo", StringComparison.OrdinalIgnoreCase)).Content.ToArray();
        Assert.NotEqual(original, changed);
        Assert.Equal("New", new OverlaySaveSet(workspace.Set, group.Serialize().ToDictionary(o => o.RelativePath, o => o.Content)).Directories.Single().ParamSfo?.SubTitle);

        Assert.True(group.Undo().Ok);
        Assert.Equal(original, workspace.Set.AllDiskFiles.Single(f => f.PathInDirectory.EndsWith("param.sfo", StringComparison.OrdinalIgnoreCase)).ReadAllBytes());
        Assert.True(group.Redo().Ok);
        Assert.Equal(changed, group.Serialize().Single(o => o.RelativePath.EndsWith("param.sfo", StringComparison.OrdinalIgnoreCase)).Content.ToArray());
        Assert.True(group.InvokeAction("fail-metadata", new Dictionary<string, System.Text.Json.JsonElement>()).Ok is false);
        Assert.Equal(changed, group.Serialize().Single(o => o.RelativePath.EndsWith("param.sfo", StringComparison.OrdinalIgnoreCase)).Content.ToArray());
    }

    [Fact]
    public void FailedGroupBindingDisposesStagedModelAndRetainsCleanReplacementOwner()
    {
        var plugin = new GroupPlugin { DeclareSystemOutput = true };
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateGroupWorkspace(store, plugin);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var single = workspace.Open(anchor.Id, plugin.Info.Id);
        _ = single.BuildView();
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        var sfo = workspace.Set.AllDiskFiles.Single(f => f.PathInDirectory.EndsWith("param.sfo", StringComparison.OrdinalIgnoreCase));
        plugin.AfterRoleOpen = () => Delete(sfo);

        Assert.ThrowsAny<IOException>(() => workspace.OpenGroup(offer.Token, single.Id));

        Assert.Same(single, workspace.FindDocument(single.Id));
        _ = single.BuildView();
        Assert.Empty(workspace.DocumentGroups);
        Assert.All(plugin.Opened, document => Assert.True(document.IsDisposed));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GroupOmittedOrDuplicateSystemOutput_IsRejectedAtomically(bool omit, bool duplicate)
    {
        var plugin = new GroupPlugin { DeclareSystemOutput = true, OmitSystemOutput = omit, DuplicateSystemOutput = duplicate };
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = CreateGroupWorkspace(store, plugin);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));

        Assert.Throws<SaveFormatException>(() => workspace.OpenGroup(offer.Token));
        Assert.Empty(workspace.DocumentGroups);
        Assert.All(plugin.Opened, document => Assert.True(document.IsDisposed));
    }

    [Fact]
    public void GroupDeclaredOutput_ConflictsWithIndependentReservation()
    {
        var groupPlugin = new GroupPlugin { DeclareSystemOutput = true };
        var independentPlugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: false);
        using var store = new WorkspaceStore(new PluginCatalog([groupPlugin, independentPlugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = store.Create("group conflict", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root.Replace("99997", "99990") + "character.dat", GroupPlugin.Create("C", 1)),
                (Root.Replace("99997", "99990") + "profile.dat", GroupPlugin.Create("P", 2)),
                (Root.Replace("99997", "99990") + "outside.dat", [1]),
                (Root.Replace("99997", "99990") + "sce_sys/param.sfo", CreateSfo("Old"))))) ]);
        var outside = workspace.Set.AllDiskFiles.Single(f => f.FileName == "outside.dat");
        _ = workspace.Open(outside.Id, independentPlugin.Info.Id);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));

        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
        Assert.Empty(workspace.DocumentGroups);
    }

    [Theory]
    [InlineData(ExportScope.ChangedFiles, 2)]
    [InlineData(ExportScope.ChangedDirectories, 4)]
    [InlineData(ExportScope.Everything, 4)]
    public void ExportScopesAndRepeatedZipWritesAreDeterministic(ExportScope scope, int expectedFiles)
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: true);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = store.Create("scope output", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "slot.dat", [1]), (Root + "extra.dat", [9]),
                (Root + "sce_sys/param.sfo", CreateSfo("Old")), (Root + "sce_sys/icon0.png", [7])))) ]);
        OpenAndEdit(workspace, plugin);

        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = scope, MirrorBackups = false });
        Assert.Equal(expectedFiles, plan.Files.Count);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, first);
        WorkspaceExporter.WriteZip(plan, second);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void IdenticalSfoTwinMirrorsButDifferingOrProtectedTwinDoesNot()
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: true);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var same = CreateTwinWorkspace(store, plugin, "Old", "Old");
        OpenAndEdit(same, plugin);
        var samePlan = WorkspaceExporter.Plan(same, new() { Scope = ExportScope.ChangedFiles });
        Assert.Contains(samePlan.Overrides.Keys, path => path.Contains("sdimg_sce_bu_SAVE/sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));

        using var differing = CreateTwinWorkspace(store, plugin, "Old", "Twin");
        OpenAndEdit(differing, plugin);
        var differingPlan = WorkspaceExporter.Plan(differing, new() { Scope = ExportScope.ChangedFiles });
        Assert.DoesNotContain(differingPlan.Overrides.Keys, path => path.Contains("sdimg_sce_bu_SAVE/sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));

        using var protectedTwin = CreateTwinWorkspace(store, plugin, "Old", "Old");
        var backup = protectedTwin.Set.AllDiskFiles.Single(f => f.Directory.IsBackup && f.FileName == "slot.dat");
        _ = protectedTwin.Open(backup.Id, plugin.Info.Id);
        OpenAndEdit(protectedTwin, plugin);
        var protectedPlan = WorkspaceExporter.Plan(protectedTwin, new() { Scope = ExportScope.ChangedFiles });
        Assert.DoesNotContain(protectedPlan.Overrides.Keys, path => path.Contains("sdimg_sce_bu_SAVE/sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LooseSaveHasNoBackupTwinForDeclaredSystemOutput()
    {
        var plugin = new OutputPlugin(declareSystemOutput: true, writeSystemOutput: true);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = store.Create("loose", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build(("PPSA99997/SAVE/slot.dat", [1]), ("PPSA99997/SAVE/sce_sys/param.sfo", CreateSfo("Old"))))) ]);
        OpenAndEdit(workspace, plugin);

        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.ChangedFiles });

        Assert.Equal(2, plan.Overrides.Count);
    }

    internal static Workspace CreateWorkspace(WorkspaceStore store, OutputPlugin plugin) =>
        store.Create("system output", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "slot.dat", [1]), (SfoPath, CreateSfo("Old"))))) ]);

    internal static Workspace CreateGroupWorkspace(WorkspaceStore store, GroupPlugin plugin) =>
        store.Create("group system output", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root.Replace("99997", "99990") + "character.dat", GroupPlugin.Create("C", 1)),
                (Root.Replace("99997", "99990") + "profile.dat", GroupPlugin.Create("P", 2)),
                (Root.Replace("99997", "99990") + "sce_sys/param.sfo", CreateSfo("Old"))))) ]);

    private static Workspace CreateTwinWorkspace(WorkspaceStore store, OutputPlugin plugin,
        string primarySubtitle, string backupSubtitle) =>
        store.Create("twins", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "slot.dat", [1]), (Root + "sce_sys/param.sfo", CreateSfo(primarySubtitle)),
                ("PPSA99997/sdimg_sce_bu_SAVE/slot.dat", [1]),
                ("PPSA99997/sdimg_sce_bu_SAVE/sce_sys/param.sfo", CreateSfo(backupSubtitle))))) ]);

    private static DocumentSession OpenGroup(Workspace workspace)
    {
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        return workspace.OpenGroup(offer.Token).Session;
    }

    internal static DocumentSession OpenAndEdit(Workspace workspace, OutputPlugin plugin)
    {
        var file = workspace.Set.AllDiskFiles.Single(f => f.FileName == "slot.dat" && !f.Directory.IsBackup);
        var session = workspace.Open(file.Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("value", System.Text.Json.JsonSerializer.SerializeToElement(2))]).Ok);
        return session;
    }

    internal static void Tamper(ISaveFile file, byte[] bytes)
    {
        string path;
        using (var stream = (FileStream)file.OpenRead()) path = stream.Name;
        File.WriteAllBytes(path, bytes);
    }

    internal static void Delete(ISaveFile file)
    {
        string path;
        using (var stream = (FileStream)file.OpenRead()) path = stream.Name;
        File.Delete(path);
    }

    internal static byte[] CreateSfo(string subtitle)
    {
        byte[] key = Encoding.ASCII.GetBytes("SUBTITLE\0");
        byte[] value = Encoding.UTF8.GetBytes(subtitle + "\0");
        const int headerSize = 20;
        const int entrySize = 16;
        int keyOffset = headerSize + entrySize;
        int dataOffset = keyOffset + key.Length;
        byte[] bytes = new byte[dataOffset + 32];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46535000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x00000101);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), keyOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), dataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        var entry = bytes.AsSpan(headerSize, entrySize);
        BinaryPrimitives.WriteUInt16LittleEndian(entry, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 0x0204);
        BinaryPrimitives.WriteInt32LittleEndian(entry[4..], value.Length);
        BinaryPrimitives.WriteInt32LittleEndian(entry[8..], 32);
        key.CopyTo(bytes.AsSpan(keyOffset));
        value.CopyTo(bytes.AsSpan(dataOffset));
        return bytes;
    }

    internal sealed class OutputPlugin(bool declareSystemOutput, bool writeSystemOutput) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.system-output", "System output", "1.0.0");
        public IReadOnlyList<string> Declarations { get; set; } =
            declareSystemOutput ? ["sce_sys/param.sfo"] : [];
        public Action? AfterOpen { get; set; }
        private OutputDocument? LastOpened { get; set; }
        public bool LastOpenedDisposed => LastOpened?.IsDisposed == true;
        public Func<IReadOnlyList<FileOutput>, IReadOnlyList<FileOutput>>? OutputTransform { get; set; }
        public bool ThrowOnDispose { get; set; }
        public bool WriteSystemOutput { get; set; } = writeSystemOutput;
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file)
        {
            var document = new OutputDocument(this, file, Declarations, WriteSystemOutput);
            LastOpened = document;
            AfterOpen?.Invoke();
            return document;
        }
    }

    private sealed class OutputDocument(OutputPlugin owner, ISaveFile file, IReadOnlyList<string> declarations, bool writeSystemOutput) : BoundSaveDocument, IAdditionalOutputOwner
    {
        private readonly string _path = file.RelativePath;
        private readonly bool _writeSystemOutput = writeSystemOutput;
        private byte _value = file.ReadAllBytes()[0];
        private readonly byte[] _sfo = file.Directory.FindAnyFile("sce_sys/param.sfo")?.ReadAllBytes() ?? [];

        public bool IsDisposed { get; private set; }

        public IReadOnlyList<string> AdditionalOutputPaths { get; } = declarations;
        public override string Title => "System output";
        public override ViewNode BuildView() => Field("value", "Value", IntegerSpec.UInt8,
            () => FieldValue.Of(_value), value => _value = (byte)value.AsInt32());
        public override IReadOnlyList<FileOutput> Serialize()
        {
            var outputs = _writeSystemOutput
                ? (IReadOnlyList<FileOutput>)[new(_path, new byte[] { _value }), new($"{_path[.._path.LastIndexOf('/')]}/sce_sys/param.sfo", ReplaceSubtitle())]
                : [new(_path, new byte[] { _value })];
            return owner.OutputTransform?.Invoke(outputs) ?? outputs;
        }

        private byte[] ReplaceSubtitle()
        {
            var value = Encoding.UTF8.GetBytes("New\0");
            var copy = _sfo.ToArray();
            var marker = Encoding.UTF8.GetBytes("Old\0");
            var offset = copy.AsSpan().IndexOf(marker);
            value.CopyTo(copy.AsSpan(offset));
            BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(24), value.Length);
            return copy;
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            if (owner.ThrowOnDispose) throw new IOException("Injected output-document cleanup failure.");
            base.Dispose(disposing);
        }
    }
}
