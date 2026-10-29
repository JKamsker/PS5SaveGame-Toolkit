using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ProgressionViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebuildingProgressionViewRetainsBindingsWarningsMixedEditsAndExactRestoration(bool invalidMirrors)
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var tree = CsavTree.Parse(original);
        var payload = original.Payload.ToArray();
        if (invalidMirrors)
        {
            var offset = PlayerStatsReader.Read(original, tree)[PlayerStatsReader.NameHash("Strength")];
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset), float.NaN);
        }
        var bytes = original.Rebuild(payload);
        using var document = ProgressionSampleTests.Open(bytes);
        var before = View(document);
        Assert.Equal(before, View(document));
        var fields = CyberpunkSampleTests.ExpandAll(document);
        var attribute = Assert.Single(fields, field => field.ValueId == "progression/attributes/Strength");
        var level = Assert.Single(fields, field => field.ValueId == "progression/Level/level");
        Assert.Equal(invalidMirrors, attribute.Spec.ReadOnly);
        Assert.Equal(invalidMirrors, level.Spec.ReadOnly);
        if (invalidMirrors)
        {
            const string warning = "Ambiguous or invalid player stat modifiers; attributes remain read-only.";
            Assert.Equal(warning, attribute.Warning);
            Assert.Equal(warning, level.Warning);
        }
        ValueEdit[] edits = [new("progression/Level/xp", FieldValue.Of(17L)),
            new("progression/StreetCred/threshold", FieldValue.Of(31L)), new("progression/points/Primary", FieldValue.Of(9L))];
        var outcome = document.ApplyEdits(edits);
        Assert.True(outcome.Succeeded);
        var after = View(document);
        Assert.NotEqual(before, after);
        Assert.Equal(after, View(document));
        var written = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = ProgressionSampleTests.Open(written);
        Assert.Equal(after, View(reopened));
        foreach (var edit in edits) Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
        Assert.Equal(written, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        Assert.Equal(before, View(document));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static string View(ISaveDocument document) => JsonSerializer.Serialize(
        Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Player development"));
}
