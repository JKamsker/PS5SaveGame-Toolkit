using System.Text.Json.Nodes;
using SaveToolkit.Testing.Ff7Browser;

namespace SaveToolkit.Ff7Browser.Tests;

[Collection(Ff7ApiCollection.Name)]
public sealed class Ff7TamperTests(Ff7ApiHost host)
{
    private static readonly int[] CreationRejectionStatuses = [400, 422];
    [Theory]
    [InlineData("scope")]
    [InlineData("action")]
    [InlineData("parameter")]
    [InlineData("identity")]
    [InlineData("label")]
    [InlineData("duplicate")]
    [InlineData("null-instance")]
    public async Task StoredPayloadTamperingCannotReachAWrite(string mutation)
    {
        await using var document = await Ff7ApiDocument.Open(host, Ff7Binary.Create(Ff7Actions.Titles[0]));
        var captured = await document.Capture(Ff7Actions.Recover, new { }, "selected", ["character/1"]);
        var actions = captured["actions"]!.DeepClone();
        var action = actions[0]!;
        switch (mutation)
        {
            case "scope": action["scopeKind"] = "allCopies"; break;
            case "action": action["actionId"] = Ff7Actions.Restock; action["parameters"] = new JsonObject { ["quantity"] = "99" }; break;
            case "parameter": action["parameters"]!["quantity"] = "99"; break;
            case "identity": action["instances"]![0]!["stableId"] = "ff7:character-index/999"; break;
            case "label": action["instances"]![0]!["label"] = "Invented label"; break;
            case "duplicate": action["instances"]!.AsArray().Add(action["instances"]![0]!.DeepClone()); break;
            case "null-instance": action["instances"]!.AsArray().Add((JsonNode?)null); break;
        }
        var before = await document.Snapshot();
        var created = await host.Send("/api/presets", new { name = "Tampered " + mutation, compatibility = captured["compatibility"]!.DeepClone(), actions });
        if (created.Status != 200)
        {
            Assert.Contains(created.Status, CreationRejectionStatuses);
            await document.Unchanged(before);
            return;
        }
        before = await document.Snapshot();
        var rejected = await document.Preview(created.Body);
        Assert.Equal(422, rejected.Status);
        Assert.Null(rejected.Body["preview"]?["planId"]);
        await document.Unchanged(before);
    }

    [Fact]
    public async Task ForgedChoiceOnUniqueResolutionIsRejectedEvenIfItNamesTheRealCandidate()
    {
        var binary = Ff7Binary.Create(Ff7Actions.Titles[0]).InventoryMatches(0);
        await using var document = await Ff7ApiDocument.Open(host, binary);
        var preset = await document.Capture(Ff7Actions.Restock, new { quantity = "99" }, "selected", ["inventory/0"]);
        var preview = await document.Preview(preset);
        Assert.Equal(200, preview.Status);
        var resolved = Assert.Single(preview.Body["preview"]!["resolution"]!["instances"]!.AsArray())!;
        Assert.Equal("Resolved", resolved["status"]!.GetValue<string>());
        var candidate = Assert.Single(resolved["candidates"]!.AsArray())!["stableId"]!.GetValue<string>();
        var before = await document.Snapshot();
        var choices = new Dictionary<string, string> { [resolved["requested"]!["stableId"]!.GetValue<string>()] = candidate };
        Assert.Equal(422, (await document.Preview(preset, choices: choices)).Status);
        await document.Unchanged(before);
    }

    [Fact]
    public async Task UnsupportedInventoryAllMatchesIdentityNeverPresentsAMateriaCandidate()
    {
        var binary = Ff7Binary.Create(Ff7Actions.Titles[0]).InventoryMatches(0);
        await using var document = await Ff7ApiDocument.Open(host, binary);
        var captured = await document.Capture(Ff7Actions.Restock, new { quantity = "99" }, "selected", ["inventory/0"]);
        var actions = captured["actions"]!.DeepClone();
        var instance = actions[0]!["instances"]![0]!;
        instance["kind"] = instance["kind"]!.GetValue<string>() + ".all-matches";
        var created = await host.Send("/api/presets", new
        {
            name = "Unsupported inventory all-matches", compatibility = captured["compatibility"]!.DeepClone(), actions,
        });
        Assert.Equal(200, created.Status);
        var before = await document.Snapshot();
        var rejected = await document.Preview(created.Body);
        Assert.Equal(422, rejected.Status);
        await document.Unchanged(before);
        var result = Assert.Single(rejected.Body["preview"]!["resolution"]!["instances"]!.AsArray())!;
        Assert.Equal("Unsupported", result["status"]!.GetValue<string>());
        Assert.Empty(result["candidates"]!.AsArray());
        Assert.Null(rejected.Body["preview"]!["planId"]);
    }
}
