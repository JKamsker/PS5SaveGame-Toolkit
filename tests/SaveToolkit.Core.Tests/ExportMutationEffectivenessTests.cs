using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ExportMutationEffectivenessTests
{
    private const string Root = "PPSA99999/sdimg_SAVE/";

    [Fact]
    public void UnknownOutputPathRejectsExportWithoutChangingTheImportedBytes()
    {
        var plugin = new OutputPlugin(Root + "missing.dat");
        using var store = CreateStore(plugin);
        var workspace = Import(store);
        var session = Edit(workspace, "first.dat", plugin);

        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));

        Assert.Contains(plugin.OutputPath, error.Message, StringComparison.Ordinal);
        Assert.True(session.State.IsDirty);
        Assert.Equal(new byte[] { 1 }, session.File.ReadAllBytes());
    }

    [Fact]
    public void TwoDirtyDocumentsCannotSilentlyOverwriteTheSameOutputPath()
    {
        var plugin = new OutputPlugin(Root + "first.dat");
        using var store = CreateStore(plugin);
        var workspace = Import(store);
        var first = Edit(workspace, "first.dat", plugin);
        var second = Edit(workspace, "second.dat", plugin);

        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));

        Assert.Contains(plugin.OutputPath, error.Message, StringComparison.Ordinal);
        Assert.True(first.State.IsDirty);
        Assert.True(second.State.IsDirty);
        Assert.Equal(new byte[] { 1 }, first.File.ReadAllBytes());
        Assert.Equal(new byte[] { 1 }, second.File.ReadAllBytes());
    }

    private static WorkspaceStore CreateStore(OutputPlugin plugin) =>
        new(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });

    private static Workspace Import(WorkspaceStore store)
    {
        byte[] zip = TestZips.Build((Root + "first.dat", [1]), (Root + "second.dat", [1]));
        return store.Create("output validation", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
    }

    private static DocumentSession Edit(Workspace workspace, string name, OutputPlugin plugin)
    {
        var file = workspace.Set.AllDiskFiles.Single(f => f.FileName == name);
        var session = workspace.Open(file.Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("value", JsonSerializer.SerializeToElement(2))]).Ok);
        return session;
    }

    private sealed class OutputPlugin(string outputPath) : IGamePlugin
    {
        public string OutputPath => outputPath;
        public PluginInfo Info { get; } = new("test.output-validation", "Output validation", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file) => new OutputDocument(outputPath, file.ReadAllBytes());
    }

    private sealed class OutputDocument(string outputPath, byte[] bytes) : BoundSaveDocument
    {
        public override string Title => "Output validation";
        public override ViewNode BuildView() => Field("value", "Value", IntegerSpec.UInt8,
            () => FieldValue.Of(bytes[0]), value => bytes[0] = (byte)value.AsInt32());
        public override IReadOnlyList<FileOutput> Serialize() => [new(outputPath, bytes)];
    }
}
