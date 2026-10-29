using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class PresetStoreTests
{
    [Fact]
    public void CreateReloadAndRenameKeepExternalVersionedPayload()
    {
        var root = TestZips.TempDir();
        var compatibility = new PresetCompatibility("test.plugin", "1.2.3", ["PPSA00001"], "build-7");
        var action = new PresetAction("set", new Dictionary<string, JsonElement>
        {
            ["value"] = JsonDocument.Parse("42").RootElement.Clone(),
        });
        var created = new PresetStore(new() { StorageRoot = root }).Create(" My preset ", compatibility, [action]);

        var reloaded = new PresetStore(new() { StorageRoot = root });
        var found = reloaded.Find(created.Id);
        Assert.NotNull(found);
        Assert.Equal(PresetStore.CurrentSchemaVersion, found!.SchemaVersion);
        Assert.Equal("My preset", found.Name);
        Assert.Equal(42, found.Actions.Single().Parameters["value"].GetInt32());
        Assert.Equal("Renamed", reloaded.Rename(created.Id, "Renamed").Name);
        Assert.True(File.Exists(Path.Combine(root, "presets.json")));
    }

    [Fact]
    public void RejectsInvalidCompatibilityAndUnknownSchemaOnLoad()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        Assert.Throws<ArgumentException>(() => store.Create("bad", new("p", "1", []), [new("a", new Dictionary<string, JsonElement>())]));
        File.WriteAllText(Path.Combine(root, "presets.json"), "[{\"schemaVersion\":\"99\",\"id\":\"future\"}]");
        Assert.Empty(new PresetStore(new() { StorageRoot = root }).List());
    }

    [Fact]
    public void EnforcesPayloadAndNameBoundsWithoutPartialRecord()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root, MaxPayloadBytes = 64 });
        var compatibility = new PresetCompatibility("p", "1", ["T"]);
        var action = new PresetAction("a", new Dictionary<string, JsonElement>
        {
            ["large"] = JsonDocument.Parse("\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"").RootElement.Clone(),
        });
        Assert.Throws<ArgumentException>(() => store.Create("x", compatibility, [action]));
        Assert.Empty(store.List());
    }
}
