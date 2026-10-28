using System.Buffers.Binary;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasFormatTests
{
    [Fact]
    public void Primitives_PreserveTagsGuidsIndexesEncodingsAndOpaquePayloads()
    {
        var bytes = GvasFixture.Primitives();
        var parsed = GvasFile.Parse(bytes);
        Assert.Equal(2, parsed.Header.SaveVersion);
        Assert.Equal(1, parsed.Header.CustomVersionCount);
        Assert.Equal("/Script/Test.Save", parsed.Header.SaveClass);
        Assert.Equal(bytes, parsed.Serialize());
        var values = parsed.Properties.ToDictionary(n => n.Name);
        Assert.Equal((Int128)(-1), values["Signed8"].Scalar!.Value);
        Assert.Equal((Int128)long.MinValue, values["Signed64"].Scalar!.Value);
        Assert.Equal((Int128)ulong.MaxValue, values["Unsigned64"].Scalar!.Value);
        Assert.Equal("Hello", values["Localized"].Scalar!.Value);
        Assert.Null(values["SoftObject"].Scalar);
        Assert.Equal(2, values["SoftObject"].Children.Count);
        Assert.Null(values["Unknown"].Scalar);
    }

    [Fact]
    public void PrimitiveEdits_SurviveDocumentReopenAndUnicodeLengthChanges()
    {
        var plugin = new UnrealGvasPlugin();
        using var document = plugin.Open(MemorySaveSet.SingleFile("data.sav", GvasFixture.Primitives()));
        var fields = Expand(document);
        var requested = new Dictionary<string, FieldValue>
        {
            ["Count"] = FieldValue.Of(-321L), ["Health"] = FieldValue.Of(2.5), ["Enabled"] = FieldValue.Of(false),
            ["Name"] = FieldValue.Of("A much longer name 🧙"), ["Wide"] = FieldValue.Of(""),
            ["Localized"] = FieldValue.Of("Bonjour 世界"), ["Invariant"] = FieldValue.Of("Wörld"),
            ["Enum"] = FieldValue.Of("ETest::Second"), ["EnumByte"] = FieldValue.Of("ETest::LongerValue"),
            ["Unsigned64"] = FieldValue.Of(ulong.MaxValue - 1), ["Double"] = FieldValue.Of(-9876.54321),
            ["SubPathString"] = FieldValue.Of("A longer subpath"),
        };
        var edits = fields.Where(f => requested.ContainsKey(Name(f))).Select(f => new ValueEdit(f.ValueId, requested[Name(f)])).ToArray();
        Assert.Equal(requested.Count, edits.Length);
        Assert.True(document.ApplyEdits(edits).Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF, 0x42 }, output[^4..]);
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("data.sav", output));
        var actual = Expand(reopened);
        foreach (var field in actual.Where(f => requested.ContainsKey(Name(f)))) Assert.Equal(requested[Name(field)], reopened.GetValue(field.ValueId));
        foreach (var name in new[] { "Unknown" })
        {
            var before = GvasFile.Parse(GvasFixture.Primitives());
            var after = GvasFile.Parse(output);
            var oldNode = before.Properties.Single(n => n.Name == name);
            var newNode = after.Properties.Single(n => n.Name == name);
            Assert.True(before.Original.Slice(oldNode.Offset, oldNode.Length).Span.SequenceEqual(after.Original.Slice(newNode.Offset, newNode.Length).Span));
        }
    }

    [Fact]
    public void NestedStringEdits_RecomputeStructArrayAndOuterSizes()
    {
        var bytes = GvasFixture.Nested();
        var parsed = GvasFile.Parse(bytes);
        Assert.Equal(bytes, parsed.Serialize());
        var all = GvasFile.Descendants(parsed.Properties).ToArray();
        Assert.DoesNotContain(all, n => n.Note?.Contains("opaque", StringComparison.Ordinal) == true);
        var captions = all.Where(n => n.Name == "Caption").ToArray();
        Assert.Equal(2, captions.Length);
        captions[0].Scalar!.Value = "A longer caption 雪";
        all.Single(n => n.Name == "[1] Key").Scalar!.Value = "A longer map key";
        var output = parsed.Serialize();
        var reparsed = GvasFile.Parse(output);
        var reopened = GvasFile.Descendants(reparsed.Properties).ToArray();
        Assert.Equal("A longer caption 雪", reopened.First(n => n.Name == "Caption").Scalar!.Value);
        Assert.Equal("A longer map key", reopened.Single(n => n.Name == "[1] Key").Scalar!.Value);
        Assert.Equal(output, reparsed.Serialize());
    }

    [Fact]
    public void NestedEdits_ReopenWithStableViewIdsAndCanBeUndoneExactly()
    {
        var plugin = new UnrealGvasPlugin();
        var bytes = GvasFixture.Nested();
        using var document = plugin.Open(MemorySaveSet.SingleFile("save", bytes));
        var fields = Expand(document);
        var caption = fields.First(f => Name(f) == "Caption");
        var quantity = fields.First(f => Name(f) == "Quantity");
        var edits = new[] { new ValueEdit(caption.ValueId, FieldValue.Of("Longer Unicode caption 🪄")), new ValueEdit(quantity.ValueId, FieldValue.Of(99L)) };
        var outcome = document.ApplyEdits(edits);
        Assert.True(outcome.Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("save", output));
        _ = Expand(reopened);
        foreach (var edit in edits) Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
        Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.True(bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void InvalidBatches_AreAtomicAndUnknownFieldsAreReadOnly()
    {
        var bytes = GvasFixture.Primitives();
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save", bytes));
        var fields = Expand(document);
        var count = fields.Single(f => Name(f) == "Count");
        var name = fields.Single(f => Name(f) == "Name");
        var outcome = document.ApplyEdits([new(count.ValueId, FieldValue.Of(999L)), new(name.ValueId, FieldValue.Of("bad\0name"))]);
        Assert.False(outcome.Succeeded);
        Assert.Equal(count.Value, document.GetValue(count.ValueId));
        Assert.False(document.ApplyEdits([new(count.ValueId, FieldValue.Of(ulong.MaxValue))]).Succeeded);
        Assert.False(document.ApplyEdits([new("missing", FieldValue.Of(0L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EveryTruncationOfMinimalGvas_IsRejectedCleanly()
    {
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Count", "IntProperty", GvasFixture.Body(b => b.Write(5))), suffix: false);
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bytes.AsSpan(0, length)));
    }

    [Fact]
    public void InvalidSizesStringsGuidsAndBooleans_AreRejectedCleanly()
    {
        var bytes = GvasFixture.Build(w => GvasFixture.Property(w, "Flag", "BoolProperty", [], boolean: true));
        var badBool = bytes.ToArray();
        var nameOffset = Find(badBool, "BoolProperty\0");
        badBool[nameOffset + "BoolProperty\0".Length + 8] = 2;
        Assert.Throws<SaveFormatException>(() => GvasFile.Parse(badBool));
        foreach (var badLength in new[] { -1, int.MinValue, int.MaxValue })
        {
            var bad = bytes.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(nameOffset - 4), badLength);
            Assert.Throws<SaveFormatException>(() => GvasFile.Parse(bad));
        }
        var invalidGuid = bytes.ToArray();
        invalidGuid[nameOffset + "BoolProperty\0".Length + 9] = 2;
        Assert.Throws<SaveFormatException>(() => GvasFile.Parse(invalidGuid));
    }

    [Fact]
    public void UnknownTextHistoryAndNonFiniteFloats_RemainOpaque()
    {
        var bytes = GvasFixture.Build(w =>
        {
            GvasFixture.Property(w, "Text", "TextProperty", [0, 0, 0, 0, 99, 1, 2, 3]);
            GvasFixture.Property(w, "Float", "FloatProperty", GvasFixture.Body(b => b.Write(float.NaN)));
        });
        var parsed = GvasFile.Parse(bytes);
        Assert.All(parsed.Properties, n => Assert.Null(n.Scalar));
        Assert.Equal(bytes, parsed.Serialize());
    }

    internal static IReadOnlyList<FieldNode> Expand(ISaveDocument document)
    {
        var result = new List<FieldNode>();
        var pending = new Queue<ViewNode>();
        pending.Enqueue(document.BuildView());
        var budget = 10_000;
        while (pending.TryDequeue(out var node))
        {
            Assert.True(budget-- > 0, "View expansion exceeded test safety budget.");
            if (node is FieldNode field) result.Add(field);
            IEnumerable<ViewNode> children = node switch
            {
                TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children),
                SectionNode { LazyId: { } lazy } => document.ExpandNode(lazy),
                SectionNode section => section.Children,
                TableNode table => table.Rows.SelectMany(r => r.Cells.Values),
                _ => [],
            };
            foreach (var child in children) pending.Enqueue(child);
        }
        Assert.Equal(result.Count, result.Select(f => f.ValueId).Distinct(StringComparer.Ordinal).Count());
        return result;
    }

    private static string Name(FieldNode field) => field.Label.Split(" · ")[0];
    private static int Find(byte[] data, string value) => data.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(value));
}
