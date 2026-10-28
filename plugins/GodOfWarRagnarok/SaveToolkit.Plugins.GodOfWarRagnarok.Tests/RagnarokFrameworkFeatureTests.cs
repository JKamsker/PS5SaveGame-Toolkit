using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokFrameworkFeatureTests
{
    [Fact]
    public void ProductionF12DecoderPublishesBoundedStablePaths()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(f => f.FileName == "GOWRSAVE0");
        var decoder = Assert.IsAssignableFrom<ISaveDecoderProvider>(new GodOfWarRagnarokPlugin()).Decoders.Single();
        Assert.Equal("gow-ragnarok-evidence", decoder.Id);
        Assert.True(decoder.CanDecode(file));
        var sink = new Sink(20_000, 512, 8, 32 * 1024 * 1024);
        decoder.Decode(file, sink);
        Assert.False(sink.Truncated);
        Assert.Contains(sink.Values, value => value.Path == "metadata/location");
        Assert.Contains(sink.Values, value => value.Path.Contains("/quantity", StringComparison.Ordinal));
        Assert.Contains(sink.Values, value => value.Path.StartsWith("integrity[algorithm=SHA-256", StringComparison.Ordinal));
        Assert.Contains(sink.Values, value => value.Path.StartsWith("flags[", StringComparison.Ordinal));
        Assert.All(sink.Values, value => Assert.InRange(value.Path.Length, 1, sink.MaxValueLength));
    }

    [Fact]
    public void ResourceInspectionIsNonMutatingAndIncludesEvidenceWarnings()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(f => f.FileName == "GOWRSAVE0");
        using var document = new GodOfWarRagnarokPlugin().Open(file);
        var before = file.ReadAllBytes();
        var row = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("resources", new() { Text = "Hacksilver", Limit = 1 }).Rows.Single();
        var action = Assert.IsType<ActionNode>(row.Cells["inspect"]);
        Assert.False(action.Mutates);
        var outcome = document.InvokeAction(action.ActionId, new Dictionary<string, FieldValue>());
        Assert.Contains("Hacksilver", outcome.Message);
        Assert.Contains(outcome.Warnings, warning => warning.Contains("Integrity unknown", StringComparison.Ordinal));
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private sealed class Sink(int maxValues, int maxValueLength, int maxDepth, long maxFileBytes) : ISaveDecodeSink
    {
        public int MaxValues => maxValues;
        public int MaxValueLength => maxValueLength;
        public int MaxDepth => maxDepth;
        public long MaxFileBytes => maxFileBytes;
        public List<(string Path, string Kind, string Value)> Values { get; } = [];
        public bool Truncated { get; private set; }
        public bool Add(string path, string kind, string value, string? label = null)
        {
            if (Values.Count == MaxValues || path.Length > MaxValueLength || value.Length > MaxValueLength) return false;
            Values.Add((path, kind, value));
            return true;
        }
        public void Warn(string message) { }
        public void MarkTruncated(string reason) => Truncated = true;
    }
}
