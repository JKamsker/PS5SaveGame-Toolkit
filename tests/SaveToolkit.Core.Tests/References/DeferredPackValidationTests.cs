using System.IO.Compression;
using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests.References;

/// <summary>
/// Embedded packs are validated on first need instead of at construction. Whatever operation comes first,
/// the store must report the same packs, duplicate-id winners and diagnostics as validating in order.
/// </summary>
public sealed class DeferredPackValidationTests
{
    private const string Duplicate = "dup.items";
    private const string BrokenOnly = "broken.only";
    private const string Other = "other.items";

    [Fact]
    public void ConstructionValidatesNothing()
    {
        var source = Fixture();
        _ = new ReferencePackStore(null, [source]);
        Assert.All(source.GetManifestResourceNames(), name => Assert.Equal(0, source.Reads(name)));
    }

    [Fact]
    public void PinValidatesOnlyPacksThatCanApplyToThePlugin()
    {
        var source = Fixture();
        var store = new ReferencePackStore(null, [source]);
        var context = store.Pin(new ItemsPlugin(), ItemsPlugin.TitleId);

        Assert.Equal([Duplicate], context.Packs.Select(p => p.Id));
        Assert.Equal(1, source.Reads("refpacks/b-dup-valid/items.json"));
        Assert.Equal(0, source.Reads("refpacks/c-dup-again/items.json"));
        Assert.Equal(0, source.Reads("refpacks/e-other/items.json"));
        Assert.Equal(0, source.Reads("refpacks/e-other/icons/a.png"));
    }

    [Fact]
    public void FirstValidCopyWinsAndEveryRejectionIsReportedInDiscoveryOrder()
    {
        var store = new ReferencePackStore(null, [Fixture()]);

        Assert.Equal([Duplicate, Other], store.Packs.Select(p => p.Id));
        Assert.Equal("Second copy", store.Find(Duplicate)!.Catalog("items")!.Find("1001")!.Name);
        var errors = store.LoadErrors;
        Assert.Equal(
            ["0-garbage", "a-dup-broken", "c-dup-again", "d-broken-only"],
            errors.Select(e => e.Origin[(e.Origin.LastIndexOf('/') + 1)..]));
        Assert.Contains("hash mismatch", errors[1].Message, StringComparison.Ordinal);
        Assert.Equal($"Pack id '{Duplicate}' is already loaded; this copy was skipped.", errors[2].Message);
        Assert.Contains("hash mismatch", errors[3].Message, StringComparison.Ordinal);
        Assert.Null(store.Find(BrokenOnly));
    }

    private static readonly string[] Operations = ["listing", "errors", "pin", "find", "import", "remove", "idle"];

    public static TheoryData<string> FirstOperations => new(Operations);

    [Theory]
    [MemberData(nameof(FirstOperations))]
    public void ResultsDoNotDependOnWhichOperationValidatesFirst(string first)
    {
        var expected = Snapshot(new ReferencePackStore(null, [Fixture()]));
        var store = new ReferencePackStore(null, [Fixture()]);
        Run(store, first);
        Assert.Equal(expected, Snapshot(store));
    }

    [Fact]
    public void ImportCollidesOnlyWithAnAcceptedEmbeddedCopy()
    {
        var store = new ReferencePackStore(null, [Fixture()]);

        var error = Assert.Throws<ReferencePackException>(() => store.Import(PackBuilder.WithItems(id: Duplicate).Zip(), "dup.zip"));
        Assert.Equal($"Pack id '{Duplicate}' is already used by a plugin's built-in pack. Give the pack another id.", error.Message);
        // Every embedded copy of this id is rejected, so a user pack may take it.
        Assert.Equal(BrokenOnly, store.Import(PackBuilder.WithItems(id: BrokenOnly).Zip(), "broken.zip").Id);
        Assert.Throws<InvalidOperationException>(() => store.Remove(Duplicate));
        Assert.True(store.Remove(BrokenOnly));
        Assert.False(store.Remove("missing.pack"));
    }

