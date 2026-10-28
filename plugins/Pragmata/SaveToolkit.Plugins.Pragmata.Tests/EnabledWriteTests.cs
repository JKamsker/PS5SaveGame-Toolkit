using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Research;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class EnabledWriteTests
{
    private static readonly string[] Families = ["Gadgets", "Loadout", "Progress", "Quests", "World"];
    private static readonly DecodeLimits Full = new() { MaxValues = 1_000_000, MaxDepth = 100 };

    [Fact]
    public void AllSampleFamilyEditsPersistWithOnlyIntendedF1DeltasAndOneUndoGroup()
    {
        foreach (var file in SampleEvidenceTests.Files().OfType<DiskSaveFile>().Where(file => file.FileName != "data00-1.bin"))
        {
            var original = file.ReadAllBytes();
            using var document = new PragmataDocument(file.RelativePath, original);
            using var session = new DocumentSession("enabled-families", file, new PragmataPlugin(), document);
            var tabs = ((TabsNode)session.BuildView()).Tabs;
            var fields = Families.Select(title => FirstField(tabs.Single(tab => tab.Title == title).Children,
                session.Expand, table => session.QueryCollection(table.CollectionId,
                    new QueryableCollectionQuery { Limit = 3 }, session.State.Revision))).ToArray();
            Assert.Equal(Families.Length, fields.Select(field => field.ValueId).Distinct(StringComparer.Ordinal).Count());
            Assert.All(fields, AssertEnabled);
            var outcome = session.ApplyEdits(fields.Select(field => new IncomingEdit(field.ValueId, Wire(Other(field)))).ToArray());
            Assert.True(outcome.Ok);
            Assert.Contains(outcome.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
            var modified = session.Serialize().Single().Content.ToArray();
            var before = new DsssContainer(original).Plaintext;
            var after = new DsssContainer(modified).Plaintext;
            var targets = fields.Select(field => Find(document.Layout.Roots, field.ValueId["inspect/value/".Length..])).ToArray();
            for (var index = 0; index < targets.Length; index++) Assert.Equal(fields[index].Spec, document.Bounds.Spec(targets[index]));
            AssertOnlyRanges(before, after, targets);
            var decoder = new PragmataDecoder();
            var baseline = decoder.Decode(before, new SaveLayout(before).Roots, Full);
            var semantic = SemanticDiffer.Compare(baseline,
                decoder.Decode(after, new SaveLayout(after).Roots, Full));
            Assert.False(semantic.InputTruncated);
            Assert.False(semantic.Truncated);
            // Changing a stored identity can legitimately appear as removed/added F1 paths.
            // Compare the complete expected delta from only the requested primitive patches.
            var expected = before.ToArray();
            for (var index = 0; index < targets.Length; index++) PrimitiveCodec.Write(targets[index], expected, Other(fields[index]));
            var intended = SemanticDiffer.Compare(baseline, decoder.Decode(expected, new SaveLayout(expected).Roots, Full));
            Assert.False(intended.InputTruncated);
            Assert.False(intended.Truncated);
            Assert.NotEmpty(semantic.Changes);
            Assert.Equal(intended.Changes, semantic.Changes);
            using var reopened = new PragmataDocument(file.RelativePath, modified);
            Assert.All(fields, field => Assert.Equal(Other(field), reopened.Read(Find(reopened.Layout.Roots,
                field.ValueId["inspect/value/".Length..]))));
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.CanUndo);
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
        }
    }

    [Theory]
    [InlineData("Gadgets")]
    [InlineData("Loadout")]
    [InlineData("Progress")]
    [InlineData("Quests")]
    [InlineData("World")]
    public void FamilyControlsRejectWrongTypesAndOutOfBoundsWithoutPartialWrites(string title)
    {
        using var document = EditingTests.Gameplay();
        var tab = ((TabsNode)document.BuildView()).Tabs.Single(tab => tab.Title == title);
        var field = FirstField(tab.Children, document.ExpandNode,
            table => document.Query(table.CollectionId, new QueryableCollectionQuery { Limit = 3 }));
        var before = document.Serialize().Single().Content.ToArray();
        var invalid = field.Spec switch
        {
            IntegerSpec integer => FieldValue.Of(integer.Max + 1),
            FloatSpec floating => FieldValue.Of((floating.Max ?? double.MaxValue) + 1),
            _ => FieldValue.Of(2L),
        };
        foreach (var value in new[] { invalid, FieldValue.Of("invalid") })
            Assert.False(document.ApplyEdits([new(EditingTests.Quantity(document).Path, FieldValue.Of(1L)),
                new(field.ValueId, value)]).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void AllSampleReferenceIdsEditReopenRejectInvalidTextAndUndoRedo()
    {
        foreach (var file in SampleEvidenceTests.Files().OfType<DiskSaveFile>().Where(file => file.FileName != "data00-1.bin"))
        {
            var original = file.ReadAllBytes();
            using var document = new PragmataDocument(file.RelativePath, original);
            using var session = new DocumentSession("enabled-reference", file, new PragmataPlugin(), document);
            var table = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<TableNode>().Single();
            var field = (FieldNode)table.Rows[0].Cells["name"];
            AssertEnabled(field);
            Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement("1"))]).Ok);
            var modified = session.Serialize().Single().Content.ToArray();
            using var reopened = new PragmataDocument(file.RelativePath, modified);
            var itemId = document.Layout.Items[0].Find(KnownHashes.ItemId)!;
            Assert.Equal(1U, reopened.Read(reopened.Layout.Items[0].Find(KnownHashes.ItemId)!).AsUInt32());
            AssertOnlyRanges(new DsssContainer(original).Plaintext, new DsssContainer(modified).Plaintext, [itemId]);
            foreach (var invalid in new[] { "-1", "4294967296", "1.0", "+1", "not-an-id" })
                Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(invalid))]).Succeeded);
            Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1L))]).Succeeded);
            var maximum = Assert.IsType<IntegerSpec>(document.Bounds.Spec(itemId)).Max;
            if (maximum < uint.MaxValue)
                Assert.False(session.ApplyEdits([new(field.ValueId, Wire(FieldValue.Of(maximum + 1)))]).Ok);
            Assert.Equal(modified, document.Serialize().Single().Content.ToArray());
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
        }
    }

    [Fact]
    public void AllWritableFieldsAndQuantityPlansKeepConsoleAndIntegrityWarnings()
    {
        using var document = EditingTests.Gameplay();
        var tabs = ((TabsNode)document.BuildView()).Tabs;
        Assert.All(tabs.Single(tab => tab.Title == "Overview").Children.OfType<SectionNode>()
            .SelectMany(section => section.Children).OfType<FieldNode>(), AssertEnabled);
        var inventory = tabs.Single(tab => tab.Title == "Inventory");
        var table = inventory.Children.OfType<TableNode>().Single();
        Assert.All(table.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>(), AssertEnabled);
        var action = inventory.Children.OfType<ActionNode>().Single();
        Assert.True(action.SupportsPlanning);
        Assert.Contains("RSA", action.Description!, StringComparison.Ordinal);
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(17L) };
        var plan = document.PlanAction(PragmataViews.QuantityAction, parameters, new ActionScope());
        Assert.Contains(plan.Warnings, warning => warning.Contains("RSA", StringComparison.Ordinal));
        var outcome = document.ApplyActionPlan(PragmataViews.QuantityAction, parameters, new ActionScope(), plan);
        Assert.Contains(outcome.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferenceAndRawAliasConflictsRejectAtomically()
    {
        using var document = EditingTests.Gameplay();
        var target = document.Layout.Items[0].Find(KnownHashes.ItemId)!;
        var reference = document.References.ItemField(target);
        var raw = document.Field(target, raw: true);
        var original = document.Serialize().Single().Content.ToArray();
        Assert.False(document.ApplyEdits([new(reference.ValueId, FieldValue.Of("1")), new(raw.ValueId, FieldValue.Of(2L))]).Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        var outcome = document.ApplyEdits([new(reference.ValueId, FieldValue.Of("1")), new(raw.ValueId, FieldValue.Of(1L))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Changes.Count);
    }

    private static FieldNode FirstField(IReadOnlyList<ViewNode> nodes, Func<string, IReadOnlyList<ViewNode>> expand,
        Func<QueryableTableNode, QueryableCollectionPage> query)
    {
        var pending = new Queue<ViewNode>(nodes);
        var budget = 200;
        while (pending.TryDequeue(out var node) && budget-- > 0)
        {
            if (node is FieldNode field) return field;
            IEnumerable<ViewNode> children = node switch
            {
                SectionNode section => section.LazyId is { } lazy ? expand(lazy) : section.Children,
                QueryableTableNode table => query(table).Rows.SelectMany(row => row.Cells.Values),
                _ => [],
            };
            foreach (var child in children) pending.Enqueue(child);
        }
        throw new InvalidOperationException("Family has no bounded, existing primitive control.");
    }

    private static void AssertEnabled(FieldNode field)
    {
        Assert.False(field.Spec.ReadOnly);
        Assert.NotEqual(Confidence.Verified, field.Confidence);
        Assert.Contains("not validated on console", field.Description!, StringComparison.Ordinal);
        Assert.Contains("RSA", field.Description!, StringComparison.Ordinal);
        Assert.Contains("game may reject", field.Description!, StringComparison.Ordinal);
    }

    private static FieldValue Other(FieldNode field) => field.Spec switch
    {
        BoolSpec => FieldValue.Of(!field.Value.AsBool()),
        IntegerSpec => FieldValue.Of(field.Value.AsInteger() == 0 ? 1L : 0L),
        FloatSpec => FieldValue.Of(field.Value.AsDouble() == 0 ? 0.5 : 0),
        _ => throw new InvalidOperationException("Expected a primitive."),
    };

    private static JsonElement Wire(FieldValue value) => value.Kind switch
    {
        FieldValueKind.Bool => JsonSerializer.SerializeToElement(value.AsBool()),
        FieldValueKind.Integer => JsonSerializer.SerializeToElement(value.ToString()),
        _ => JsonSerializer.SerializeToElement(value.AsDouble()),
    };

    private static SaveNode Find(IEnumerable<SaveNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.Path == path) return node;
            if (path.StartsWith(node.Path, StringComparison.Ordinal)) return Find(node.Children, path);
        }
        throw new KeyNotFoundException(path);
    }

    private static void AssertOnlyRanges(byte[] before, byte[] after, IReadOnlyList<SaveNode> targets)
    {
        Assert.Equal(before.Length, after.Length);
        var diff = ByteDiffer.Compare(before, after, options: new ByteDiffOptions { MergeGap = 0 });
        Assert.Equal(diff.RangeCount, diff.Ranges.Count);
        Assert.NotEmpty(diff.Ranges);
        Assert.All(diff.Ranges, range => Assert.Contains(targets,
            target => range.Offset >= target.Offset && range.Offset + range.Length <= target.Offset + target.Size));
    }
}
