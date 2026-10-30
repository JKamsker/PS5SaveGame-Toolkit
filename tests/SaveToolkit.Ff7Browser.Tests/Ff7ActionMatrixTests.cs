using SaveToolkit.Testing.Ff7Browser;

namespace SaveToolkit.Ff7Browser.Tests;

[Collection(Ff7ApiCollection.Name)]
public sealed class Ff7ActionMatrixTests(Ff7ApiHost host)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var title in Ff7Actions.Titles)
        {
            foreach (var action in Ff7Actions.All)
            {
                yield return [title, action, "document", false];
                yield return [title, action, action is Ff7Actions.Ap or Ff7Actions.Stars ? "allCopies" : "allEligible", false];
                // Remake has no proven owned materia instance identity; Selected is not advertised there.
                if (title == Ff7Actions.Titles[0] && action is Ff7Actions.Ap or Ff7Actions.Stars) continue;
                yield return [title, action, "selected", false];
                yield return [title, action, "selected", true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TypedActionsUseRealImportF6HistoryWholeExportAndReimport(string title, string action, string scope, bool empty)
    {
        var binary = Ff7Binary.Create(title);
        if (action == Ff7Actions.Restock && scope == "selected" && !empty) binary = binary.InventoryMatches(0);
        await using var document = await Ff7ApiDocument.Open(host, binary);
        var initial = await host.Get(document.Path);
        var capability = initial["presetCapability"]!;
        Assert.Equal(5, capability["actions"]!.AsArray().Count);
        Assert.Null(capability["gameBuild"]);
        Assert.False(string.IsNullOrEmpty(capability["referencePackIdentity"]!.GetValue<string>()));
        Assert.Equal("gameplay", capability["fileRole"]!.GetValue<string>());
        var descriptor = capability["actions"]!.AsArray().Single(node => node!["actionId"]!.GetValue<string>() == action)!;
        Assert.Contains(scope, descriptor["scopeKinds"]!.AsArray().Select(node => node!.GetValue<string>()));

        var preset = await document.Capture(action, Ff7Actions.Parameters(action, binary), scope,
            scope == "selected" ? empty ? [] : Ff7Actions.Selected(action) : null);
        Assert.Null(preset["compatibility"]!["gameBuild"]);
        Assert.Equal(capability["referencePackIdentity"]!.GetValue<string>(), preset["compatibility"]!["referencePackIdentity"]!.GetValue<string>());
        Assert.Equal(scope, preset["actions"]![0]!["scopeKind"]!.GetValue<string>());
        if (scope == "selected" && empty) Assert.Empty(preset["actions"]![0]!["instances"]!.AsArray());
        var beforePreview = await document.Snapshot();
        var preview = await document.Preview(preset);
        Assert.Equal(200, preview.Status);
        await document.Unchanged(beforePreview);
        var changes = preview.Body["preview"]!["plan"]!["changes"]!.AsArray();
        var expected = Ff7ActionOracle.Expected(binary, action, scope, empty, changes);
        var apply = await document.Apply(preset, preview.Body);
        Assert.Equal(200, apply.Status);
        Assert.True(apply.Body["ok"]!.GetValue<bool>());
        await document.AssertBytes(expected);
        if (empty)
        {
            Assert.Equal(0, apply.Body["state"]!["revision"]!.GetValue<long>());
            await document.Unchanged(beforePreview);
            return;
        }

        Assert.Equal(1, apply.Body["state"]!["revision"]!.GetValue<long>());
        Assert.True(apply.Body["state"]!["canUndo"]!.GetValue<bool>());
        Assert.False(apply.Body["state"]!["canRedo"]!.GetValue<bool>());
        Assert.Equal(200, (await host.Send(document.Path + "/undo", new { revision = 1 })).Status);
        var undone = (await host.Get(document.Path))["state"]!;
        Assert.Equal(2, undone["revision"]!.GetValue<long>());
        Assert.False(undone["canUndo"]!.GetValue<bool>());
        Assert.True(undone["canRedo"]!.GetValue<bool>());
        await document.AssertBytes(binary.Gameplay);
        Assert.Equal(200, (await host.Send(document.Path + "/redo", new { revision = 2 })).Status);
        Assert.Equal(3, (await host.Get(document.Path))["state"]!["revision"]!.GetValue<long>());
        await document.AssertBytes(expected);
        await using var reopened = await Ff7ApiDocument.Open(host, binary.WithGameplay(expected));
        await reopened.AssertBytes(expected);
        var rePreview = await reopened.Preview(preset);
        Assert.Equal(200, rePreview.Status);
        Assert.Empty(rePreview.Body["preview"]!["plan"]!["changes"]!.AsArray());
    }
}
