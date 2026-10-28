using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ProgressionValidationTests
{
    [Theory]
    [InlineData(0, 3)]
    [InlineData(0, 5)]
    [InlineData(1, 1)]
    [InlineData(2, 7)]
    public void UnverifiedScriptablePackageLayoutRefusesProgressionEditsWithReason(int offset, byte value)
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var node = Assert.Single(CsavTree.Parse(original).Nodes, n => n.Name == "ScriptableSystemsContainer");
        var payload = original.Payload.ToArray();
        payload[node.Offset + 8 + offset] = value;
        var bytes = original.Rebuild(payload);
        using var document = ProgressionSampleTests.Open(bytes);
        Assert.DoesNotContain(CyberpunkSampleTests.ExpandAll(document), f => f.ValueId.StartsWith("progression/", StringComparison.Ordinal));
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var warning = Assert.IsType<TextNode>(Assert.Single(Assert.Single(tabs.Tabs, t => t.Title == "Player development").Children));
        Assert.Contains("edits refused", warning.Text, StringComparison.Ordinal);
        Assert.False(document.ApplyEdits([new("progression/points/Primary", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(8, int.MaxValue)] // Name descriptor pool.
    [InlineData(12, int.MaxValue)] // Name data pool.
    [InlineData(16, int.MaxValue)] // Chunk descriptor pool.
    [InlineData(20, int.MaxValue)] // Chunk data pool.
    [InlineData(24, int.MaxValue)] // Root count.
    public void VerifiedPackageWithCorruptBoundsIsRejected(int offset, int value)
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var node = Assert.Single(CsavTree.Parse(original).Nodes, n => n.Name == "ScriptableSystemsContainer");
        var payload = original.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(node.Offset + 8 + offset), value);
        var bytes = original.Rebuild(payload);
        Assert.Throws<SaveFormatException>(() => ProgressionSampleTests.Open(bytes));
    }

    [Fact]
    public void MismatchedPlayerStatDisablesOnlyItsAttributeAndCannotBePatched()
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var data = ProgressionData.TryRead(original, CsavTree.Parse(original), out _)!;
        var strength = Assert.Single(data.Attributes, a => a.Id == "attributes/Strength");
        var payload = original.Payload.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(strength.MirrorOffset!.Value), 4);
        var bytes = original.Rebuild(payload);
        using var document = ProgressionSampleTests.Open(bytes);
        var fields = CyberpunkSampleTests.ExpandAll(document);
        var field = Assert.Single(fields, f => f.ValueId == "progression/attributes/Strength");
        Assert.True(field.Spec.ReadOnly);
        Assert.NotNull(field.Warning);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(20L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void InvalidMixedBatchAndReadOnlyProgressionLeaveBothCopiesAndPointBalancesUnchanged()
    {
        var file = ProgressionSampleTests.Files()[0];
        using var document = new Cyberpunk2077Plugin().Open(file);
        _ = CyberpunkSampleTests.ExpandAll(document);
        foreach (var bad in new[] { new ValueEdit("progression/points/Primary", FieldValue.Of(-1L)),
            new ValueEdit("progression/attributes/Cool", FieldValue.Of(21L)),
            new ValueEdit("progression/attributes/Cool", FieldValue.Of("invalid")),
            new ValueEdit("progression/Level/level", FieldValue.Of(10L)),
            new ValueEdit("progression/StreetCred/xp", FieldValue.Of(100L)) })
        {
            var outcome = document.ApplyEdits([new("progression/attributes/Strength", FieldValue.Of(20L)),
                new("progression/points/Espionage", FieldValue.Of(10L)), bad]);
            Assert.False(outcome.Succeeded);
            Assert.Equal(FieldValue.Of(3L), document.GetValue("progression/attributes/Strength"));
            Assert.Equal(FieldValue.Of(3.0), document.GetValue("stats/attributes/Strength"));
            Assert.Equal(FieldValue.Of(0L), document.GetValue("progression/points/Espionage"));
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }
}
