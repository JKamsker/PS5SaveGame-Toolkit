using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroExportSafetyTests
{
    private const string CoinAction = "common/set-coins";
    private const string BudgetAction = "common/top-up-gatcha";
    private const string CollectAction = "collectibles/collect-all";
    private const string WorldAction = "collectibles/collect-world";
    private const string MemoryWorld = "World/MEMORY_HEAVEN";

    [Theory]
    [InlineData(0, CoinAction)]
    [InlineData(1, CoinAction)]
    [InlineData(0, BudgetAction)]
    [InlineData(1, BudgetAction)]
    [InlineData(0, CollectAction)]
    [InlineData(1, CollectAction)]
    [InlineData(0, WorldAction)]
    [InlineData(1, WorldAction)]
    public void PresetExportMirrorsOnlyItsGenerationAndUndoRemovesAllOverrides(int generation, string actionId)
    {
        var (plugin, packs, _) = AstroCatalogueTestData.References();
        var set = SampleSaves.Load("PPSA01325");
        using var workspace = new Workspace("astro-export", "ASTRO write review", set, new PluginCatalog([plugin]), packs);
        var primary = Allocation(set, generation, backup: false);
        var backup = Allocation(set, generation, backup: true);
        var originals = AstroTestData.Samples().ToDictionary(file => file.RelativePath, file => file.ReadAllBytes());
        var session = workspace.Open(primary.Id, plugin.Info.Id);
        _ = session.BuildView();
        var parameters = actionId switch
        {
            CoinAction => Parameters("amount", 1700),
            BudgetAction => Parameters("attempts", 17),
            WorldAction => Parameters("world", MemoryWorld),
            _ => new Dictionary<string, JsonElement>()
        };

        var preview = session.PlanAction(actionId, parameters, expectedRevision: session.State.Revision);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Contains(preview.Plan.Warnings, warning => warning.Contains("not validated on console", StringComparison.Ordinal));
        Assert.Contains(preview.Plan.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize());
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        Assert.True(applied.Ok);
        Assert.Equal(preview.Plan.Warnings, applied.Warnings);
        Assert.Equal(PluginMaturity.Experimental, plugin.Info.Maturity);

        var options = new ExportOptions { Scope = ExportScope.ChangedFiles };
        var plan = WorkspaceExporter.Plan(workspace, options);
        // Generation 1 already exceeds the requested budget: preserve it without dirtying/exporting.
        if (preview.Plan.Changes.Count == 0)
        {
            Assert.Equal(BudgetAction, actionId);
            Assert.Equal(1, generation);
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);
            Assert.Empty(plan.Files);
            Assert.Empty(plan.Overrides);
            return;
        }

        var edited = Assert.Single(session.Serialize()).Content.ToArray();
        var paths = new[] { primary.RelativePath, backup.RelativePath }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(paths, plan.Overrides.Keys.Order(StringComparer.Ordinal));
        AssertZip(plan, paths, edited);
        using (var reopened = plugin.Open(AstroTestData.File(edited)))
        {
            Assert.All(preview.Plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
        }

        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.Serialize());
        var undone = WorkspaceExporter.Plan(workspace, options);
        Assert.Empty(undone.Files);
        Assert.Empty(undone.Overrides);
        Assert.True(session.Redo().Ok);
        AssertZip(WorkspaceExporter.Plan(workspace, options), paths, edited);
        Assert.True(session.Undo().Ok);
        Assert.All(AstroTestData.Samples(), file => Assert.Equal(originals[file.RelativePath], file.ReadAllBytes()));
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(1, false, false, false)]
    [InlineData(0, true, true, false)]
    [InlineData(1, true, true, false)]
    [InlineData(0, true, false, true)]
    [InlineData(1, true, false, true)]
    public void ExportRespectsDisabledMirroringOpenBackupOwnershipAndBackupOnlyEdits(
        int generation, bool mirrorBackups, bool openBackup, bool editBackup)
    {
        var plugin = new AstroPlayroomPlugin();
        var set = SampleSaves.Load("PPSA01325");
        using var workspace = new Workspace("astro-backup", "ASTRO backup review", set, new PluginCatalog([plugin]));
        var file = Allocation(set, generation, editBackup);
        if (openBackup)
        {
            var cleanBackup = workspace.Open(Allocation(set, generation, backup: true).Id, plugin.Info.Id);
            _ = cleanBackup.BuildView();
            Assert.False(cleanBackup.State.IsDirty);
        }

        var session = workspace.Open(file.Id, plugin.Info.Id);
        _ = session.BuildView();
        var coins = Assert.Single(Assert.IsType<TabsNode>(session.BuildView()).Tabs
            .Single(tab => tab.Title == "Overview").Children.OfType<FieldNode>(),
            field => field.Description == AstroTestData.CoinsPath);
        Assert.True(session.ApplyEdits([new IncomingEdit(coins.ValueId, JsonSerializer.SerializeToElement(42))]).Ok);
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions
        {
            Scope = ExportScope.ChangedFiles, MirrorBackups = mirrorBackups
        });
        Assert.Equal(file.RelativePath, Assert.Single(plan.Overrides).Key);
        AssertZip(plan, [file.RelativePath], Assert.Single(session.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.Serialize());
    }

    private static DiskSaveFile Allocation(DiskSaveSet set, int generation, bool backup) =>
        Assert.Single(set.AllDiskFiles, file => file.PathInDirectory == "memory.dat" &&
            file.Directory.Generation == generation && file.Directory.IsBackup == backup);

    private static Dictionary<string, JsonElement> Parameters<T>(string key, T value) =>
        new() { [key] = JsonSerializer.SerializeToElement(value) };

    private static void AssertZip(ExportPlan plan, IReadOnlyList<string> paths, byte[] expected)
    {
        Assert.Equal(paths, plan.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal));
        Assert.All(plan.Overrides.Values, bytes => Assert.Equal(expected, bytes.ToArray()));
        using var output = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, output);
        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal(paths, archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            Assert.Equal(expected, bytes.ToArray());
        }
    }
}
