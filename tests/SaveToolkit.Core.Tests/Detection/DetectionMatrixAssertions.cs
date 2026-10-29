using SaveToolkit.Core.Plugins;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests.Detection;

internal sealed class DetectionMatrixAssertions
{
    private const int ShuffleCount = 4;
    private const int ShuffleSeed = 0x40DE7EC;
    private readonly PluginCatalog _catalog;
    private readonly PluginCatalog[] _permutations;

    public DetectionMatrixAssertions(PluginCatalog catalog)
    {
        _catalog = catalog;
        var random = new Random(ShuffleSeed);
        _permutations = Enumerable.Range(0, ShuffleCount).Select(_ =>
        {
            var plugins = catalog.Plugins.ToArray();
            random.Shuffle(plugins);
            return new PluginCatalog(plugins);
        }).Append(new PluginCatalog(catalog.Plugins.Reverse())).ToArray();
    }

    public static bool IsGeneric(IGamePlugin plugin) => plugin.Info.Id is "format.json" or "format.json-xor";

    public IReadOnlyList<ProbeMatch> Check(ISaveFile file, string name, bool verifyOrder = true)
    {
        // Catalog.Probe deliberately swallows exceptions. Exercise every probe directly as well,
        // including nonmatches, so that broken foreign-input probes cannot silently disappear.
        var direct = new List<(string Id, int Score)>();
        foreach (var plugin in _catalog.Plugins)
        {
            ProbeResult? result = null;
            var error = Record.Exception(() => result = plugin.Probe(file));
            Assert.True(error is null, $"{name}: {plugin.Info.Id} threw {error?.GetType().FullName}.");
            if (result!.IsMatch) direct.Add((plugin.Info.Id, result.Score));
        }

        var matches = _catalog.Probe(file);
        Assert.Equal(direct.OrderByDescending(match => match.Score).ThenBy(match => match.Id, StringComparer.Ordinal),
            matches.Select(match => (match.Plugin.Info.Id, match.Result.Score)));
        if (verifyOrder)
            foreach (var catalog in _permutations)
                Assert.Equal(Signature(matches), Signature(catalog.Probe(file)));

        var nongeneric = matches.Where(match => match.Plugin is not RawFilePlugin && !IsGeneric(match.Plugin)).ToArray();
        Assert.All(nongeneric.GroupBy(match => match.Result.Score), group =>
            Assert.True(group.Count() == 1, $"{name}: equal-confidence collision: {Names(group)}."));
        return matches;
    }

    public IReadOnlyList<ProbeMatch> Owned(DetectionFixture fixture)
    {
        var matches = Check(fixture.File, fixture.Name);
        var owner = Assert.Single(matches, match => match.Plugin.Info.Id == fixture.Owner.Info.Id);
        foreach (var match in matches.Where(match => match != owner && match.Plugin is not RawFilePlugin))
        {
            Assert.True(IsGeneric(match.Plugin), $"{fixture.Name}: foreign claimant {match.Plugin.Info.Id}.");
            // The documented plain-character XorJson format also outranks GenericJson.
            Assert.True(match.Result.Score < owner.Result.Score,
                $"{fixture.Name}: generic fallback {match.Plugin.Info.Id} does not rank below owner.");
        }
        Assert.Equal(fixture.Owner.Info.Id, matches[0].Plugin.Info.Id);
        return matches;
    }

    public int Mutated(DetectionFixture fixture)
    {
        var count = 0;
        foreach (var (name, file) in DetectionFixtures.Mutations(fixture.File))
        {
            var matches = Check(file, $"{fixture.Name}/{name}", verifyOrder: false);
            Assert.All(matches.Where(match => match.Result.Score >= ProbeScore.Title), match =>
                Assert.True(match.Plugin.Info.Id == fixture.Owner.Info.Id,
                    $"{fixture.Name}/{name}: foreign high-confidence claimant {match.Plugin.Info.Id}."));
            count++;
        }
        return count;
    }

    public static string Names(IEnumerable<ProbeMatch> matches) => string.Join(" > ", matches.Select(match => match.Plugin.Info.Id));
    private static IEnumerable<(string Id, int Score, string? Label)> Signature(IEnumerable<ProbeMatch> matches) =>
        matches.Select(match => (match.Plugin.Info.Id, match.Result.Score, match.Result.Label));
}
