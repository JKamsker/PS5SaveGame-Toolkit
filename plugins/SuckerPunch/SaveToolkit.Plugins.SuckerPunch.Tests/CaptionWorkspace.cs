using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

internal sealed class CaptionWorkspace : IDisposable
{
    internal readonly WorkspaceStore Store;
    internal readonly Workspace Workspace;
    internal readonly byte[] Sfo;
    internal readonly byte[] Primary;
    internal const string Twin = "PPSA02225/sdimg_sce_bu_CAPTION/";
    internal const string Other = "PPSA02225/sdimg_OTHER/";

    internal CaptionWorkspace(bool twin = false, bool changedTwin = false, bool secondary = false)
    {
        Sfo = CaptionTestData.Sfo();
        Primary = new SyntheticSave(false).Bytes;
        var files = new Dictionary<string, byte[]>
        {
            [CaptionTestData.Root + "dis.dat"] = Primary,
            [CaptionTestData.Root + CaptionTestData.SfoPath] = Sfo,
            [CaptionTestData.Root + "other.bin"] = [0x12, 0x34],
            [CaptionTestData.Root + "sce_sys/keystone"] = [0x56, 0x78],
            [Other + "dis.dat"] = Primary,
            [Other + CaptionTestData.SfoPath] = Sfo,
        };
        if (secondary) files.Add(CaptionTestData.Root + "memory.dat", Primary);
        if (twin)
        {
            files.Add(Twin + "dis.dat", Primary);
            files.Add(Twin + CaptionTestData.SfoPath, changedTwin ? CaptionTestData.Sfo("Other") : Sfo);
        }
        var folder = Path.Combine(TestScratch.ProcessFolder, "caption-" + Guid.NewGuid().ToString("N"));
        Store = new WorkspaceStore(new PluginCatalog([new SuckerPunchPlugin()]), new() { DataRoot = folder });
        var zip = CaptionTestData.Zip(files);
        Workspace = Store.Create("synthetic manual caption", [new ImportSource("caption.zip", () => new MemoryStream(zip))]);
    }

    internal DocumentSession Open(string path = CaptionTestData.Root + "dis.dat") =>
        Workspace.Open(Workspace.Set.AllDiskFiles.Single(file => file.RelativePath == path).Id, "suckerpunch");

    internal static ActionPreview Plan(DocumentSession session, string text) => session.PlanAction(CaptionTestData.ActionId,
        new Dictionary<string, JsonElement> { ["caption"] = JsonSerializer.SerializeToElement(text) });

    internal static void Apply(DocumentSession session, string text)
    {
        var preview = Plan(session, text);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        var applied = session.ApplyActionPlan(preview.PlanId, preview.Revision);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors.Select(error => error.Message)));
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Store.Dispose();
    }
}
