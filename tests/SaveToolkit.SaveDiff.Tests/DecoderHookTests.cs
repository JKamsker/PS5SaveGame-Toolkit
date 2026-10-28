using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class DecoderHookTests
{
    private static readonly ISaveDecoderHook Native = new NativeHook("test.native");

    private static (int Exit, string Out, string Err) Run(IReadOnlyCollection<IGamePlugin> plugins, params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var catalog = new PluginCatalog(plugins);
        var exit = CliApp.Run(args, stdout, stderr, () => new DecoderResolver(catalog.Plugins));
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Compare_SelectsSdkOnlyHook_AndPreservesLegacyViewAndAutoModes()
    {
        var dir = TempDir();
        var a = WriteZip(dir, "a.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 1))));
        var b = WriteZip(dir, "b.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 20))));
        IGamePlugin[] plugins = [new HookPlugin([Native])];

        var native = Run(plugins, "compare", a, b, "--decoder", "TEST.NATIVE", "--format", "json");
        var legacy = Run(plugins, "compare", a, b, "--semantic", "test.inventory", "--format", "json");
        var auto = Run(plugins, "compare", a, b, "--decoder", "auto", "--format", "json");

        Assert.Equal(CliApp.ExitDifferent, native.Exit);
        Assert.Empty(native.Err);
        using var json = JsonDocument.Parse(native.Out);
        var semantic = json.RootElement.GetProperty("files")[0].GetProperty("semantic");
        Assert.Equal("test.native", semantic.GetProperty("decoder").GetString());
        Assert.Equal("native/length", semantic.GetProperty("changes")[0].GetProperty("path").GetString());
        Assert.Contains("inventory/1/count", legacy.Out, StringComparison.Ordinal);
        Assert.Contains("inventory/1/count", auto.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("native/length", auto.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingTestingDecoders_RegisterWithoutSourceChanges_AndAdaptToSdkSinks()
    {
        ISaveDecoder legacy = new LegacyHook();
        var resolver = new DecoderResolver([new HookPlugin([legacy])]);

        Assert.Same(legacy, resolver.Resolve("test.legacy"));
        var tree = new HookSaveDecoder(legacy).Decode(MemorySaveSet.SingleFile("inv.dat", InventoryPlugin.Create((1, 1))), new());
        Assert.Equal("legacy/value", Assert.Single(tree.Values).Path);
        Assert.True(tree.Truncated);
        Assert.Contains("legacy warning", tree.Warnings);
    }

    [Fact]
    public void SdkHookOutput_UsesSharedValueLimits_AndInputByteBound()
    {
        var decoder = new HookSaveDecoder(Native);
        var file = MemorySaveSet.SingleFile("inv.dat", InventoryPlugin.Create((1, 1)));
        var tree = decoder.Decode(file, new() { MaxValues = 0 });

        Assert.True(tree.Truncated);
        Assert.Empty(tree.Values);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new() { MaxFileBytes = 0 }));
    }

    [Fact]
    public void ListDecoders_HasHumanColumns_AndVersionedJsonEnvelope()
    {
        IGamePlugin[] plugins = [new HookPlugin([Native])];
        var human = Run(plugins, "list-decoders");
        var machine = Run(plugins, "list-decoders", "--format", "json");

        Assert.Equal(CliApp.ExitSame, human.Exit);
        Assert.StartsWith("ID", human.Out, StringComparison.Ordinal);
        Assert.Contains("KIND", human.Out, StringComparison.Ordinal);
        Assert.Contains("test.native", human.Out, StringComparison.Ordinal);
        Assert.Empty(human.Err);
        Assert.Equal(CliApp.ExitSame, machine.Exit);
        Assert.Empty(machine.Err);
        using var json = JsonDocument.Parse(machine.Out);
        Assert.Equal("savetoolkit-decoders/1", json.RootElement.GetProperty("format").GetString());
        var decoders = json.RootElement.GetProperty("decoders").EnumerateArray().ToArray();
        Assert.Equal(["auto", "json", "none", "test.inventory", "test.native"], decoders.Select(d => d.GetProperty("id").GetString()));
        var hook = decoders.Single(d => d.GetProperty("kind").GetString() == "hook");
        Assert.Equal("test.inventory", hook.GetProperty("pluginId").GetString());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("AUTO")]
    [InlineData("none")]
    [InlineData("test.inventory")]
    [InlineData("TEST.NATIVE")]
    public void Registry_RejectsCollidingIds_WithActionableError(string id)
    {
        var result = Run([new HookPlugin([Native, new NativeHook(id)])], "list-decoders");

        Assert.Equal(CliApp.ExitError, result.Exit);
        Assert.Empty(result.Out);
        Assert.Contains("Duplicate decoder id", result.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad id")]
    [InlineData("bad/id")]
    public void Registry_RejectsInvalidIds(string id)
    {
        Assert.Equal(CliApp.ExitError, Run([new HookPlugin([new NativeHook(id)])], "list-decoders").Exit);
    }

    [Fact]
    public void HelpAndUsage_CoverHookSelectionAndRejectConflictingOptions()
    {
        var help = Run([], "--help");
        var both = Run([], "compare", "a", "b", "--decoder", "json", "--semantic", "auto");
        var unknown = Run([], "compare", "a", "b", "--decoder", "unknown");

        Assert.Equal(CliApp.ExitSame, help.Exit);
        Assert.Contains("list-decoders", help.Out, StringComparison.Ordinal);
        Assert.Contains("--decoder ID", help.Out, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitError, both.Exit);
        Assert.Contains("either --decoder or --semantic", both.Err, StringComparison.Ordinal);
        Assert.Contains("savediff list-decoders", unknown.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_ExposesQueryCapsInTextAndJsonReports()
    {
        var dir = TempDir();
        var a = WriteZip(dir, "a.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 1), (2, 2))));
        var b = WriteZip(dir, "b.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 1), (2, 3))));
        var plugin = new QueryableInventoryPlugin();

        var text = Run([plugin], "compare", a, b, "--decoder", plugin.Info.Id, "--max-query-rows", "1");
        var json = Run([plugin], "compare", a, b, "--decoder", plugin.Info.Id, "--max-query-pages", "0", "--format", "json");

        Assert.Contains("input truncated by decode limits", text.Out, StringComparison.Ordinal);
        Assert.Contains("row cap 1", text.Out, StringComparison.Ordinal);
        using var report = JsonDocument.Parse(json.Out);
        Assert.True(report.RootElement.GetProperty("files")[0].GetProperty("semantic").GetProperty("inputTruncated").GetBoolean());
        Assert.Contains("page cap 0", json.Out, StringComparison.Ordinal);
        Assert.Equal(CliApp.ExitError, Run([plugin], "compare", a, b, "--max-query-bytes", "-1").Exit);
    }

    [Fact]
    public void FixtureDecoder_CanSelectRegisteredHook()
    {
        var dir = TempDir();
        WriteZip(dir, "before.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 1))));
        WriteZip(dir, "after.zip", (Primary("inv.dat"), InventoryPlugin.Create((1, 20))));
        var manifest = Path.Combine(dir, "fixture.json");
        File.WriteAllText(manifest, """
            {"fixtures":[{"id":"hook","titleId":"PPSA99999","event":"synthetic length change","decoder":"test.native",
              "before":{"archive":"before.zip"},"after":{"archive":"after.zip"},
              "expected":[{"file":"inv.dat","path":"native/length"}]}]}
            """);

        var result = Run([new HookPlugin([Native])], "verify", manifest, "--artifacts", dir);

        Assert.Equal(CliApp.ExitSame, result.Exit);
        Assert.Contains("1 passed, 0 failed", result.Out, StringComparison.Ordinal);
    }

    private sealed class HookPlugin(IReadOnlyList<ISaveDecoderHook> decoders) : IGamePlugin, ISaveDecoderProvider
    {
        private readonly InventoryPlugin _plugin = new();
        public PluginInfo Info => _plugin.Info;
        public IReadOnlyList<ISaveDecoderHook> Decoders => decoders;
        public ProbeResult Probe(ISaveFile file) => _plugin.Probe(file);
        public ISaveDocument Open(ISaveFile file) => _plugin.Open(file);
    }

    private sealed class NativeHook(string id) : ISaveDecoderHook
    {
        public string Id => id;
        public bool CanDecode(ISaveFile file) => new InventoryPlugin().Probe(file).IsMatch;
        public void Decode(ISaveFile file, ISaveDecodeSink sink) => sink.Add("native/length", "integer", file.Length.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class LegacyHook : ISaveDecoder
    {
        // Explicit implementations were supported before F12 and must continue to register unchanged.
        string ISaveDecoder.Id => "test.legacy";
        bool ISaveDecoder.CanDecode(ISaveFile file) => true;
        public SemanticTree Decode(ISaveFile file, DecodeLimits limits) => new("test.legacy", [new("legacy/value", "integer", "1")])
        {
            Truncated = true,
            Warnings = ["legacy warning"],
        };
    }
}
