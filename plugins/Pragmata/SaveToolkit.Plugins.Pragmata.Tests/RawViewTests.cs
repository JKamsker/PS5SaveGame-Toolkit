using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class RawViewTests
{
    [Fact]
    public void RawQuantityProjectionUpdatesCanonicalControlAndUndoesAtomically()
    {
        using var document = EditingTests.Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var target = EditingTests.Quantity(document);
        var view = (TabsNode)document.BuildView();
        var raw = RawFieldNavigation.Find(view, target, document.ExpandNode);
        Assert.Equal(Confidence.Inferred, raw.Confidence);
        Assert.Equal(document.GetValue(target.Path), raw.Value);
        var outcome = document.ApplyEdits([new(raw.ValueId, FieldValue.Of(9999L))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(target.Path, raw.ValueId);
        Assert.Single(outcome.Changes);
        Assert.Equal(9999, document.GetValue(target.Path).AsInt32());
        Assert.Equal(9999, document.GetValue(raw.ValueId).AsInt32());
        Assert.False(document.ApplyEdits([new(raw.ValueId, FieldValue.Of(5L)), new(target.Path, FieldValue.Of(6L))]).Succeeded);
        Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void LargeArraysUseBoundedIntermediatePagesAndLeaves()
    {
        const int length = 1_000_000;
        var pages = new LazyPages();
        var root = pages.Section("large", "Large array", length,
            (start, count) => Enumerable.Range(start, count).Select(i => (ViewNode)new TextNode(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
        var queue = new Queue<SectionNode>();
        queue.Enqueue(root);
        var values = 0;
        while (queue.TryDequeue(out var page))
        {
            var nodes = pages.Expand(page.LazyId!);
            Assert.InRange(nodes.Count, 1, LazyPages.PageSize);
            foreach (var section in nodes.OfType<SectionNode>()) queue.Enqueue(section);
            values += nodes.OfType<TextNode>().Count();
        }
        Assert.Equal(length, values);
        Assert.Throws<KeyNotFoundException>(() => pages.Expand("unknown"));
    }

    [Fact]
    public void RawFloatEditReopensAndReversesWithoutTouchingOtherPlaintext()
    {
        using var document = EditingTests.Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var reader = new TypedReader(new Crypto.DsssContainer(original).Plaintext);
        _ = reader.Parse();
        var floating = reader.Classes.SelectMany(c => c.Children).First(n => n.Type == 11 && double.IsFinite(document.Read(n).AsDouble()));
        var field = document.Field(floating, raw: true);
        var spec = Assert.IsType<FloatSpec>(field.Spec);
        var next = field.Value.AsDouble() == 0 ? Math.Min(0.5, spec.Max ?? 0.5) : 0;
        var outcome = document.ApplyEdits([new(field.ValueId, FieldValue.Of(next))]);
        Assert.True(outcome.Succeeded);
        var checkedPlaintext = new Crypto.DsssContainer(document.Serialize().Single().Content.Span).Plaintext;
        Assert.Equal((double)(float)next, PrimitiveCodec.Read(floating, checkedPlaintext).AsDouble());
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }
}
