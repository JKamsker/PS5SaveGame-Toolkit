using System.Text.Json.Nodes;
using SaveToolkit.Testing.Ff7Browser;

namespace SaveToolkit.Ff7Browser.Tests;

[Collection(Ff7ApiCollection.Name)]
public sealed class Ff7ResolutionTests(Ff7ApiHost host)
{
    [Theory]
    [InlineData("PPSA01911", 0)]
    [InlineData("PPSA01911", 1)]
    [InlineData("PPSA01911", 2)]
    [InlineData("PPSA08664", 0)]
    [InlineData("PPSA08664", 1)]
    [InlineData("PPSA08664", 2)]
    public async Task SelectedItemReresolvesMovedZeroOneOrTwoCurrentStacks(string title, int matches)
    {
        var binary = Ff7Binary.Create(title);
        await using var source = await Ff7ApiDocument.Open(host, binary.InventoryMatches(0));
        var preset = await source.Capture(Ff7Actions.Restock, new { quantity = "99" }, "selected", ["inventory/0"]);
        var destination = binary.InventoryMatches(matches == 0 ? [] : matches == 1 ? [7] : [1, 7]);
        await using var current = await Ff7ApiDocument.Open(host, destination);
        var before = await current.Snapshot();
        var preview = await current.Preview(preset);
        var resolution = Assert.Single(preview.Body["preview"]!["resolution"]!["instances"]!.AsArray())!;
        await current.Unchanged(before);
        Assert.Equal(matches == 0 ? "Missing" : matches == 1 ? "Resolved" : "Ambiguous", resolution["status"]!.GetValue<string>());
        var candidates = resolution["candidates"]!.AsArray();
        Assert.Equal(matches, candidates.Count);
        if (matches == 0)
        {
            Assert.Equal(422, preview.Status);
            Assert.Null(preview.Body["preview"]!["planId"]);
            return;
        }
        if (matches == 2)
        {
            Assert.Equal(422, preview.Status);
            Assert.Null(preview.Body["preview"]!["planId"]);
            var requested = resolution["requested"]!["stableId"]!.GetValue<string>();
            var invalid = await current.Preview(preset, choices: new Dictionary<string, string> { [requested] = "not-presented" });
            Assert.Equal(422, invalid.Status);
            await current.Unchanged(before);
            var chosen = candidates.Single(node => node!["path"]!.GetValue<string>() == "inventory/7")!["stableId"]!.GetValue<string>();
            preview = await current.Preview(preset, choices: new Dictionary<string, string> { [requested] = chosen });
            Assert.Equal(200, preview.Status);
            var resolved = Assert.Single(preview.Body["preview"]!["resolution"]!["instances"]!.AsArray())!;
            Assert.Equal("Resolved", resolved["status"]!.GetValue<string>());
            Assert.Equal(chosen, Assert.Single(resolved["candidates"]!.AsArray())!["stableId"]!.GetValue<string>());
        }
        else Assert.Equal(200, preview.Status);
        var changes = preview.Body["preview"]!["plan"]!["changes"]!.AsArray();
        Assert.Equal("inventory/7/quantity", Assert.Single(changes)!["valueId"]!.GetValue<string>());
        var expected = destination.Gameplay.ToArray();
        destination.Write(expected, "inventory/7/quantity", 99);
        Assert.Equal(200, (await current.Apply(preset, preview.Body)).Status);
        await current.AssertBytes(expected);
        Assert.Equal(200, (await host.Send(current.Path + "/undo", new { revision = 1 })).Status);
        Assert.False((await host.Get(current.Path))["state"]!["canUndo"]!.GetValue<bool>());
        await current.AssertBytes(destination.Gameplay);
        Assert.Equal(200, (await host.Send(current.Path + "/redo", new { revision = 2 })).Status);
        await current.AssertBytes(expected);
        await using var reopened = await Ff7ApiDocument.Open(host, destination.WithGameplay(expected));
        await reopened.AssertBytes(expected);
    }

    [Theory]
    [InlineData("PPSA08664", "moved", "Resolved")]
    [InlineData("PPSA18936", "moved", "Resolved")]
    [InlineData("PPSA08664", "missing", "Unsupported")]
    [InlineData("PPSA08664", "duplicate-detail", "Unsupported")]
    [InlineData("PPSA08664", "duplicate-inventory", "Unsupported")]
    [InlineData("PPSA08664", "invalid-count", "Unsupported")]
    [InlineData("PPSA08664", "wrong-type", "Unsupported")]
    public async Task OwnedMateriaUsesNativeInstanceAndCurrentJoinWithoutOrphanWrites(string title, string variant, string status)
    {
        var binary = Ff7Binary.Create(title);
        await using var source = await Ff7ApiDocument.Open(host, binary);
        var preset = await source.Capture(Ff7Actions.Stars, Ff7Actions.Parameters(Ff7Actions.Stars, binary), "selected", ["materia/0"]);
        var destination = binary.OwnedVariant(variant);
        await using var current = await Ff7ApiDocument.Open(host, destination);
        var before = await current.Snapshot();
        var preview = await current.Preview(preset);
        await current.Unchanged(before);
        var resolution = Assert.Single(preview.Body["preview"]!["resolution"]!["instances"]!.AsArray())!;
        Assert.Equal(status, resolution["status"]!.GetValue<string>());
        if (status != "Resolved")
        {
            Assert.Equal(422, preview.Status);
            Assert.Empty(resolution["candidates"]!.AsArray());
            Assert.Null(preview.Body["preview"]!["planId"]);
            var rejection = await host.Send($"{current.Path}/presets/{preset["id"]!.GetValue<string>()}/apply", new { revision = 0, planId = "unavailable" });
            Assert.Equal(409, rejection.Status);
            await current.Unchanged(before);
            return;
        }
        Assert.Equal(200, preview.Status);
        Assert.Equal("materia/7", Assert.Single(resolution["candidates"]!.AsArray())!["path"]!.GetValue<string>());
        var changes = preview.Body["preview"]!["plan"]!["changes"]!.AsArray();
        Assert.All(changes, change => Assert.StartsWith("materia/7/", change!["valueId"]!.GetValue<string>(), StringComparison.Ordinal));
        var expected = destination.Gameplay.ToArray();
        destination.Write(expected, "materia/7/ap", destination.SecondTierAp);
        destination.Write(expected, "materia/7/level", 2);
        Assert.Equal(200, (await current.Apply(preset, preview.Body)).Status);
        await current.AssertBytes(expected);
        Assert.Equal(200, (await host.Send(current.Path + "/undo", new { revision = 1 })).Status);
        await current.AssertBytes(destination.Gameplay);
        Assert.False((await host.Get(current.Path))["state"]!["canUndo"]!.GetValue<bool>());
        Assert.Equal(200, (await host.Send(current.Path + "/redo", new { revision = 2 })).Status);
        await current.AssertBytes(expected);
        await using var reopened = await Ff7ApiDocument.Open(host, destination.WithGameplay(expected));
        await reopened.AssertBytes(expected);
    }
}
