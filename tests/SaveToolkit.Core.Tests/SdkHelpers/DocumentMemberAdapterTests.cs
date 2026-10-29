using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class DocumentMemberAdapterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a~1b/%2F + 雪")]
    public void PrefixIsOrdinalAndLossless_WithoutImplicitEscaping(string id)
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("character", document);
        Assert.Equal("character/" + id, member.Prefix(id));
        Assert.Equal(id, member.Local(member.Prefix(id)));
        Assert.Throws<KeyNotFoundException>(() => member.Local("Character/" + id));
        Assert.Throws<KeyNotFoundException>(() => member.Local("characters/" + id));
    }

    [Fact]
    public void DispatchLocalizesRequestsAndQualifiesOutcomesIncludingErrors()
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("a", document);
        var id = member.Prefix(MemberAdapterDocument.ValueId);
        Assert.Equal(FieldValue.Of(1), member.GetValue(id));
        Assert.Equal(id, Assert.IsType<FieldNode>(Assert.Single(member.ExpandNode("a/lazy"))).ValueId);
        Assert.Equal(new byte[] { 42 }, member.ReadBlob("a/blob", 0, 1).ToArray());
        var query = new QueryableCollectionQuery { Text = "filter", Limit = 2 };
        var page = member.Query("a/items", query);
        Assert.Same(query, document.LastQuery);
        Assert.Equal("a/items", page.CollectionId);
        Assert.Equal("a/" + MemberAdapterDocument.RowKey, Assert.Single(page.Rows).Key);
        Assert.Equal("opaque/~%", page.NextCursor);
        Assert.Equal(7, page.Revision);
        Assert.Equal(2, page.HiddenEmptyCount);
        Assert.Equal("a: Items", Assert.Single(member.Collections).Title);
        var outcome = member.ApplyEdits([new(id, FieldValue.Of(3)), new(id, FieldValue.Of(4))]);
        Assert.Equal([MemberAdapterDocument.ValueId, MemberAdapterDocument.ValueId], document.LastEdits!.Select(e => e.ValueId));
        Assert.Equal([1, 3], outcome.Changes.Select(c => c.OldValue.AsInt32()));
        Assert.All(outcome.Changes, c => Assert.Equal(id, c.ValueId));
        Assert.Equal(["member warning"], outcome.Warnings);
        Assert.Equal("a/absent", Assert.Single(member.ApplyEdits([new("a/absent", FieldValue.Of(1))]).Errors).ValueId);
        Assert.Throws<KeyNotFoundException>(() => member.ApplyEdits([new(id, FieldValue.Of(9)), new("b/value", FieldValue.Of(1))]));
        Assert.Equal(4, document.Value); // All identities are checked before dispatch.
        Assert.Throws<KeyNotFoundException>(() => member.Query("b/items", new()));
        Assert.Throws<KeyNotFoundException>(() => member.GetValue("a/absent"));
        Assert.Throws<KeyNotFoundException>(() => member.InvokeAction("a/absent", new Dictionary<string, FieldValue>()));
        var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(5) };
        var action = member.InvokeAction("a/set", parameters);
        Assert.Same(parameters, document.LastParameters);
        Assert.Equal(id, Assert.Single(action.Changes).ValueId);
        Assert.Equal("Set", action.Message);
        Assert.Equal(["action warning"], action.Warnings);
        Assert.True(action.StructureChanged);
        Assert.NotNull(action.Revert); // Remains a MEMBER token; the group must replace it.
        _ = document.RestoreMemento(action.Revert);
        Assert.Equal(4, document.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlansRoundTripSelectionQueryExclusionsDiagnosticsAndFlags(bool errors)
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("a", document);
        var query = new QueryableCollectionQuery { Cursor = "opaque" };
        var scope = new ActionScope([member.Prefix(MemberAdapterDocument.RowKey)])
        { QueryScope = new("a/items", query, 12) { ExcludedRowKeys = ["a/excluded/~"] } };
        var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(7) };
        if (errors) parameters.Add("errors", FieldValue.Of(true));
        var plan = member.PlanAction("a/set", parameters, scope);
        Assert.Equal([MemberAdapterDocument.RowKey], document.LastScope!.RowKeys);
        Assert.Equal("items", document.LastScope.QueryScope!.CollectionId);
        Assert.Equal(["excluded/~"], document.LastScope.QueryScope.ExcludedRowKeys);
        Assert.Same(query, document.LastScope.QueryScope.Query);
        Assert.Equal(12, document.LastScope.QueryScope.Revision);
        Assert.Equal("a/" + MemberAdapterDocument.RowKey, Assert.Single(plan.SkippedRows).RowKey);
        Assert.True(Assert.Single(plan.Changes).Derived);
        Assert.False(plan.Changes[0].NewExists);
        Assert.Equal(["plan warning"], plan.Warnings);
        if (errors)
        {
            Assert.Equal([member.Prefix(MemberAdapterDocument.ValueId), ""], plan.Errors.Select(e => e.ValueId));
            Assert.Equal([MemberAdapterDocument.ValueId, ""], member.ToLocalPlan(plan).Errors.Select(e => e.ValueId));
        }
        else
        {
            var outcome = member.ApplyActionPlan("a/set", parameters, scope, plan);
            Assert.Equal(member.Prefix(MemberAdapterDocument.ValueId), Assert.Single(outcome.Changes).ValueId);
            Assert.Equal(MemberAdapterDocument.ValueId, Assert.Single(document.LastPlan!.Changes).ValueId);
            Assert.Equal(MemberAdapterDocument.RowKey, Assert.Single(document.LastPlan.SkippedRows).RowKey);
        }
        _ = member.PlanAction("a/set", parameters, new());
        Assert.Null(document.LastScope!.RowKeys);
        _ = member.PlanAction("a/set", parameters, new([]));
        Assert.Empty(document.LastScope!.RowKeys!);
        Assert.Throws<KeyNotFoundException>(() => member.PlanAction("a/set", parameters,
            scope with { QueryScope = scope.QueryScope with { ExcludedRowKeys = ["b/row"] } }));
        Assert.Throws<KeyNotFoundException>(() => member.ApplyActionPlan("a/set", parameters, scope,
            plan with { Changes = [plan.Changes[0] with { ValueId = "b/value" }] }));
    }

    [Fact]
    public void RouterPreflightsAllIdsAndPreservesMemberAndDuplicateEditOrder()
    {
        using var first = new MemberAdapterDocument();
        using var second = new MemberAdapterDocument();
        var a = new DocumentMemberAdapter("a", first);
        var b = new DocumentMemberAdapter("b", second);
        var router = new DocumentMemberRouter([a, b]);
        var batches = router.PartitionEdits([new("b/x", FieldValue.Of(1)), new("a/y", FieldValue.Of(2)), new("b/x", FieldValue.Of(3))]);
        Assert.Equal([b, a], batches.Select(batch => batch.Member));
        Assert.Equal([1, 3], batches[0].Edits.Select(edit => edit.Value.AsInt32()));
        Assert.Same(b, router.Resolve("b/x"));
        Assert.Throws<KeyNotFoundException>(() => router.Resolve("unknown/x"));
        Assert.Throws<KeyNotFoundException>(() => router.PartitionEdits([new("a/x", FieldValue.Of(2)), new("unknown/x", FieldValue.Of(3))]));
        Assert.Null(first.LastEdits);
        Assert.Null(second.LastEdits);
        Assert.Throws<ArgumentException>(() => new DocumentMemberRouter([a, a]));
        var overlapping = new DocumentMemberRouter([a, new DocumentMemberAdapter("a/nested", second)]);
        Assert.Throws<InvalidOperationException>(() => overlapping.Resolve("a/nested/x"));
        Assert.False(first.Disposed);
    }

    [Fact]
    public void OptionalCapabilitiesDoNotInventQueriesOrPlanning()
    {
        using var document = new PlainDocument();
        var member = new DocumentMemberAdapter("a", document);
        Assert.Empty(member.Collections);
        Assert.Throws<NotSupportedException>(() => member.Query("a/items", new()));
        Assert.Throws<NotSupportedException>(() => member.PlanAction("a/set", new Dictionary<string, FieldValue>(), new()));
        Assert.Throws<NotSupportedException>(() => member.ApplyActionPlan("a/set", new Dictionary<string, FieldValue>(), new(), new()));
        Assert.Throws<KeyNotFoundException>(() => member.Query("b/items", new()));
    }

    [Fact]
    public void MetadataForwardingQualifiesCanonicalAliasesAndPreservesPresentation()
    {
        using var document = new MetadataMemberDocument();
        var member = new DocumentMemberAdapter("profile", document);
        var alias = Assert.Single(member.ResolveFieldMetadata(["profile/legacy"]));
        Assert.Equal("profile/legacy", alias.ValueId);
        Assert.Equal("profile/current", alias.CanonicalValueId);
        Assert.Equal(["settings"], alias.Path);
        Assert.Equal(Confidence.Inferred, alias.Presentation!.Confidence);
        var router = new DocumentMemberRouter([member]);
        Assert.Equal("profile/current", Assert.Single(router.ResolveFieldMetadata(["profile/current"])).ValueId);
    }

    private sealed class PlainDocument : BoundSaveDocument
    {
        public override string Title => "Plain";
        public override ViewNode BuildView() => new TextNode("Plain");
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }

    private sealed class MetadataMemberDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;

        public MetadataMemberDocument()
        {
            RegisterField(new FieldMetadata("current", "Current", IntegerSpec.Int32)
            {
                Path = ["settings"],
                Presentation = new FieldPresentation { Confidence = Confidence.Verified },
            }, () => FieldValue.Of(_value), value => _value = value.AsInt32());
            RegisterField(new FieldMetadata("legacy", "Legacy", IntegerSpec.Int32 with { ReadOnly = true })
            {
                BindingKind = FieldBindingKind.Derived,
                CanonicalValueId = "current",
                Path = ["settings"],
                Presentation = new FieldPresentation { Confidence = Confidence.Inferred },
            }, () => FieldValue.Of(_value));
        }

        public override string Title => "Metadata member";
        public override ViewNode BuildView() => new TextNode("metadata");
        public override IReadOnlyList<FileOutput> Serialize() => [new("member", new byte[] { (byte)_value })];
    }
}
