using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class ExportMirrorSafetyTests
{
    private const string Primary = "PPSA99990/sdimg_SAVE/";
    private const string Backup = "PPSA99990/sdimg_sce_bu_SAVE/";

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public void Export_ValidatesGroupDependenciesAgainstMirroredOutputs(int requiredBalance, bool valid)
    {
        var plugin = new GroupPlugin { RequiredOutsideBalance = 20 };
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var zip = TestZips.Build(
            (Backup + "character.dat", GroupPlugin.Create("C", 10)),
            (Backup + "profile.dat", GroupPlugin.Create("P", 20)),
            (Backup + "outside.dat", GroupPlugin.Create("P", 20)),
            (Primary + "outside.dat", GroupPlugin.Create("P", 20)));
        var workspace = store.Create("mirror dependencies", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.RelativePath == Backup + "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        var group = workspace.OpenGroup(offer.Token);
        _ = group.Session.BuildView();
        Assert.True(group.Session.ApplyEdits([Edit("character/balance", 11)]).Ok);
        var outside = workspace.Set.AllDiskFiles.Single(f => f.RelativePath == Primary + "outside.dat");
        var independent = workspace.Open(outside.Id, GroupPlugin.PluginId);
        _ = independent.BuildView();
        Assert.True(independent.ApplyEdits([Edit("balance", 21)]).Ok);

        plugin.RequiredOutsideBalance = requiredBalance;
        var options = new ExportOptions { Scope = ExportScope.ChangedFiles };
        if (valid)
        {
            var plan = WorkspaceExporter.Plan(workspace, options);
            Assert.Equal(4, plan.Files.Count);
            Assert.Equal(GroupPlugin.Create("P", 21), plan.Overrides[Backup + "outside.dat"].ToArray());
        }
        else
        {
            var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, options));
            Assert.Contains("complete final overlay", error.Message, StringComparison.Ordinal);
        }

        Assert.True(group.Session.State.IsDirty);
        Assert.True(independent.State.IsDirty);
        Assert.Equal(GroupPlugin.Create("P", 20), outside.ReadAllBytes());
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value.ToString()));
}
