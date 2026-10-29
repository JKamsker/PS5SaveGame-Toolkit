using System.Text.Json;
using SaveToolkit.Core.Presets;

namespace SaveToolkit.Core.Tests;

public sealed class PresetTransferCompositionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    [Fact]
    public void SuccessfulIdenticalReloadInvalidatesWholeLibraryPreview()
    {
        var store = PresetTransferFixture.Store();
        var preset = PresetTransferFixture.Preset();
        store.Create("Existing", preset.Compatibility, preset.Actions);
        var identity = store.Snapshot().Identity;
        var service = new PresetTransferService(store);
        var review = PresetTransferFixture.Resolve(service, service.Preview(PresetPackageCodec.Encode([preset])));
        store.Reload();
        Assert.NotEqual(identity, store.Snapshot().Identity);
        Assert.Equal("stalePresetImport", Assert.Throws<PresetTransferException>(() => service.Commit(new(review.Token))).Code);
        Assert.Single(store.List());
    }

    [Fact]
    public void FailedReloadPreservesLastGoodMapAndGenerationAndAllowsReviewedCommitAfterRepair()
    {
        var root = TestZips.TempDir();
        var store = PresetTransferFixture.Store(root);
        var preset = PresetTransferFixture.Preset();
        var existing = store.Create("Existing", preset.Compatibility, preset.Actions);
        var identity = store.Snapshot().Identity;
        var path = Path.Combine(root, "presets.json");
        var original = File.ReadAllBytes(path);
        var service = new PresetTransferService(store);
        var review = PresetTransferFixture.Resolve(service, service.Preview(PresetPackageCodec.Encode([preset])));
        File.WriteAllText(path, JsonSerializer.Serialize(new[] { existing, existing }));
        Assert.Throws<InvalidDataException>(() => store.Reload());
        Assert.Equal(existing.Name, Assert.Single(store.List()).Name);
        File.WriteAllBytes(path, original);
        Assert.Equal(identity, store.Snapshot().Identity);
        service.Commit(new(review.Token));
        Assert.Equal(2, PresetTransferFixture.Store(root).List().Count);
    }

    [Fact]
    public void CommonStagingRejectsDuplicatesCountAndAggregateSizeWithoutPublishing()
    {
        var preset = PresetTransferFixture.Preset();
        var store = PresetTransferFixture.Store();
        var identity = store.Snapshot().Identity;
        var duplicate = new[] { preset, preset };
        Assert.Equal("invalidPresetLibrary", Assert.Throws<PresetTransferException>(() => store.ValidateImportLibrary(duplicate)).Code);
        Assert.Throws<PresetTransferException>(() => store.ImportLibrary(identity, duplicate));
        Assert.Equal(identity, store.Snapshot().Identity);
        Assert.Empty(store.List());
        var countStore = new PresetStore(new() { StorageRoot = TestZips.TempDir(), MaxPresets = 1 });
        Assert.Throws<PresetTransferException>(() => countStore.ValidateImportLibrary([preset, preset with { Id = "second" }]));
        // One valid record fits; two valid records exceed the whole-file budget.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] { preset }, JsonOptions).Length;
        var sizeStore = new PresetStore(new() { StorageRoot = TestZips.TempDir(), MaxPayloadBytes = bytes + 16 });
        sizeStore.ValidateImportLibrary([preset]);
        Assert.Throws<PresetTransferException>(() => sizeStore.ImportLibrary(sizeStore.Snapshot().Identity, [preset, preset with { Id = "second" }]));
        Assert.Empty(sizeStore.List());
    }

    [Fact]
    public void CandidateProvenanceSurvivesImportDurableReloadAndExportWithDetachedSnapshots()
    {
        var root = TestZips.TempDir();
        var store = PresetTransferFixture.Store(root);
        var service = new PresetTransferService(store);
        var source = PresetTransferFixture.Preset();
        var preview = service.Preview(PresetPackageCodec.Encode([source]));
        var detached = service.GetPreviewPresets(preview.Token);
        Assert.Equal(source.Actions[0].Instances.Single(), detached[0].Actions[0].Instances.Single());
        Assert.Throws<NotSupportedException>(() => ((IList<SaveToolkit.Sdk.ToolkitPreset>)detached).Clear());
        service.Commit(new(PresetTransferFixture.Resolve(service, preview).Token));
        store.Reload();
        var reloaded = new PresetTransferService(PresetTransferFixture.Store(root));
        var exported = PresetPackageCodec.Decode(reloaded.Export(new([source.Id]))).Presets.Single();
        Assert.Equal(source.Actions[0].Instances.Single(), exported.Actions[0].Instances.Single());
        Assert.Equal("1.2300e+120", exported.Actions[0].Parameters["lexeme"].GetRawText());
    }
    [Fact]
    public void ExpiredPreviewsRejectReadResolveAndCommitAndReleaseCapacityWithoutWriting()
    {
        var store = PresetTransferFixture.Store();
        var clock = new ImportClock();
        var service = new PresetTransferService(store, clock);
        var bytes = PresetPackageCodec.Encode([PresetTransferFixture.Preset()]);
        var first = service.Preview(bytes);
        var review = PresetTransferFixture.Resolve(service, first);
        for (var index = 0; index < 6; index++) service.Preview(bytes);
        clock.Now += TimeSpan.FromMinutes(10);
        Assert.Equal("stalePresetImport", Assert.Throws<PresetTransferException>(() => service.GetPreviewPresets(first.Token)).Code);
        Assert.Throws<PresetTransferException>(() => PresetTransferFixture.Resolve(service, first));
        Assert.Throws<PresetTransferException>(() => service.Commit(new(review.Token)));
        Assert.NotNull(service.Preview(bytes));
        Assert.Empty(store.List());
    }

    private sealed class ImportClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

}
