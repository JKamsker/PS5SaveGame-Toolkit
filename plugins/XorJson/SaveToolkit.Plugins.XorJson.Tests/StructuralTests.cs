using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class StructuralTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemoveKeepsSurvivingReferencesAndDetachedMementosRestoreBytesRepeatedly(bool xor)
    {
        const string original = "\uFEFF{ \"rows\" : [ {\"n\":-0,\"s\":\"\\u0041\"}, {\"n\":1.2300E+02,\"future\":[null]} ],\"n\":7,\"n\":8 }\r\n";
        var bytes = XorJsonTests.Encode(original, xor);
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        var survivor = fields.Single(f => f.Value.Kind == FieldValueKind.ExactNumber && f.Value.AsExactNumber() == "1.2300E+02");
        var nodes = Nodes(document);
        var firstRow = nodes.OfType<SectionNode>().First(n => n.Title == "[0]" && n.LazyId is not null);
        var rowId = firstRow.LazyId!.Split('/')[1];
        var remove = nodes.OfType<ActionNode>().Single(a => a.ActionId == $"json/remove/{rowId}");
        var outcome = document.InvokeAction(remove.ActionId, new Dictionary<string, FieldValue>());
        Assert.True(outcome.Undoable);
        Assert.True(outcome.StructureChanged);
        Assert.NotNull(outcome.Revert);
        Assert.True(outcome.Revert.SizeBytes >= bytes.Length);
        var afterRemove = document.Serialize()[0].Content.ToArray();
        Assert.Contains(XorJsonTests.Fields(document), f => f.ValueId == survivor.ValueId);
        Assert.True(document.ApplyEdits([new(survivor.ValueId, FieldValue.ExactNumber("9.000e+1"))]).Succeeded);
        Assert.True(document.ApplyEdits([new(survivor.ValueId, survivor.Value)]).Succeeded);
        Assert.Equal(afterRemove, document.Serialize()[0].Content.ToArray());
        var redo = document.RestoreMemento(outcome.Revert);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        _ = XorJsonTests.Fields(document);
        Assert.Equal(survivor.Value, document.GetValue(survivor.ValueId));
        var undo = document.RestoreMemento(redo);
        Assert.Equal(afterRemove, document.Serialize()[0].Content.ToArray());
        _ = document.RestoreMemento(undo);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(afterRemove, document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData("{\"items\":[]}", "[7,{\"unknown\":1.00E-02}]")]
    [InlineData("{\"items\":[1]}", "null")]
    [InlineData("{\"items\":{ }}", "{\"nested\":true}")]
    public void AddAndReplacePreserveUnrelatedTokensAndAreReversible(string original, string inserted)
    {
        var bytes = XorJsonTests.Encode(original);
        using var document = XorJsonTests.Open(bytes);
        var actions = Nodes(document).OfType<ActionNode>().ToArray();
        var add = actions.Last(a => a.Label == "Add row/member");
        var parameters = new Dictionary<string, FieldValue> { ["json"] = FieldValue.Of(inserted), ["key"] = FieldValue.Of("new/key") };
        var addition = document.InvokeAction(add.ActionId, parameters);
        var afterAdd = document.Serialize()[0].Content.ToArray();
        using var reopened = XorJsonTests.Open(afterAdd);
        Assert.Equal(afterAdd, reopened.Serialize()[0].Content.ToArray());
        var replace = Nodes(document).OfType<ActionNode>().Last(a => a.Label == "Replace row/member");
        var replacement = document.InvokeAction(replace.ActionId, new Dictionary<string, FieldValue> { ["json"] = FieldValue.Of("{\"replacement\":1.2300E+02}") });
        var afterReplace = document.Serialize()[0].Content.ToArray();
        Assert.NotEqual(afterAdd, afterReplace);
        _ = document.RestoreMemento(replacement.Revert!);
        Assert.Equal(afterAdd, document.Serialize()[0].Content.ToArray());
        _ = document.RestoreMemento(addition.Revert!);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void ScalarUndoAfterStructuralUndoPreservesOriginalEscapesAndNumberSpelling()
    {
        var bytes = XorJsonTests.Encode("{\"s\":\"\\u0041\",\"n\":-0,\"rows\":[1,2]}");
        using var document = XorJsonTests.Open(bytes);
        var fields = XorJsonTests.Fields(document);
        var text = fields.Single(f => f.Label == "s");
        Assert.True(document.ApplyEdits([new(text.ValueId, FieldValue.Of("modified"))]).Succeeded);
        var add = Nodes(document).OfType<ActionNode>().Last(a => a.Label == "Add row/member");
        var outcome = document.InvokeAction(add.ActionId, new Dictionary<string, FieldValue> { ["json"] = FieldValue.Of("3") });
        _ = document.RestoreMemento(outcome.Revert!);
        _ = XorJsonTests.Fields(document);
        Assert.True(document.ApplyEdits([new(text.ValueId, text.Value)]).Succeeded);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData("1,2")]
    [InlineData("{\"x\":01}")]
    [InlineData("[\"\\uD800\"]")]
    [InlineData("[[1]]")]
    public void FailedStructuralActionsRollBackAndKeepScalarBindingsUsable(string fragment)
    {
        var original = "{\"s\":\"ok\",\"rows\":" + new string('[', 62) + "0" + new string(']', 62) + "}";
        var bytes = XorJsonTests.Encode(original);
        using var document = XorJsonTests.Open(bytes);
        var add = Nodes(document).OfType<ActionNode>().Last(a => a.Label == "Add row/member");
        Assert.Throws<SaveFormatException>(() => document.InvokeAction(add.ActionId, new Dictionary<string, FieldValue> { ["json"] = FieldValue.Of(fragment) }));
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        var field = XorJsonTests.Fields(document).Single(f => f.Label == "s");
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of("still live"))]).Succeeded);
    }

    [Fact]
    public void ObjectDuplicateAdditionAndRootRemovalAreRejectedAndRetiredIdsAreNotReused()
    {
        var bytes = XorJsonTests.Encode("{\"x\":1,\"x\":2}");
        using var document = XorJsonTests.Open(bytes);
        var add = Nodes(document).OfType<ActionNode>().Single(a => a.Label == "Add row/member");
        Assert.Throws<SaveFormatException>(() => document.InvokeAction(add.ActionId, new Dictionary<string, FieldValue> { ["key"] = FieldValue.Of("x"), ["json"] = FieldValue.Of("3") }));
        Assert.Throws<SaveFormatException>(() => document.InvokeAction("json/remove/0", new Dictionary<string, FieldValue>()));
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        var added = document.InvokeAction(add.ActionId, new Dictionary<string, FieldValue> { ["key"] = FieldValue.Of("a"), ["json"] = FieldValue.Of("3") });
        var retired = XorJsonTests.Fields(document).Single(f => f.Label == "a").ValueId;
        _ = document.RestoreMemento(added.Revert!);
        var addedAgain = document.InvokeAction(add.ActionId, new Dictionary<string, FieldValue> { ["key"] = FieldValue.Of("b"), ["json"] = FieldValue.Of("4") });
        Assert.NotNull(addedAgain.Revert);
        Assert.DoesNotContain(XorJsonTests.Fields(document), f => f.ValueId == retired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingTheLastRootArrayElementCannotCommitAnAmbiguousEncoding(bool xor)
    {
        var original = XorJsonTests.Encode("[null]", xor);
        using var document = XorJsonTests.Open(original);
        var remove = Nodes(document).OfType<ActionNode>().Single(a => a.Label == "Remove row/member");
        Assert.Throws<SaveFormatException>(() => document.InvokeAction(remove.ActionId, new Dictionary<string, FieldValue>()));
        Assert.Equal(original, document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void RemovedDuplicateMembersRetainTheSurvivorsStableReference()
    {
        var original = XorJsonTests.Encode("{\"\\u0078\":1,\"x\":2,\"untouched\":1.2300e+004}");
        using var document = XorJsonTests.Open(original);
        var fields = XorJsonTests.Fields(document);
        var survivor = fields[1].ValueId;
        var remove = Nodes(document).OfType<ActionNode>().First(a => a.Label == "Remove row/member");
        var outcome = document.InvokeAction(remove.ActionId, new Dictionary<string, FieldValue>());
        Assert.Equal(XorJsonTests.Encode("{\"x\":2,\"untouched\":1.2300e+004}"), document.Serialize()[0].Content.ToArray());
        var after = XorJsonTests.Fields(document);
        Assert.Equal(survivor, after[0].ValueId);
        Assert.Contains("member(\"x\",0)", after[0].Description!);
        _ = document.RestoreMemento(outcome.Revert!);
        Assert.Equal(original, document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void ReplacementPreservesEscapedMemberNameColonAndUnrelatedWhitespace()
    {
        var original = XorJsonTests.Encode("{\r\n \"\\u0078\" : 1.00E-2,\r\n \"future\":{\"a\":-0}\r\n}");
        using var document = XorJsonTests.Open(original);
        var replace = Nodes(document).OfType<ActionNode>().First(a => a.Label == "Replace row/member");
        var outcome = document.InvokeAction(replace.ActionId, new Dictionary<string, FieldValue> { ["json"] = FieldValue.Of("[null,{\"b\":2.00}]") });
        Assert.Equal(XorJsonTests.Encode("{\r\n \"\\u0078\" : [null,{\"b\":2.00}],\r\n \"future\":{\"a\":-0}\r\n}"), document.Serialize()[0].Content.ToArray());
        _ = document.RestoreMemento(outcome.Revert!);
        Assert.Equal(original, document.Serialize()[0].Content.ToArray());
    }

    internal static IReadOnlyList<ViewNode> Nodes(ISaveDocument document)
    {
        var result = new List<ViewNode>();
        Visit(document.BuildView());
        return result;
        void Visit(ViewNode node)
        {
            result.Add(node);
            if (node is TabsNode tabs) foreach (var child in tabs.Tabs.SelectMany(t => t.Children)) Visit(child);
            if (node is SectionNode section)
                foreach (var child in section.LazyId is { } id ? document.ExpandNode(id) : section.Children) Visit(child);
        }
    }
}
