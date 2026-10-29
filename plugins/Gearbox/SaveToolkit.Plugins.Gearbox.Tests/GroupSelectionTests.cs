using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GroupSelectionTests
{
    [Theory]
    [InlineData("PPSA01463", "items/match-level", 0)]
    [InlineData("PPSA01493", "items/match-level", 0)]
    [InlineData("PPSA01463", "items/mark", 1)]
    [InlineData("PPSA01493", "items/mark", 1)]
    [InlineData("PPSA01463", "items/mark", 2)]
    [InlineData("PPSA01493", "items/mark", 2)]
    public void ItemPreviews_RejectAmmoPrefixedItemIdentityWithoutPublishingToken(string title, string action, int mark)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var file = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var source = (GearboxDocument)plugin.Open(file);
        var item = source.Inventory.Entries.First(entry => source.Inventory.Info(entry.Id).Complete &&
            entry.Field.Message is { } message && GearboxItemMarks.Supported(message) &&
            (action != "items/mark" || GearboxItemMarks.Get(message) != mark));
        using var workspace = new Workspace("foreign-group-selection", "Group selection", set,
            new PluginCatalog([plugin]), new ReferencePackStore([plugin]));
        var session = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(file.Id, "gearbox")).Token).Session;
        _ = session.BuildView();
        if (action == "items/match-level")
            Assert.True(session.ApplyEdits([new("character/character/level", JsonSerializer.SerializeToElement("17"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        _ = session.BuildView();
        _ = session.QueryCollection("character/inventory_items", new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        _ = session.Expand("character/detail/" + item.Id);
        var parameters = new Dictionary<string, JsonElement>();
        if (action == "items/mark") parameters["mark"] = JsonSerializer.SerializeToElement(mark);
        var before = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
        foreach (var row in new[] { "character/ammo/" + item.Id, "character/bank_inventory_list/" + item.Id,
            "profile/inventory_items/" + item.Id, "character/inventory_items/extra/" + item.Id, "character/ammo/0" })
        {
            var preview = session.PlanAction("character/" + action, parameters, new([row]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.All(session.Serialize(cancellationToken: TestContext.Current.CancellationToken), output => Assert.Equal(before[output.RelativePath], output.Content.ToArray()));
            Assert.NotEmpty(preview.Plan.Errors);
            Assert.Null(preview.PlanId);
        }

        var valid = session.PlanAction("character/" + action, parameters, new(["character/inventory_items/" + item.Id]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(valid.Plan.Errors);
        Assert.NotEmpty(valid.Plan.Changes);
        Assert.NotNull(valid.PlanId);
        Assert.All(session.Serialize(cancellationToken: TestContext.Current.CancellationToken), output => Assert.Equal(before[output.RelativePath], output.Content.ToArray()));
    }
}
