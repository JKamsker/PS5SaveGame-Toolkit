using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokDiffHarnessTests
{
    [Fact]
    public void F1DecoderReportsStableGrammarAndSemanticChangesAcrossGenerations()
    {
        var set = SampleSaves.Load("PPSA08330");
        var report = SaveSetDiffer.Compare(set, set, new SaveDiffOptions
        {
            BeforeGeneration = GenerationSelector.Only(0),
            AfterGeneration = GenerationSelector.Latest,
            FileFilters = ["GOWRSAVE0"],
            TitleFilter = "PPSA08330",
            Decoder = new RagnarokDiffDecoder(),
            DecodeLimits = new DecodeLimits { MaxValues = 20_000 },
            CollapseMirroredBackups = true,
        });

        Assert.NotEmpty(report.Files);
        Assert.Equal(0, report.Totals.SemanticErrors);
        Assert.Contains(report.Files, file => file.Semantic is { Changed: > 0 });
        var changed = report.Files.First(file => file.Semantic is { Changed: > 0 });
        Assert.Contains(changed.Semantic!.Changes, change => change.Path == "metadata/location");
    }
}
