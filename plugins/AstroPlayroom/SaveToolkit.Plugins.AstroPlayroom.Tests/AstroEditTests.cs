using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroEditTests
{
    [Theory]
    [InlineData("0", 123456789)]
    [InlineData("123456789", 0)]
    [InlineData("0000", 4)]
    public void LengthChangingEdit_PreservesEveryOtherByteAndUndo(string original, long value)
    {
        var sourceText = "\r\n<PreInstall><Common><NumCoin> \t" + original + "\r\n </NumCoin>" +
            "<PlayCount>1</PlayCount></Common><Unknown a='&quot;>中'> keep &amp; me </Unknown></PreInstall>stale>tail";
        var source = AstroTestData.Bytes(sourceText);
        using var document = AstroTestData.Open(source);
        var coins = AstroTestData.Find(document, AstroTestData.CoinsPath);
        Assert.True(document.ApplyEdits([new ValueEdit(coins.ValueId, FieldValue.Of(value))]).Succeeded);
        var output = AstroTestData.Output(document);
        Assert.Equal(AstroTestData.AllocationSize, output.Length);
        var expected = sourceText.Replace($"> \t{original}\r\n", $"> \t{value}\r\n", StringComparison.Ordinal);
        Assert.Equal(AstroTestData.Bytes(expected), output);
        using var reopened = AstroTestData.Open(output);
        Assert.Equal(FieldValue.Of(value), AstroTestData.Find(reopened, AstroTestData.CoinsPath).Value);
        Assert.True(document.ApplyEdits([new ValueEdit(coins.ValueId, coins.Value)]).Succeeded);
        Assert.Equal(source, AstroTestData.Output(document));
    }

    [Fact]
    public void MultipleEdits_KeepIdsAndReportUndoValues()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes());
        var coins = AstroTestData.Find(document, AstroTestData.CoinsPath);
        var count = AstroTestData.Find(document, AstroTestData.CountPath);
        var outcome = document.ApplyEdits([
            new ValueEdit(coins.ValueId, FieldValue.Of(12345L)), new ValueEdit(count.ValueId, FieldValue.Of(9L))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Changes.Count);
        Assert.Equal(coins.Value, outcome.Changes[0].OldValue);
        using var reopened = AstroTestData.Open(AstroTestData.Output(document));
        Assert.Equal(FieldValue.Of(12345L), reopened.GetValue(coins.ValueId));
        Assert.Equal(FieldValue.Of(9L), reopened.GetValue(count.ValueId));
    }

    [Fact]
    public void BooleanLengthChanges_PreserveSampleSuffixAndEveryUneditedField()
    {
        foreach (var file in AstroTestData.Samples())
        {
            var original = file.ReadAllBytes();
            var rootEnd = original.AsSpan().IndexOf("</PreInstall>"u8) + "</PreInstall>"u8.Length;
            var terminator = original.AsSpan(AstroTestData.TextOffset).IndexOf((byte)0) + AstroTestData.TextOffset;
            var suffix = original[rootEnd..terminator];
            using var document = new AstroPlayroomPlugin().Open(file);
            var fields = AstroTestData.Fields(document);
            var boss = Assert.Single(fields, f => f.Description == "PreInstall/Progress/IsBossDefeated");
            Assert.True(document.ApplyEdits([new ValueEdit(boss.ValueId, FieldValue.Of(true))]).Succeeded);
            var output = AstroTestData.Output(document);
            Assert.Equal(AstroTestData.AllocationSize, output.Length);
            Assert.Equal(terminator - 1, output.AsSpan(AstroTestData.TextOffset).IndexOf((byte)0) + AstroTestData.TextOffset);
            Assert.True(output.AsSpan(rootEnd - 1, suffix.Length).SequenceEqual(suffix));
            using var reopened = AstroTestData.Open(output);
            foreach (var field in fields)
            {
                Assert.Equal(field.ValueId == boss.ValueId ? FieldValue.Of(true) : field.Value, reopened.GetValue(field.ValueId));
            }

            Assert.True(document.ApplyEdits([new ValueEdit(boss.ValueId, boss.Value)]).Succeeded);
            Assert.Equal(original, AstroTestData.Output(document));
        }
    }

    [Fact]
    public void CapacityCheck_RejectsWholeBatch_AndAllowsExactFit()
    {
        var prefix = AstroTestData.MinimalRoot;
        // Two spare bytes before the required NUL: two one-digit growth edits exactly fit.
        var text = prefix + new string('x', AstroTestData.AllocationSize - AstroTestData.TextOffset - Encoding.UTF8.GetByteCount(prefix) - 3);
        var original = AstroTestData.Bytes(text);
        using var document = AstroTestData.Open(original);
        var coins = AstroTestData.Find(document, AstroTestData.CoinsPath);
        var count = AstroTestData.Find(document, AstroTestData.CountPath);
        var rejected = document.ApplyEdits([
            new ValueEdit(coins.ValueId, FieldValue.Of(12L)), new ValueEdit(count.ValueId, FieldValue.Of(123L))]);
        Assert.False(rejected.Succeeded);
        Assert.Empty(rejected.Changes);
        Assert.Equal(original, AstroTestData.Output(document));
        Assert.True(document.ApplyEdits([
            new ValueEdit(coins.ValueId, FieldValue.Of(12L)), new ValueEdit(count.ValueId, FieldValue.Of(12L))]).Succeeded);
        var output = AstroTestData.Output(document);
        Assert.Equal(0, output[^1]);
        Assert.NotEqual(0, output[^2]);
        using var reopened = AstroTestData.Open(output);
        Assert.Equal(FieldValue.Of(12L), reopened.GetValue(coins.ValueId));
        Assert.False(document.ApplyEdits([new ValueEdit(coins.ValueId, FieldValue.Of(123L))]).Succeeded);
        Assert.Equal(output, AstroTestData.Output(document));
    }

    [Fact]
    public void InvalidOrUnknownEdit_IsAtomic_AndSessionsAreIsolated()
    {
        var original = AstroTestData.Bytes();
        using var first = AstroTestData.Open(original);
        using var second = AstroTestData.Open(original);
        var coins = AstroTestData.Find(first, AstroTestData.CoinsPath);
        foreach (var invalid in new[] { FieldValue.Of(-1L), FieldValue.Of((long)int.MaxValue + 1), FieldValue.Of("<injected>") })
        {
            Assert.False(first.ApplyEdits([new ValueEdit(coins.ValueId, invalid)]).Succeeded);
            Assert.Equal(original, AstroTestData.Output(first));
        }

        Assert.False(first.ApplyEdits([
            new ValueEdit(coins.ValueId, FieldValue.Of(99L)), new ValueEdit("missing", FieldValue.Of(true))]).Succeeded);
        Assert.Equal(original, AstroTestData.Output(first));
        Assert.True(first.ApplyEdits([new ValueEdit(coins.ValueId, FieldValue.Of(99L))]).Succeeded);
        Assert.Equal(original, AstroTestData.Output(second));
    }
}
