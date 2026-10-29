using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class Sdk06ResourceTests
{
    [Fact]
    public void ScalarPreflightMatchesDefaultJsonEscaping()
    {
        var values = new[]
        {
            FieldValue.Of("controls\b\t\n\f\r\"\\<>&'é日本語😀"),
            FieldValue.ExactNumber("-0.000E+02"), FieldValue.Of(true), FieldValue.Null,
        };
        foreach (var value in values)
        {
            var encoded = value.Kind == FieldValueKind.Text
                ? JsonSerializer.SerializeToUtf8Bytes(value.AsString())
                : value.Kind == FieldValueKind.ExactNumber ? Encoding.UTF8.GetBytes(value.AsExactNumber())
                : value.Kind == FieldValueKind.Bool ? (value.AsBool() ? "true"u8.ToArray() : "false"u8.ToArray())
                : "null"u8.ToArray();
            Assert.Equal(encoded.Length, JsonScalarEncoding.EncodedLength(value));
        }
    }

    [Fact]
    public void OversizedMultibyteScalarRejectsBeforeEncodingAndKeepsSdkState()
    {
        const string prefix = "{\"s\":\"";
        const string suffix = "\"}";
        var sourceText = prefix + new string('a', JsonTokenTree.MaxBytes - Encoding.UTF8.GetByteCount(prefix + suffix) - 1) + suffix;
        var tree = JsonTokenTree.Parse(Encoding.UTF8.GetBytes(sourceText));
        var scalar = tree.Nodes.Single(node => !node.IsContainer);
        scalar.Current = FieldValue.Of(new string('界', 3_000_000));
        var original = tree.Lossless.Serialize();

        var allocated = AllocationProbe.Measure(
            () => Assert.Throws<SaveFormatException>(() => JsonTokenWriter.Write(tree.Nodes[0])),
            collectBetweenRuns: true);
        allocated.AssertLessThan(1_000_000, "Rejected scalar preflight should not materialize a multi-megabyte JSON lexeme");
        Assert.Equal('a', scalar.Original.AsString()[0]);
        Assert.Equal(sourceText.Length - prefix.Length - suffix.Length, scalar.Original.AsString().Length);
        Assert.Equal(scalar.SdkNode!.OriginalLexeme, scalar.SdkNode.CurrentLexeme);
        Assert.Equal(original, tree.Lossless.Serialize());
    }

    [Fact]
    public void MementoOwnsEachSourceOnceAndRestoredScalarTreeUsesSdkPath()
    {
        var source = Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Range(0, 20000)) + "]");
        var tree = JsonTokenTree.Parse(source);
        var model = new JsonEditModel(tree);
        var state = model.Snapshot();
        var snapshotNodes = JsonEditModel.Walk(state.Root).ToArray();
        Assert.All(snapshotNodes, node => Assert.Same(state.Root.Source, node.Source));
        Assert.InRange(state.SizeBytes, source.LongLength, source.LongLength + snapshotNodes.Length * 512L + 1_000_000L);

        var allocated = AllocationProbe.Measure(() => _ = model.Snapshot(), collectBetweenRuns: true);
        allocated.AssertLessThan(32 * 1024 * 1024L, "A shared source must be cloned once per memento");
        for (var i = 0; i < 3; i++)
        {
            model.Restore(state);
            Assert.All(JsonEditModel.Walk(model.Root), node => Assert.Same(model.Root.Source, node.Source));
        }
        Assert.All(JsonEditModel.Walk(model.Root), node => Assert.NotNull(node.SdkNode));
        var value = model.Nodes.Single(node => !node.IsContainer && node.Index == 1);
        value.Current = FieldValue.ExactNumber("999999");
        var changed = JsonTokenWriter.Write(model.Root);
        Assert.Contains((byte)'9', changed);

        model.Change("add", model.Root.Index, "2", "added");
        var structural = model.Snapshot();
        var structuralSources = JsonEditModel.Walk(structural.Root).Select(node => node.Source)
            .Distinct(ReferenceEqualityComparer.Instance).ToArray();
        Assert.Equal(2, structuralSources.Length);
        model.Restore(structural);
        Assert.Equal(2, JsonEditModel.Walk(model.Root).Select(node => node.Source)
            .Distinct(ReferenceEqualityComparer.Instance).Count());
    }
}
