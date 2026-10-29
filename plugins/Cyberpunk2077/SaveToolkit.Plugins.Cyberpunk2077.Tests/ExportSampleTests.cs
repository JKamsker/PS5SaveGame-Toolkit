using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ExportSampleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalZipMirrorsOnlyGameplayEditAndPreservesUnrelatedFiles(bool pointTargets)
    {
        var sample = SampleSaves.Load("PPSA04027");
        var (plugin, store, _) = ReferencePackFixture.Embedded();
        using var workspace = new Workspace("export", "Cyberpunk evidence", sample, new PluginCatalog([plugin]), store);
        var file = sample.AllDiskFiles.Single(file => file.FileName == "sav.dat" && !file.Directory.IsBackup);
        var session = workspace.Open(file.Id, plugin.Info.Id);
        _ = session.BuildView();
        if (pointTargets)
        {
            var preview = session.PlanAction(PointTargetActionTests.ActionId, PointTargetActionTests.Targets(12, 9, 3), new());
            Assert.NotNull(preview.PlanId);
            Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        }
        else
        {
            _ = session.QueryCollection("inventory", new QueryableCollectionQuery { Limit = 200 });
            Assert.True(session.ApplyEdits([new("inventory/49/quantity", JsonSerializer.SerializeToElement("601"))]).Ok);
        }
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.Everything });
        Assert.Equal(2, plan.Overrides.Count);
        Assert.All(plan.Overrides.Keys, path => Assert.EndsWith("/sav.dat", path));
        using var bytes = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, bytes);
        bytes.Position = 0;
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        Assert.Equal(sample.AllDiskFiles.Count(), zip.Entries.Count);
        var before = CsavContainer.Parse(file.ReadAllBytes());
        var expected = before.Payload.ToArray();
        var item = InventoryReader.Read(before, CsavTree.Parse(before)).Single(item => item.NodeId == 49);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(item.QuantityOffset!.Value), 601);
        foreach (var original in sample.AllDiskFiles)
        {
            var entry = Assert.IsType<ZipArchiveEntry>(zip.GetEntry(original.RelativePath));
            using var stream = entry.Open();
            using var content = new MemoryStream();
            stream.CopyTo(content);
            if (original.FileName == "sav.dat")
            {
                var exported = CsavContainer.Parse(content.ToArray());
                if (pointTargets)
                    PointTargetFixtures.AssertOnlyUnspentChanges(before, exported, PointTargetActionTests.TargetValues());
                else
                    Assert.True(expected.AsSpan().SequenceEqual(exported.Payload), "Exported gameplay payload differs from the intended four-byte patch.");
            }
            else
            {
                Assert.True(original.ReadAllBytes().AsSpan().SequenceEqual(content.ToArray()), "An unrelated exported file changed.");
            }
        }
    }
}
