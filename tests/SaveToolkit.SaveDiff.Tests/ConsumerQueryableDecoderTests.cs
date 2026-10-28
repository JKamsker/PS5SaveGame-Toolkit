using SaveToolkit.Plugins.Cyberpunk2077;
using SaveToolkit.Plugins.Pragmata;
using SaveToolkit.Plugins.Witcher3;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff.Tests;

/// <summary>Read-only integration with F12's actual consumers; private samples are never copied.</summary>
public sealed class ConsumerQueryableDecoderTests
{
    [Theory]
    [InlineData("PPSA04027")]
    [InlineData("PPSA04018")]
    [InlineData("PPSA02530")]
    public void RealPluginCollections_AppearInBoundedSemanticOutput(string titleId)
    {
        IGamePlugin plugin = titleId switch
        {
            "PPSA04027" => new Cyberpunk2077Plugin(),
            "PPSA04018" => new Witcher3Plugin(),
            "PPSA02530" => new PragmataPlugin(),
            _ => throw new ArgumentException("Unknown synthetic test title.", nameof(titleId)),
        };
        var file = SampleSaves.Load(titleId).Directories.Where(d => !d.IsBackup).SelectMany(d => d.Files)
            .First(f => (titleId != "PPSA02530" || f.FileName == "data000.bin") && plugin.Probe(f).Score > ProbeScore.Fallback);
        var limits = new DecodeLimits { MaxQueryRows = 3, QueryPageSize = 2 };
        var decoder = new PluginViewDecoder(plugin);

        var first = decoder.Decode(file, limits);
        var second = decoder.Decode(file, limits);

        Assert.True(first.Values.Any(v => v.Path.StartsWith("collections/", StringComparison.Ordinal)), "The consumer's F8 cells must be decoded.");
        Assert.True(first.Truncated);
        Assert.Contains(first.Warnings, w => w.Contains("row cap 3", StringComparison.Ordinal));
        Assert.True(first.Values.SequenceEqual(second.Values), "Repeated decodes must produce identical values.");
        Assert.True(first.Warnings.SequenceEqual(second.Warnings), "Repeated decodes must produce identical warnings.");
    }
}
