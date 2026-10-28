using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using Xunit;

namespace SaveToolkit.Testing;

/// <summary>Reference-pack checks: every embedded pack must load, and documents open with them pinned.</summary>
public abstract partial class PluginConformanceTests<TPlugin>
{
    private static readonly Lazy<ReferencePackStore> EmbeddedPacks = new(() => new ReferencePackStore([new TPlugin()]));

    /// <summary>Embedded packs of the plugin's assembly, loaded and validated exactly as the app does.</summary>
    protected static ReferencePackStore ReferencePacks => EmbeddedPacks.Value;

    /// <summary>The reference context the app would pin for <paramref name="file"/> (embedded packs only).</summary>
    protected IReferenceContext ReferencesFor(ISaveFile file) => ReferencePacks.Pin(Plugin, file.Directory.TitleId);

    [Fact]
    public void EmbeddedReferencePacks_AreValid()
    {
        var errors = ReferencePacks.LoadErrors.Select(e => $"{e.Origin}: {e.Message}").ToList();

        Assert.True(errors.Count == 0, "Embedded reference packs failed to load:\n" + string.Join("\n", errors));
        foreach (var pack in ReferencePacks.Packs)
        {
            Assert.All(pack.Info.Datasets, d => Assert.False(string.IsNullOrWhiteSpace(d.Attribution.License)));
        }
    }
}
