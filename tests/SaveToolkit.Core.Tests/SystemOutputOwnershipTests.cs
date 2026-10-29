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

    private static Workspace CreateWorkspace(WorkspaceStore store, OutputPlugin plugin) =>
        store.Create("system output", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "slot.dat", [1]), (SfoPath, CreateSfo("Old"))))) ]);

    private static DocumentSession OpenAndEdit(Workspace workspace, OutputPlugin plugin)
    {
        var file = workspace.Set.AllDiskFiles.Single(f => f.FileName == "slot.dat");
        var session = workspace.Open(file.Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("value", System.Text.Json.JsonSerializer.SerializeToElement(2))]).Ok);
        return session;
    }

    private static void Tamper(ISaveFile file, byte[] bytes)
    {
        string path;
        using (var stream = (FileStream)file.OpenRead()) path = stream.Name;
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] CreateSfo(string subtitle)
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

    private sealed class OutputPlugin(bool declareSystemOutput, bool writeSystemOutput) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.system-output", "System output", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file) => new OutputDocument(file, declareSystemOutput, writeSystemOutput);
    }

    private sealed class OutputDocument : BoundSaveDocument, IAdditionalOutputOwner
    {
        private readonly string _path;
        private readonly bool _writeSystemOutput;
        private byte _value;
        private readonly byte[] _sfo;

        public OutputDocument(ISaveFile file, bool declareSystemOutput, bool writeSystemOutput)
        {
            _path = file.RelativePath;
            _value = file.ReadAllBytes()[0];
            _writeSystemOutput = writeSystemOutput;
            _sfo = file.Directory.FindAnyFile("sce_sys/param.sfo")?.ReadAllBytes() ?? [];
            AdditionalOutputPaths = declareSystemOutput ? ["sce_sys/param.sfo"] : [];
        }

        public IReadOnlyList<string> AdditionalOutputPaths { get; }
        public override string Title => "System output";
        public override ViewNode BuildView() => Field("value", "Value", IntegerSpec.UInt8,
            () => FieldValue.Of(_value), value => _value = (byte)value.AsInt32());
        public override IReadOnlyList<FileOutput> Serialize() =>
            _writeSystemOutput
                ? [new(_path, new byte[] { _value }), new($"{_path[.._path.LastIndexOf('/')]}/sce_sys/param.sfo", ReplaceSubtitle())]
                : [new(_path, new byte[] { _value })];

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
    }
}
