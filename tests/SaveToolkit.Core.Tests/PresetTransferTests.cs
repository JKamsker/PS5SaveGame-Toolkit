using SaveToolkit.Core.Presets;

namespace SaveToolkit.Core.Tests;

public sealed class PresetTransferTests
{
    [Fact]
    public void ImportRequiresExplicitConflictChoiceAndReviewedNamesThenPersistsWholePackage()
    {
        var root = TestZips.TempDir();
        var store = PresetTransferFixture.Store(root);
        var source = PresetTransferFixture.Preset();
        var existing = store.Create(source.Name, source.Compatibility, source.Actions);
        var service = new PresetTransferService(store);
        var preview = service.Preview(PresetPackageCodec.Encode([source with { Id = existing.Id }, source with { Id = "second", Name = "Other" }]));
        Assert.Single(preview.Items[0].Conflicts);
        Assert.Throws<PresetTransferException>(() => service.Commit(new(preview.Token)));
        Assert.Throws<PresetTransferException>(() => service.Resolve(new(preview.Token, [])));
        Assert.Single(store.List());
        var review = PresetTransferFixture.Resolve(service, preview);
        Assert.Equal(source.Name + " (2)", review.Items[0].ResultingName);
        Assert.Null(review.Items[0].ReplacesName);
        Assert.Single(store.List());
        var result = service.Commit(new(review.Token));
        Assert.Equal(2, result.Imported.Count);
        Assert.Equal(3, store.List().Count);
        Assert.Equal(3, PresetTransferFixture.Store(root).List().Count);
        Assert.Throws<PresetTransferException>(() => service.Commit(new(review.Token)));
    }

    [Fact]
    public void BothIdAndNameConflictsRequireSpecificReplacementTarget()
    {
        var store = PresetTransferFixture.Store();
        var source = PresetTransferFixture.Preset();
        var idMatch = store.Create("ID match", source.Compatibility, source.Actions);
        var nameMatch = store.Create("Name match", source.Compatibility, source.Actions);
        var service = new PresetTransferService(store);
        var preview = service.Preview(PresetPackageCodec.Encode([source with { Id = idMatch.Id, Name = nameMatch.Name }]));
        Assert.Equal(2, preview.Items.Single().Conflicts.Count);
        Assert.Throws<PresetTransferException>(() => PresetTransferFixture.Resolve(service, preview, new PresetImportChoice(0, "replace")));
        var review = PresetTransferFixture.Resolve(service, preview, new PresetImportChoice(0, "replace", nameMatch.Id));
        Assert.Equal("Name match", review.Items[0].ReplacesName);
        service.Commit(new(review.Token));
        Assert.Equal(2, store.List().Count);
        Assert.Equal("ID match", store.Find(idMatch.Id)!.Name);
        Assert.Equal("Name match", store.Find(nameMatch.Id)!.Name);
    }

    [Fact]
    public void DuplicateNamesInsidePackageNeedChoiceAndGetDistinctResultNames()
    {
        var service = new PresetTransferService(PresetTransferFixture.Store());
        var source = PresetTransferFixture.Preset();
        var preview = service.Preview(PresetPackageCodec.Encode([source, source with { Id = "second" }]));
        Assert.All(preview.Items, item => Assert.True(item.RepeatedPackageName));
        var review = PresetTransferFixture.Resolve(service, preview);
        Assert.Equal(source.Name, review.Items[0].ResultingName);
        Assert.Equal(source.Name + " (2)", review.Items[1].ResultingName);
    }

    [Fact]
    public void CrudAndExternalFileChangesInvalidateImportEvenWhenUnrelated()
    {
        var root = TestZips.TempDir();
        var store = PresetTransferFixture.Store(root);
        var service = new PresetTransferService(store);
        var source = PresetTransferFixture.Preset();
        var review = PresetTransferFixture.Resolve(service, service.Preview(PresetPackageCodec.Encode([source])));
        var existing = store.Create("Unrelated", source.Compatibility, source.Actions);
        Assert.Equal(409, Assert.Throws<PresetTransferException>(() => service.Commit(new(review.Token))).StatusCode);
        var next = PresetTransferFixture.Resolve(service, service.Preview(PresetPackageCodec.Encode([source])));
        File.AppendAllText(Path.Combine(root, "presets.json"), " ");
        Assert.Equal("stalePresetImport", Assert.Throws<PresetTransferException>(() => service.Commit(new(next.Token))).Code);
        Assert.Single(store.List());
        Assert.NotNull(store.Find(existing.Id));
    }

    [Fact]
    public void FailedAtomicWriteKeepsMemoryAndDiskAndAllowsExplicitRetry()
    {
        var root = TestZips.TempDir();
        var store = PresetTransferFixture.Store(root);
        var source = PresetTransferFixture.Preset();
        store.Create("Original", source.Compatibility, source.Actions);
        var path = Path.Combine(root, "presets.json");
        var before = File.ReadAllBytes(path);
        var service = new PresetTransferService(store);
        var review = PresetTransferFixture.Resolve(service, service.Preview(PresetPackageCodec.Encode([source, source with { Id = "second", Name = "Second" }])));
        // A directory at the fixed temp filename deterministically injects an I/O failure on every OS.
        Directory.CreateDirectory(path + ".tmp");
        Assert.Throws<PresetStorageException>(() => service.Commit(new(review.Token)));
        Assert.Single(store.List());
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(PresetTransferFixture.Store(root).List());
        Directory.Delete(path + ".tmp");
        service.Commit(new(review.Token));
        Assert.Equal(3, PresetTransferFixture.Store(root).List().Count);
    }

    [Fact]
    public void CancelAndBoundedPendingPreviewsNeverMutateLibrary()
    {
        var store = PresetTransferFixture.Store();
        var service = new PresetTransferService(store);
        var bytes = PresetPackageCodec.Encode([PresetTransferFixture.Preset()]);
        var preview = service.Preview(bytes);
        service.Cancel(preview.Token);
        Assert.Throws<PresetTransferException>(() => service.Commit(new(preview.Token)));
        for (var index = 0; index < 8; index++) service.Preview(bytes);
        Assert.Equal(503, Assert.Throws<PresetTransferException>(() => service.Preview(bytes)).StatusCode);
        Assert.Empty(store.List());
    }
}
