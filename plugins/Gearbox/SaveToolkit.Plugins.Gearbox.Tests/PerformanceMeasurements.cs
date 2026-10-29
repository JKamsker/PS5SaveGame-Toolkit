using System.Diagnostics;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

// Run this class alone for uncontaminated first-use observations; never assert elapsed time.
[Collection("Gearbox performance")]
public sealed class PerformanceMeasurements(ITestOutputHelper output)
{
    private const int Repetitions = 9;

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void LargestSamples_PhaseCosts(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var file = set.AllDiskFiles.Where(f => plugin.Probe(f).IsMatch).MaxBy(f => f.Length)!;
        output.WriteLine($"{title}: largest={file.Length} bytes; Release recommended; {Repetitions} warm repetitions.");
        ReferencePackStore store = null!;
        Measure("cold pack store (both embedded packs)", () => store = new([plugin]), 1);
        Assert.Empty(store.LoadErrors);
        Measure("cold fallback", () => _ = SerialDatabase.For(title == "PPSA01493"), 1);
        var references = store.Pin(plugin, title);
        using (var first = plugin.Open(file, references))
            Measure("first root view", () => _ = first.BuildView(), 1);
        Measure("pin", () => _ = store.Pin(plugin, title));
        Measure("open", () => { using var document = plugin.Open(file, references); });
        Measure("open + root", () => { using var document = plugin.Open(file, references); _ = document.BuildView(); });
        Measure("open + root + all first pages", () =>
        {
            using var document = plugin.Open(file, references);
            _ = document.BuildView();
            var queryable = (IQueryableDocument)document;
            foreach (var collection in queryable.Collections)
                _ = queryable.Query(collection.Id, new() { Limit = 20 });
        });
        using var opened = (GearboxDocument)plugin.Open(file, references);
        _ = opened.BuildView();
        var entry = opened.Inventory.Entries.First();
        _ = opened.Query(entry.List, new() { Limit = 1 });
        Measure("lazy first item details", () => Assert.NotEmpty(opened.ExpandNode("detail/" + entry.Id)));
        Measure("serialize no-op", () => Assert.True(opened.Serialize().Single().Content.Span.SequenceEqual(file.ReadAllBytes())));
        var serials = opened.Inventory.Entries.Select(e => e.Serial).ToArray();
        var reader = new SerialReader(title == "PPSA01493");
        output.WriteLine($"serials={serials.Length}; costs below are per complete batch");
        Measure("serial decode", () => { foreach (var serial in serials) Assert.True(reader.Read(serial).ChecksumValid); });
        var envelopes = serials.Select(s => SerialEnvelope.Decode(s, title == "PPSA01493")).ToArray();
        Measure("serial encode", () =>
        {
            for (var index = 0; index < envelopes.Length; index++)
                Assert.Equal(serials[index], envelopes[index].EncodeOriginalSeed());
        });
        var anchor = set.AllDiskFiles.Where(f => f.FileName == "1").MaxBy(f => f.Length)!;
        Measure("F7 propose", () => Assert.Empty(Assert.Single(plugin.ProposeGroups(anchor)).Errors));
        var proposal = Assert.Single(plugin.ProposeGroups(anchor));
        var input = new DocumentGroupInput(proposal.Members.Select(m =>
            new DocumentGroupFile(m.Role, set.AllDiskFiles.Single(f => f.RelativePath == m.RelativePath))).ToArray());
        Measure("F7 open + root", () => { using var group = plugin.OpenGroup(input, references); _ = group.BuildView(); });
        Measure("F7 validate + open + root", () =>
        {
            Assert.Empty(plugin.ValidateGroup(input, references));
            using var group = plugin.OpenGroup(input, references);
            _ = group.BuildView();
        });
    }

    private void Measure(string phase, Action action, int repetitions = Repetitions)
    {
        if (repetitions > 1) action();
        var observations = new List<(double Milliseconds, long Bytes)>();
        for (var iteration = 0; iteration < repetitions; iteration++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            action();
            observations.Add((Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocated));
        }
        output.WriteLine($"{phase}: median {observations.OrderBy(o => o.Milliseconds).ElementAt(repetitions / 2).Milliseconds:F3} ms; " +
            $"{observations.OrderBy(o => o.Bytes).ElementAt(repetitions / 2).Bytes} thread allocated bytes");
    }
}

[CollectionDefinition("Gearbox performance", DisableParallelization = true)]
public sealed class GearboxPerformanceCollection;
