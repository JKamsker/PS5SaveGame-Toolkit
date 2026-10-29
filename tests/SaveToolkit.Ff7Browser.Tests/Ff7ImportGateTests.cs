using System.Net.Http.Json;
using SaveToolkit.Testing.Ff7Browser;

namespace SaveToolkit.Ff7Browser.Tests;

[Collection(Ff7ApiCollection.Name)]
public sealed class Ff7ImportGateTests(Ff7ApiHost host)
{
    private static readonly int[] OpenRejectionStatuses = [400, 415, 422];
    [Theory]
    [InlineData("title")]
    [InlineData("role")]
    [InlineData("length")]
    [InlineData("first-header")]
    [InlineData("linked-record")]
    public async Task ProbeAndOpenRejectMalformedBinaryThroughRealZipAndFileApi(string mutation)
    {
        var original = Ff7Binary.Create(Ff7Actions.Titles[0]);
        var bytes = original.Gameplay.ToArray();
        switch (mutation)
        {
            case "length": bytes = bytes[..^1]; break;
            case "first-header": bytes[0] ^= 1; break;
            case "linked-record": bytes[original.NextRecordOffset + 4] ^= 1; break;
        }
        var binary = original.WithGameplay(bytes).WithIdentity(
            mutation == "title" ? "PPSA99999" : original.TitleId,
            mutation == "role" ? "unrelated000" : original.GameplayName);
        var library = (await host.Get("/api/presets")).ToJsonString();
        var (workspace, file, info) = await Ff7ApiDocument.Import(host, binary);
        var matched = info["matches"]!.AsArray().Any(node => node!["pluginId"]!.GetValue<string>() == "ff7r");
        Assert.Equal(mutation == "linked-record", matched);
        var opened = await host.Send($"{workspace}/files/{file}/open", new { pluginId = "ff7r" });
        Assert.Contains(opened.Status, OpenRejectionStatuses);
        Assert.Empty((await host.Get(workspace))["documents"]!.AsArray());
        Assert.Equal(library, (await host.Get("/api/presets")).ToJsonString());
        await using var imported = new Ff7ApiDocument(host, workspace, "unused", binary);
        var members = await imported.Export();
        Assert.Equal(2, members.Count);
        Assert.Equal(bytes, members[binary.GameplayPath]);
        Assert.Equal(binary.Common, members[binary.CommonPath]);
    }

    [Fact]
    public async Task UnwritableHostStorageRejectsCaptureAndPreservesLibraryAndDocument()
    {
        await using var document = await Ff7ApiDocument.Open(host, Ff7Binary.Create(Ff7Actions.Titles[0]));
        _ = await document.Capture(Ff7Actions.Restock, new { quantity = "99" });
        var before = await document.Snapshot();
        var storage = Path.GetFullPath(Path.Combine(host.Root, "presets"));
        var retained = Path.GetFullPath(Path.Combine(host.Root, "presets-retained"));
        Assert.Equal(Path.GetFullPath(host.Root), Path.GetDirectoryName(storage));
        Assert.Equal(Path.GetFullPath(host.Root), Path.GetDirectoryName(retained));
        var stored = Directory.GetFiles(storage).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);
        Directory.Move(storage, retained);
        try
        {
            await File.WriteAllTextAsync(storage, "Authored storage failure sentinel.", Ff7ApiHost.Ct);
            var rejected = await host.Send(document.Path + "/presets/capture", new
            {
                name = "Storage blocked", actionId = Ff7Actions.Restock, parameters = new { quantity = "99" },
            });
            Assert.Equal(503, rejected.Status);
        }
        finally
        {
            File.Delete(storage);
            Directory.Move(retained, storage);
        }
        await document.Unchanged(before);
        foreach (var (name, content) in stored) Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(storage, name!), Ff7ApiHost.Ct));
    }
}
