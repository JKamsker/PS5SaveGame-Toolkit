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
        Assert.Throws<InvalidDataException>(() => new PresetStore(new() { StorageRoot = root }));
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

    [Fact]
    public void ReturnedAndInputGraphsAreDeepIsolatedAndJsonLifetimeSafe()
    {
        var root = TestZips.TempDir();
        var titleIds = new List<string> { "PPSA00001" };
        using var document = JsonDocument.Parse("42");
        var created = new PresetStore(new() { StorageRoot = root }).Create("safe", new("p", "1", titleIds),
            [new PresetAction("set", new Dictionary<string, JsonElement> { ["value"] = document.RootElement })]);
        titleIds[0] = "MUTATED";
        var found = new PresetStore(new() { StorageRoot = root }).Find(created.Id)!;
        Assert.Equal("PPSA00001", found.Compatibility.TitleIds.Single());
        Assert.Equal(42, found.Actions.Single().Parameters["value"].GetInt32());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)found.Compatibility.TitleIds)[0] = "MUTATED");
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, JsonElement>)found.Actions.Single().Parameters)["value"] = JsonDocument.Parse("9").RootElement);
    }

    [Fact]
    public void WholeStoreAndPreReadFileBoundsAreEnforced()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root, MaxPayloadBytes = 2048 });
        var payload = new PresetAction("set", new Dictionary<string, JsonElement>
        {
            ["value"] = JsonDocument.Parse("\"" + new string('x', 450) + "\"").RootElement.Clone(),
        });
        _ = store.Create("one", new("p", "1", ["T"]), [payload]);
        Assert.Throws<InvalidOperationException>(() => store.Create("two", new("p", "1", ["T"]), [payload]));
        Assert.Single(store.List());
        File.WriteAllBytes(Path.Combine(root, "presets.json"), new byte[2049]);
        Assert.Throws<InvalidDataException>(() => new PresetStore(new() { StorageRoot = root, MaxPayloadBytes = 2048 }));
    }

    [Fact]
    public void ReloadStagesDuplicateReplacementAndPreservesLastGoodLibrary()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        var created = store.Create("safe", new("p", "1", ["T"]),
            [new PresetAction("set", new Dictionary<string, JsonElement>())]);
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "presets.json")));
        var duplicate = JsonSerializer.Serialize(new[] { source.RootElement[0], source.RootElement[0] });
        File.WriteAllText(Path.Combine(root, "presets.json"), duplicate);

        var error = Assert.Throws<InvalidDataException>(() => store.Reload());

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(created.Id, Assert.Single(store.List()).Id);
    }

    [Fact]
    public void ReloadDisappearancePreservesLastGoodLibrary()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        var created = store.Create("safe", new("p", "1", ["T"]),
            [new PresetAction("set", new Dictionary<string, JsonElement>())]);
        File.Delete(Path.Combine(root, "presets.json"));

        Assert.Throws<InvalidDataException>(() => store.Reload());
        Assert.Equal(created.Id, Assert.Single(store.List()).Id);
    }

    [Fact]
    public void ReloadMalformedRecordPreservesLastGoodLibraryAndFile()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        var created = store.Create("safe", new("p", "1", ["T"]),
            [new PresetAction("set", new Dictionary<string, JsonElement>())]);
        var path = Path.Combine(root, "presets.json");
        var original = File.ReadAllText(path);
        File.WriteAllText(path, "[{\"schemaVersion\":\"1\",\"id\":\"\"}]");

        Assert.Throws<InvalidDataException>(() => store.Reload());
        Assert.Equal(created.Id, Assert.Single(store.List()).Id);
        Assert.NotEqual(original, File.ReadAllText(path));
        Assert.Contains("\"id\":\"\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ReloadRejectsUnsupportedScopeAndRetainsLastGoodLibrary()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        var created = store.Create("safe", new("p", "1", ["T"]),
            [new PresetAction("set", new Dictionary<string, JsonElement>())]);
        var path = Path.Combine(root, "presets.json");
        var malformed = File.ReadAllText(path).Replace("\"scopeKind\": \"document\"", "\"scopeKind\": \"session\"", StringComparison.Ordinal);
        File.WriteAllText(path, malformed);

        var error = Assert.Throws<InvalidDataException>(() => store.Reload());

        Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(created.Id, Assert.Single(store.List()).Id);
    }

    [Fact]
    public async Task ReloadSerializesWithMutationPublication()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        _ = store.Create("seed", new("p", "1", ["T"]),
            [new PresetAction("set", new Dictionary<string, JsonElement>())]);
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var reload = Task.Run(store.Reload);
            var created = Task.Run(() => store.Create($"race-{iteration}", new("p", "1", ["T"]),
                [new PresetAction("set", new Dictionary<string, JsonElement>())]));
            await Task.WhenAll(reload, created);
            Assert.Contains(store.List(), item => item.Id == created.Result.Id);
        }

        var renameTarget = store.Create("rename-target", new("p", "1", ["T"]),
            [new PresetAction("set", new Dictionary<string, JsonElement>())]);
        await Task.WhenAll(Task.Run(store.Reload), Task.Run(() => store.Rename(renameTarget.Id, "renamed")));
        Assert.Equal("renamed", store.Find(renameTarget.Id)!.Name);
        await Task.WhenAll(Task.Run(store.Reload), Task.Run(() => store.Delete(renameTarget.Id)));
        Assert.Null(store.Find(renameTarget.Id));
    }

    [Fact]
    public void ReloadReadSharingFailureMapsToStorageException()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        _ = store.Create("locked", new("p", "1", ["T"]),
            [new PresetAction("set", new Dictionary<string, JsonElement>())]);
        var path = Path.Combine(root, "presets.json");
        using var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        handle.Lock(0, 1);

        Assert.Throws<PresetStorageException>(() => store.Reload());
    }

    [Fact]
    public void CrudRefusesToOverwriteALibraryChangedOnDiskUntilReloaded()
    {
        var root = TestZips.TempDir();
        var store = new PresetStore(new() { StorageRoot = root });
        var other = new PresetStore(new() { StorageRoot = root });
        var compatibility = new PresetCompatibility("p", "1", ["T"]);
        PresetAction[] actions = [new("set", new Dictionary<string, JsonElement>())];
        var kept = store.Create("kept", compatibility, actions);
        var path = Path.Combine(root, "presets.json");
        var onDisk = File.ReadAllBytes(path);

        // The other store's view predates that write: writing it back would delete "kept".
        Assert.Equal("presetLibraryReloadRequired", Assert.Throws<PresetTransferException>(() => other.Create("stale", compatibility, actions)).Code);
        Assert.Empty(other.List());
        File.WriteAllText(path, "[{\"schemaVersion\":\"99\",\"id\":\"future\"}]");
        var unreadable = File.ReadAllBytes(path);
        Assert.Equal(409, Assert.Throws<PresetTransferException>(() => store.Rename(kept.Id, "renamed")).StatusCode);
        Assert.Throws<PresetTransferException>(() => store.Delete(kept.Id));
        Assert.Throws<PresetTransferException>(() => store.Create("new", compatibility, actions));
        Assert.Equal(unreadable, File.ReadAllBytes(path));
        Assert.Equal("kept", Assert.Single(store.List()).Name);

        File.WriteAllBytes(path, onDisk);
        other.Refresh();
        _ = other.Create("fresh", compatibility, actions);
        Assert.Throws<PresetTransferException>(() => store.Delete(kept.Id));
        store.Refresh();
        Assert.True(store.Delete(kept.Id));
        Assert.Equal("fresh", Assert.Single(new PresetStore(new() { StorageRoot = root }).List()).Name);
    }

    [Fact]
    public void FileInPlaceOfStorageRootIsUnavailableNotAChangedOrEmptyLibrary()
    {
        var root = Path.Combine(TestZips.TempDir(), "presets");
        var store = new PresetStore(new() { StorageRoot = root });
        var compatibility = new PresetCompatibility("p", "1", ["T"]);
        PresetAction[] actions = [new("set", new Dictionary<string, JsonElement>())];
        var kept = store.Create("kept", compatibility, actions);
        var retained = root + "-retained";
        Directory.Move(root, retained);
        File.WriteAllText(root, "not a directory");

        // The OS reports this like a missing directory; it must stay a 503 storage failure, not a
        // 409 reload conflict, and a refresh must not adopt an empty library.
        Assert.Throws<PresetStorageException>(() => store.Create("blocked", compatibility, actions));
        Assert.Throws<PresetStorageException>(() => store.Rename(kept.Id, "renamed"));
        Assert.Throws<PresetStorageException>(() => store.Delete(kept.Id));
        Assert.Throws<PresetStorageException>(() => store.Refresh());
        Assert.Throws<PresetStorageException>(() => store.Reload());
        Assert.Throws<PresetStorageException>(() => store.Snapshot());
        Assert.Equal("kept", Assert.Single(store.List()).Name);

        File.Delete(root);
        Directory.Move(retained, root);
        Assert.True(store.Delete(kept.Id));
        Assert.Empty(new PresetStore(new() { StorageRoot = root }).List());
    }
}
