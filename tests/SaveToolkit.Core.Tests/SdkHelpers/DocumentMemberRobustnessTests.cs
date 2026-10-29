using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class DocumentMemberRobustnessTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void EmptyRoleCannotCreateAnAdapter(string role)
    {
        using var document = new MemberAdapterDocument();
        Assert.Throws<ArgumentException>(() => new DocumentMemberAdapter(role, document));
    }

    [Theory]
    [InlineData("")]
    [InlineData("character")]
    [InlineData("/value")]
    [InlineData("Character/value")]
    [InlineData("characters/value")]
    [InlineData("profile/value")]
    public void ForeignOrMissingRoleIsRejectedBeforeAnyEdit(string key)
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("character", document);
        var router = new DocumentMemberRouter([member]);
        var edits = new ValueEdit[] { new(member.Prefix(MemberAdapterDocument.ValueId), FieldValue.Of(9)), new(key, FieldValue.Of(8)) };
        Assert.Throws<KeyNotFoundException>(() => router.PartitionEdits(edits));
        Assert.Throws<KeyNotFoundException>(() => member.ApplyEdits(edits));
        Assert.Null(document.LastEdits);
        Assert.Equal(1, document.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/value")]
    [InlineData("character/value")]
    [InlineData("items/a/b")]
    [InlineData("items/%2F/雪")]
    public void LocalSegmentsAreOpaqueAndUnknownBindingsStillRejectEdits(string local)
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("character", document);
        var router = new DocumentMemberRouter([member]);
        var key = member.Prefix(local);
        Assert.Same(member, router.Resolve(key));
        Assert.Equal(local, member.Local(key));
        Assert.Throws<KeyNotFoundException>(() => member.GetValue(key));
        var result = member.ApplyEdits([new(member.Prefix(MemberAdapterDocument.ValueId), FieldValue.Of(9)), new(key, FieldValue.Of(8))]);
        Assert.False(result.Succeeded);
        // Empty errors are reserved for general diagnostics by the adapter's public contract.
        Assert.Equal(local.Length == 0 ? "" : key, Assert.Single(result.Errors).ValueId);
        Assert.Equal(1, document.Value);
    }

    [Fact]
    public void DuplicateRolesAndCustomPrefixCollisionsNeverChooseAnOwner()
    {
        using var document = new MemberAdapterDocument();
        var first = new DocumentMemberAdapter("character", document);
        Assert.Throws<ArgumentException>(() => new DocumentMemberRouter([first, new("character", document)]));
        var caseDistinct = new DocumentMemberAdapter("Character", document);
        Assert.Same(caseDistinct, new DocumentMemberRouter([first, caseDistinct]).Resolve("Character/value"));
        var sharedPrefix = new DocumentMemberIdentityPolicy
        {
            ToGroupId = (_, id) => id,
            TryToLocalId = (_, id) => id,
        };
        var ambiguous = new DocumentMemberRouter([new("a", document, sharedPrefix), new("b", document, sharedPrefix)]);
        Assert.Throws<InvalidOperationException>(() => ambiguous.Resolve(""));
        Assert.Throws<InvalidOperationException>(() => ambiguous.PartitionEdits([new("value", FieldValue.Of(3))]));
        Assert.Null(document.LastEdits);
    }

    [Fact]
    public void IdenticalLocalBindingsRouteOnlyToTheNamedMember()
    {
        using var first = new MemberAdapterDocument();
        using var second = new MemberAdapterDocument(initialValue: 2);
        var a = new DocumentMemberAdapter("a", first);
        var b = new DocumentMemberAdapter("b", second);
        var router = new DocumentMemberRouter([a, b]);
        var edit = new ValueEdit(b.Prefix(MemberAdapterDocument.ValueId), FieldValue.Of(9));
        Assert.Throws<KeyNotFoundException>(() => a.ApplyEdits([edit]));
        var batch = Assert.Single(router.PartitionEdits([edit]));
        Assert.Same(b, batch.Member);
        Assert.True(batch.Member.ApplyEdits(batch.Edits).Succeeded);
        Assert.Equal(1, first.Value);
        Assert.Null(first.LastEdits);
        Assert.Equal(9, second.Value);
        Assert.Throws<KeyNotFoundException>(() => b.GetValue(b.Prefix(MemberAdapterDocument.ValueId.ToUpperInvariant())));
    }

    [Theory]
    [InlineData("row")]
    [InlineData("collection")]
    [InlineData("exclusion")]
    public void ForeignScopeIdentityRejectsBeforePlanning(string location)
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("a", document);
        var scope = new ActionScope([location == "row" ? "b/row" : "a/row"])
        {
            QueryScope = new(location == "collection" ? "b/items" : "a/items", new(), 0)
            { ExcludedRowKeys = [location == "exclusion" ? "b/row" : "a/row"] },
        };
        Assert.Throws<KeyNotFoundException>(() => member.PlanAction("a/set",
            new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(9) }, scope));
        Assert.Null(document.LastScope);
        Assert.Equal(1, document.Value);
    }

    [Fact]
    public void SlashContainingCollectionAndRowIdsRoundTripWithoutSplittingOrDecoding()
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("character", document,
            new() { QueryRowIdentity = (collection, key) => collection + "/" + key });
        const string collection = "inventory/subset/%2F";
        const string row = "item/a/b";
        var page = member.Page(new(collection, [], [new(row, new Dictionary<string, ViewNode>())], 1, 1, 0, 1));
        Assert.Equal("character/" + collection, page.CollectionId);
        Assert.Equal(collection + "/" + row, member.Local(Assert.Single(page.Rows).Key));
        var scope = member.ToLocalScope(new([page.Rows[0].Key])
        {
            QueryScope = new(page.CollectionId, new() { Cursor = "a/b" }, 7) { ExcludedRowKeys = [page.Rows[0].Key] },
        });
        Assert.Equal(collection, scope.QueryScope!.CollectionId);
        Assert.Equal("a/b", scope.QueryScope.Query.Cursor);
        Assert.Equal([collection + "/" + row], scope.RowKeys);
        Assert.Equal(scope.RowKeys, scope.QueryScope.ExcludedRowKeys);
    }

    [Theory]
    [InlineData("character/ammo/item-0")]
    [InlineData("character/inventory_items")]
    [InlineData("character/inventory_items/")]
    [InlineData("character/Inventory_Items/item-0")]
    [InlineData("character//inventory_items/item-0")]
    [InlineData("character/inventory_items/item-0/extra")]
    [InlineData("character/character/inventory_items/item-0")]
    public void DefaultScopeOnlyChecksRoleAndExplicitPolicyMustValidateCollectionRows(string key)
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("character", document);
        Assert.Same(member, new DocumentMemberRouter([member]).Resolve(key));
        Assert.Equal([key["character/".Length..]], member.ToLocalScope(new([key])).RowKeys);

        // The router has no action/collection model. Gearbox 5442668's exact lookup remains local policy.
        var strict = new DocumentMemberAdapter("character", document, new()
        {
            LocalScope = (adapter, scope) => scope with
            {
                RowKeys = scope.RowKeys?.Select(id => id == adapter.Prefix("inventory_items/item-0")
                    ? "item-0" : throw new KeyNotFoundException("Unknown inventory row.")).ToArray(),
            },
        });
        Assert.Throws<KeyNotFoundException>(() => strict.ToLocalScope(new([key])));
        Assert.Equal(["item-0"], strict.ToLocalScope(new(["character/inventory_items/item-0"])).RowKeys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterMemberFailureRestoresAllEarlierBatchesIncludingDuplicateWrites(bool unknownBinding)
    {
        var set = new MemorySaveSet().With("PPSA99999", "SAVE", new Dictionary<string, byte[]> { ["a"] = [1], ["b"] = [2] });
        using var group = new MemberAdapterGroup(new(set.Directories[0].Files.Select(file => new DocumentGroupFile(file.FileName, file)).ToArray()));
        var before = GroupAdapterPluginFixture.Bytes(group);
        var result = group.ApplyEdits([
            new("a/" + MemberAdapterDocument.ValueId, FieldValue.Of(3)),
            new("b/" + (unknownBinding ? "missing" : MemberAdapterDocument.ValueId), FieldValue.Of(-1)),
            new("a/" + MemberAdapterDocument.ValueId, FieldValue.Of(4)),
        ]);
        Assert.False(result.Succeeded);
        Assert.Empty(result.Changes);
        GroupAdapterPluginFixture.EqualBytes(before, group);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    public void MetadataRouterRejectsMalformedMemberResponsesBeforePublishing(string mode)
    {
        using var document = new MalformedMetadataDocument(mode);
        var member = new DocumentMemberAdapter("a", document);
        var router = new DocumentMemberRouter([member]);

        Assert.Throws<InvalidOperationException>(() => router.ResolveFieldMetadata(["a/value"]));
    }

    private sealed class MalformedMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        public MalformedMetadataDocument(string mode)
        {
            RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32),
                () => FieldValue.Of(1), _ => { });
            Mode = mode;
        }

        private string Mode { get; }
        public override string Title => "Malformed metadata";
        public override ViewNode BuildView() => new TextNode("metadata");
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            var field = base.ResolveFieldMetadata(ids).Single();
            return Mode == "duplicate" ? [field, field] : [field with { ValueId = "other" }];
        }
    }
}
