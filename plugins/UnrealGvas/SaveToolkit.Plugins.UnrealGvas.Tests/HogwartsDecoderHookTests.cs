using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsDecoderHookTests
{
    private static readonly string[] ImageNames = ["RawDatabaseImage", "RawExclusiveImage"];
    [Fact]
    public void RegisteredSdkHookAlignsReorderedRowsAndPreservesMultisetsAndInput()
    {
        var hook = Hook();
        Assert.Equal("unrealgvas.hogwarts.rows", hook.Id);
        var first = Fixture(false);
        var second = Fixture(true);
        byte[] original = first.ReadAllBytes();
        Assert.True(hook.CanDecode(first));
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile("foreign", [])));
        var decoder = new HookSaveDecoder(hook);
        var before = decoder.Decode(first, new());
        var after = decoder.Decode(second, new());
        Assert.False(before.Truncated);
        Assert.Equal(0, SemanticDiffer.Compare(before, after).Total);
        Assert.Contains(before.Values, value => value.Kind == "row-multiplicity" && value.Value == "2");
        Assert.All(before.Values.Where(value => value.Kind is "row-sha256" or "sha256"), value => Assert.Matches("^[a-f0-9]{64}$", value.Value));
        Assert.DoesNotContain(before.Values, value => value.Value.Contains("private-placeholder", StringComparison.Ordinal));
        Assert.Equal(original, first.ReadAllBytes());
    }

    [Fact]
    public void HookHonorsSinkStopDepthFileAndValueLimits()
    {
        var hook = Hook();
        var file = Fixture(false);
        var sink = new StopSink();
        hook.Decode(file, sink);
        Assert.Equal(1, sink.Calls);
        var decoder = new HookSaveDecoder(hook);
        var limited = decoder.Decode(file, new() { MaxValues = 1 });
        Assert.True(limited.Truncated);
        Assert.Single(limited.Values);
        var shallow = decoder.Decode(file, new() { MaxDepth = 4 });
        Assert.True(shallow.Truncated);
        Assert.Empty(shallow.Values);
        Assert.Contains(shallow.Warnings, warning => warning.Contains("depth", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new() { MaxFileBytes = 1 }));
    }

    [Fact]
    public void RowTraversalBudgetIsSharedByBothImages()
    {
        using var source = SqliteImage.Create();
        source.Execute("""
            CREATE TABLE CollectionDynamic(CategoryID TEXT, SubcategoryID TEXT, ItemID TEXT);
            WITH RECURSIVE numbers(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM numbers WHERE n<50001)
            INSERT INTO CollectionDynamic SELECT 'A','B','C' FROM numbers;
            """);
        var result = new HookSaveDecoder(Hook()).Decode(Wrap(source.Serialize()), new());
        Assert.True(result.Truncated);
        Assert.Contains(result.Warnings, warning => warning.Contains("100,000-row", StringComparison.Ordinal));
    }

    [Fact]
    public void PluginViewDecoderVisitsEveryGenericPropertyPageAndReportsQueryCaps()
    {
        var file = MemorySaveSet.SingleFile("generic", GvasFixture.Build(writer =>
        {
            for (int i = 0; i < 205; i++)
                GvasFixture.Property(writer, "Value" + i, "IntProperty", GvasFixture.Body(body => body.Write(i)));
        }));
        var decoder = new PluginViewDecoder(new UnrealGvasPlugin());
        var complete = decoder.Decode(file, new() { QueryPageSize = 37 });
        Assert.False(complete.Truncated);
        Assert.Equal(205, complete.Values.Count(value => value.Path.StartsWith("collections/gvas~1properties/", StringComparison.Ordinal) && value.Path.EndsWith("/cells/value", StringComparison.Ordinal)));
        var capped = decoder.Decode(file, new() { MaxQueryRows = 1, QueryPageSize = 1 });
        Assert.True(capped.Truncated);
        Assert.Contains(capped.Warnings, warning => warning.Contains("row", StringComparison.OrdinalIgnoreCase));
    }

    private static ISaveDecoderHook Hook() => Assert.Single(Assert.IsAssignableFrom<ISaveDecoderProvider>(new UnrealGvasPlugin()).Decoders);

    private static ISaveFile Fixture(bool reversed)
    {
        using var source = SqliteImage.Create();
        source.Execute("""
            CREATE TABLE InventoryDynamic(CharacterID TEXT, HolderID TEXT, SlotNumber INTEGER, ItemID TEXT, Count INTEGER);
            CREATE TABLE CollectionDynamic(CategoryID TEXT, SubcategoryID TEXT, ItemID TEXT);
            INSERT INTO CollectionDynamic VALUES('A','B','C'),('A','B','C');
            """);
        source.Execute(reversed
            ? "INSERT INTO InventoryDynamic VALUES('private-placeholder','Bag',2,'Potion',4),('private-placeholder','Bag',1,'Knuts',5);"
            : "INSERT INTO InventoryDynamic VALUES('private-placeholder','Bag',1,'Knuts',5),('private-placeholder','Bag',2,'Potion',4);");
        return Wrap(source.Serialize());
    }

    private static ISaveFile Wrap(byte[] image) => MemorySaveSet.SingleFile("save.sav", GvasFixture.Build(writer =>
    {
        foreach (string name in ImageNames)
        {
            var frames = HogwartsImage.EncodeStored(image);
            GvasFixture.Property(writer, name, "ArrayProperty", GvasFixture.Body(body =>
            {
                body.Write(frames.Length);
                body.Write(frames);
            }), "ByteProperty");
        }
    }), "PPSA01603", "HL0000");

    private sealed class StopSink : ISaveDecodeSink
    {
        public int Calls { get; private set; }
        public int MaxValues => 1;
        public int MaxValueLength => 256;
        public int MaxDepth => 64;
        public long MaxFileBytes => 128 * 1024 * 1024;
        public bool Add(string path, string kind, string value, string? label = null) { Calls++; return false; }
        public void Warn(string message) { }
        public void MarkTruncated(string reason) { }
    }
}
