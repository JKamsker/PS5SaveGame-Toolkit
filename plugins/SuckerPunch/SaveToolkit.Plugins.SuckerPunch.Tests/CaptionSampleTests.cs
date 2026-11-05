using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CaptionSampleTests
{
    [Fact]
    public void EveryEligibleRealDirectoryCaptionPreservesGameplayOwnershipAndHistory()
    {
        var plugin = new SuckerPunchPlugin();
        var set = SampleSaves.Load("PPSA02225");
        var count = 0;
        foreach (var file in set.AllDiskFiles.Where(file => plugin.Probe(file).IsMatch))
        {
            using var document = plugin.Open(file);
            _ = document.BuildView();
            var owner = Assert.IsAssignableFrom<IAdditionalOutputOwner>(document);
            foreach (var output in document.Serialize())
                Assert.Equal(set.FindAnyFile(output.RelativePath)!.ReadAllBytes(), output.Content.ToArray());
            if (owner.AdditionalOutputPaths.Count == 0) continue;
            using var workspace = new Workspace("caption-samples", "Caption samples", set,
                new PluginCatalog([plugin]), new ReferencePackStore([plugin]));
            var session = workspace.Open(file.Id, "suckerpunch");
            _ = session.BuildView();
            var primary = file.ReadAllBytes();
            var sfo = file.Directory.FindAnyFile(Assert.Single(owner.AdditionalOutputPaths))!;
            var original = sfo.ReadAllBytes();
            var state = session.State;
            const string caption = "Review α";
            var preview = CaptionWorkspace.Plan(session, caption);
            Assert.Empty(preview.Plan.Errors);
            Assert.Single(preview.Plan.Changes);
            Assert.Equal(state, session.State);
            Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
            CaptionWorkspace.Apply(session, caption);
            var outputs = session.Serialize(TestContext.Current.CancellationToken)
                .ToDictionary(output => output.RelativePath, output => output.Content);
            Assert.Equal(2, outputs.Count);
            Assert.Equal(primary, outputs[file.RelativePath].ToArray());
            Assert.NotEqual(original, outputs[sfo.RelativePath].ToArray());
            Assert.Equal(original.Length, outputs[sfo.RelativePath].Length);
            var overlay = new OverlaySaveSet(set, outputs);
            using var reopened = plugin.Open(((ISaveSet)overlay).FindFile(file.RelativePath)!);
            _ = reopened.BuildView();
            Assert.Equal(caption, reopened.GetValue(CaptionTestData.ValueId).AsString());
            var export = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.Everything }, TestContext.Current.CancellationToken);
            Assert.Equal(primary, export.Overrides[file.RelativePath].ToArray());
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.IsDirty);
            Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
            Assert.True(session.Redo().Ok);
            Assert.Equal(outputs[sfo.RelativePath].ToArray(), session.Serialize(TestContext.Current.CancellationToken)
                .Single(output => output.RelativePath == sfo.RelativePath).Content.ToArray());
            count++;
        }
        Assert.True(count > 0, "No real directory exercised the caption writer.");
        TestContext.Current.TestOutputHelper!.WriteLine($"{count} real caption owners exercised; every sample no-op checked.");
    }
}
