using System.Text.Json.Nodes;
using SaveToolkit.Testing.Ff7Browser;

namespace SaveToolkit.Ff7Browser.Tests;

[Collection(Ff7ApiCollection.Name)]
public sealed class Ff7BoundaryTests(Ff7ApiHost host)
{
    [Theory]
    [InlineData("pluginId", "foreign")]
    [InlineData("pluginVersion", "0.0.0")]
    [InlineData("gameBuild", "unproven-build")]
    [InlineData("formatVariant", "rebirth-demo")]
    [InlineData("referencePackIdentity", "foreign-content")]
    [InlineData("schemaFingerprint", "foreign-schema")]
    [InlineData("fileRole", "common")]
    [InlineData("capabilitySchemaVersion", "foreign-capability")]
    [InlineData("presetKind", "foreign-kind")]
    public async Task CapturedBoundariesAreAuthoritativeAndRejectionPreservesAllState(string property, string replacement)
    {
        await using var document = await Ff7ApiDocument.Open(host, Ff7Binary.Create(Ff7Actions.Titles[1]));
        var captured = await document.Capture(Ff7Actions.Restock, new { quantity = "99" });
        var compatibility = captured["compatibility"]!.DeepClone();
        compatibility[property] = replacement;
        var created = await host.Send("/api/presets", new { name = "Wrong " + property, compatibility, actions = captured["actions"]!.DeepClone() });
        Assert.Equal(200, created.Status);
        var before = await document.Snapshot();
        Assert.Equal(422, (await document.Preview(created.Body)).Status);
        Assert.Equal(422, (await host.Send($"{document.Path}/presets/{created.Body["id"]!.GetValue<string>()}/apply", new { planId = "unavailable", revision = 0 })).Status);
        await document.Unchanged(before);
    }

    [Fact]
    public async Task EqualLengthRetailDemoAndReadOnlyCommonNeverCrossCompatibility()
    {
        var retail = Ff7Binary.Create(Ff7Actions.Titles[1]);
        var demo = Ff7Binary.Create(Ff7Actions.Titles[2]);
        Assert.Equal(retail.Gameplay.Length, demo.Gameplay.Length);
        await using var source = await Ff7ApiDocument.Open(host, retail);
        var captured = await source.Capture(Ff7Actions.Recover, new { });
        await using var destination = await Ff7ApiDocument.Open(host, demo);
        var before = await destination.Snapshot();
        Assert.Equal(422, (await destination.Preview(captured)).Status);
        await destination.Unchanged(before);
        await using var common = await Ff7ApiDocument.Open(host, retail, retail.CommonName);
        Assert.False((await host.Get(common.Path))["canEdit"]!.GetValue<bool>());
        var commonBefore = await common.Snapshot();
        Assert.Equal(422, (await host.Send(common.Path + "/presets/capture", new { name = "Unavailable", actionId = Ff7Actions.Recover, parameters = new { } })).Status);
        Assert.Equal(422, (await common.Preview(captured)).Status);
        await common.Unchanged(commonBefore);
    }

    [Theory]
    [InlineData("unknown-type")]
    [InlineData("number-type")]
    [InlineData("wrong-scope")]
    [InlineData("quantity-bool")]
    [InlineData("level-range")]
    [InlineData("unknown-action")]
    [InlineData("foreign-selected")]
    public async Task InvalidCaptureNeverWritesBytesHistoryOrLibrary(string variant)
    {
        var binary = Ff7Binary.Create(Ff7Actions.Titles[1]);
        await using var document = await Ff7ApiDocument.Open(host, binary);
        var request = JsonNode.Parse("{\"name\":\"Rejected\",\"actionId\":\"inventory/restock-existing\",\"parameters\":{\"quantity\":\"99\"},\"scopeKind\":\"document\"}")!;
        switch (variant)
        {
            case "unknown-type": request["actionId"] = Ff7Actions.Stars; request["parameters"] = new JsonObject { ["type"] = "3735928559", ["stars"] = "2" }; break;
            case "number-type": request["actionId"] = Ff7Actions.Stars; request["parameters"] = new JsonObject { ["type"] = binary.MateriaType, ["stars"] = "2" }; break;
            case "wrong-scope": request["scopeKind"] = "allCopies"; break;
            case "quantity-bool": request["parameters"]!["quantity"] = true; break;
            case "level-range": request["actionId"] = Ff7Actions.Level; request["parameters"] = new JsonObject { ["level"] = "71" }; break;
            case "unknown-action": request["actionId"] = "not-an-action"; break;
            case "foreign-selected": request["scopeKind"] = "selected"; request["selectedRowKeys"] = new JsonArray("materia/0"); break;
        }
        var before = await document.Snapshot();
        Assert.Equal(422, (await host.Send(document.Path + "/presets/capture", request)).Status);
        await document.Unchanged(before);
    }

    [Fact]
    public async Task StaleRevisionRenamedPresetAndDeletedPresetCannotApplyOldPlans()
    {
        await using var document = await Ff7ApiDocument.Open(host, Ff7Binary.Create(Ff7Actions.Titles[0]));
        var captured = await document.Capture(Ff7Actions.Restock, new { quantity = "99" });
        var preview = await document.Preview(captured);
        Assert.Equal(200, preview.Status);
        var edited = await host.Send(document.Path + "/edits", new { revision = 0, edits = new[] { new { valueId = "character/1/hp", value = "50" } } });
        Assert.Equal(200, edited.Status);
        var before = await document.Snapshot();
        Assert.Equal(409, (await document.Apply(captured, preview.Body)).Status);
        Assert.Equal(409, (await document.Preview(captured)).Status);
        await document.Unchanged(before);
        preview = await document.Preview(captured, 1);
        Assert.Equal(200, preview.Status);
        Assert.Equal(200, (await host.Send($"/api/presets/{captured["id"]!.GetValue<string>()}", new { name = "Changed after preview" }, HttpMethod.Patch)).Status);
        before = await document.Snapshot();
        Assert.Equal(409, (await document.Apply(captured, preview.Body)).Status);
        await document.Unchanged(before);
        preview = await document.Preview(captured, 1);
        Assert.Equal(200, preview.Status);
        Assert.Equal(204, (await host.Send($"/api/presets/{captured["id"]!.GetValue<string>()}", method: HttpMethod.Delete)).Status);
        before = await document.Snapshot();
        Assert.Equal(404, (await document.Apply(captured, preview.Body)).Status);
        await document.Unchanged(before);
    }
}