    [Fact]
    public void StoredPacksAreCheckedAgainstEmbeddedWinnersAndReportedAfterThem()
    {
        var root = TestZips.TempDir();
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "pack-dup.items.zip"), PackBuilder.WithItems(id: Duplicate).Zip());
        File.WriteAllBytes(Path.Combine(root, "pack-broken.only.zip"), PackBuilder.WithItems(id: BrokenOnly).Zip());
        var store = new ReferencePackStore(new ReferencePackOptions { StorageRoot = root }, [Fixture()]);

        Assert.Equal([Duplicate, BrokenOnly], store.Pin(new ItemsPlugin(), null).Packs.Select(p => p.Id));
        Assert.Equal([Other], store.Pin(new FieldReferenceOpeningPlugin("other.plugin"), null).Packs.Select(p => p.Id));
        Assert.Equal([Duplicate, Other, BrokenOnly], store.Packs.Select(p => p.Id));
        var last = store.LoadErrors[^1];
        Assert.Equal("pack-dup.items.zip", last.Origin);
        Assert.Equal($"Pack id '{Duplicate}' is already loaded; this copy was skipped.", last.Message);
        Assert.Equal(5, store.LoadErrors.Count);
    }

    [Fact]
    public void IdleValidationFinishesEveryPackOnceAndThenStops()
    {
        var source = Fixture();
        var store = new ReferencePackStore(null, [source]);
        var steps = 0;
        while (store.ValidateNext()) steps++;

        Assert.Equal(7, steps); // six embedded folders, then the stored packs
        Assert.False(store.ValidateNext());
        var reads = source.GetManifestResourceNames().Sum(source.Reads);
        Assert.Equal(Snapshot(new ReferencePackStore(null, [Fixture()])), Snapshot(store));
        Assert.Equal(reads, source.GetManifestResourceNames().Sum(source.Reads));
    }

    [Fact]
    public async Task ConcurrentFirstUseValidatesEachPackOnceAndAgrees()
    {
        var expected = Snapshot(new ReferencePackStore(null, [Fixture()]));
        for (var round = 0; round < 20; round++)
        {
            var source = Fixture();
            var store = new ReferencePackStore(null, [source]);
            string[] operations = [.. Operations, .. Operations];
            using var start = new Barrier(operations.Length);
            var tasks = operations.Select(op => Task.Run(() =>
            {
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30)));
                Run(store, op);
                return Snapshot(store);
            }));

            foreach (var snapshot in await Task.WhenAll(tasks)) Assert.Equal(expected, snapshot);
            foreach (var name in source.GetManifestResourceNames().Where(n => !n.EndsWith("/pack.json", StringComparison.Ordinal)))
                Assert.True(source.Reads(name) <= 1, $"{name} was validated {source.Reads(name)} times");
        }
    }

    private static void Run(ReferencePackStore store, string operation)
    {
        switch (operation)
        {
            case "listing": _ = store.Packs; break;
            case "errors": _ = store.LoadErrors; break;
            case "pin": _ = store.Pin(new ItemsPlugin(), ItemsPlugin.TitleId); break;
            case "find": _ = store.Find(Duplicate); break;
            case "import":
                Assert.Throws<ReferencePackException>(() => store.Import(PackBuilder.WithItems(id: Duplicate).Zip(), "dup.zip"));
                break;
            case "remove": Assert.Throws<InvalidOperationException>(() => store.Remove(Duplicate)); break;
            case "idle": _ = store.ValidateNext(); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static string Snapshot(ReferencePackStore store)
    {
        var text = new StringBuilder();
        foreach (var pack in store.Packs)
            text.Append(pack.Id).Append(' ').Append(pack.Info.ContentFingerprint).Append(' ')
                .AppendJoin(',', pack.Info.Datasets.Select(d => $"{d.Id}={d.ItemCount}")).Append('\n');
        foreach (var error in store.LoadErrors) text.Append(error.Origin).Append(": ").Append(error.Message).Append('\n');
        var pinned = store.Pin(new ItemsPlugin(), ItemsPlugin.TitleId);
        text.Append("pin ").AppendJoin(',', pinned.Packs.Select(p => p.Id)).Append('\n');
        text.Append("find ").Append(store.Find(Duplicate)?.Info.ContentFingerprint).Append(' ').Append(store.Find(BrokenOnly) is null);
        return text.ToString();
    }

    /// <summary>Six folders: unreadable manifest, three copies of one id (first broken), a sole broken id and another plugin's pack.</summary>
    private static CountingPackResourceAssembly Fixture()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["refpacks/0-garbage/pack.json"] = "{ not json"u8.ToArray() };
        Add(files, "a-dup-broken", PackBuilder.WithItems("First copy", Duplicate) with { Mutate = BreakFirstHash });
        Add(files, "b-dup-valid", PackBuilder.WithItems("Second copy", Duplicate));
        Add(files, "c-dup-again", PackBuilder.WithItems("Third copy", Duplicate));
        Add(files, "d-broken-only", PackBuilder.WithItems("Broken", BrokenOnly) with { Mutate = BreakFirstHash });
        Add(files, "e-other", PackBuilder.WithItems("Other", Other) with { Plugins = ["other.plugin", "test.items.not"] });
        return new CountingPackResourceAssembly(files);
    }

    private static void BreakFirstHash(System.Text.Json.Nodes.JsonObject manifest) =>
        manifest["datasets"]![0]!["files"]![0]!["sha256"] = new string('0', 64);

    private static void Add(Dictionary<string, byte[]> files, string folder, PackBuilder pack)
    {
        using var zip = new ZipArchive(new MemoryStream(pack.Zip()));
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files.Add($"refpacks/{folder}/{entry.FullName}", buffer.ToArray());
        }
    }
}
