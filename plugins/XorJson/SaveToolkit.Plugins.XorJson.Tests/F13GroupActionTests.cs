using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class F13GroupActionTests
{
    public static IEnumerable<object[]> Actions()
    {
        foreach (var xor in new[] { false, true })
        foreach (var action in new[] { "json/bulk-set", "character/set-attribute", "character/set-stacks", "character/replace-item", "character/set-rarity" })
            yield return [xor, action];
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void EveryPlannedFamilyPreflightsExactMembersAndKeepsOneHistoryStep(bool xor, string action)
    {
        using var fixture = new F13GroupSessionFixture(xor);
        var session = fixture.Session;
        var allFields = ViewTraversal.Nodes(session).OfType<FieldNode>().ToArray();
        var bulk = action == "json/bulk-set";
        var collection = action == "character/set-attribute" ? "character/character/attributes" : "character/character/inventory";
        var row = bulk ? null : session.QueryCollection(collection, new(), null, TestContext.Current.CancellationToken).Rows[0];
        var target = bulk ? allFields.Single(field => field.Label == "volume")
            : Assert.IsType<FieldNode>(row!.Cells[action switch
            {
                "character/set-attribute" => "value", "character/set-stacks" => "stack",
                "character/replace-item" => "id", _ => "rarity",
            }]);
        var actionId = (bulk ? "global/" : "character/") + action;
        var parameters = Parameters(action);
        var selected = new ActionScope([bulk ? target.ValueId : row!.Key]);
        var initialState = session.State;
        var invalidParameters = session.PlanAction(actionId, new Dictionary<string, JsonElement>
        {
            [action is "character/replace-item" or "character/set-rarity" ? "id" : "value"] = JsonSerializer.SerializeToElement("not a scalar or catalog id"),
        }, selected, null, TestContext.Current.CancellationToken);
        Assert.Null(invalidParameters.PlanId);
        Assert.NotEmpty(invalidParameters.Plan.Errors);
        Assert.Equal(initialState, session.State);
        fixture.AssertBytes();
        var empty = session.PlanAction(actionId, parameters, new ActionScope([]), null, TestContext.Current.CancellationToken);
        Assert.Empty(empty.Plan.Errors);
        Assert.Empty(empty.Plan.Changes);
        Assert.NotNull(empty.PlanId);
        Assert.True(session.ApplyActionPlan(empty.PlanId!, empty.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(initialState, session.State);
        fixture.AssertBytes();

        var document = session.PlanAction(actionId, parameters, new ActionScope(), null, TestContext.Current.CancellationToken);
        Assert.Empty(document.Plan.Errors);
        Assert.Contains(document.Plan.Changes, change => change.ValueId == target.ValueId);
        var query = session.PlanAction(actionId, parameters, new ActionScope
        {
            QueryScope = new ActionQueryScope(bulk ? "global/json/array/6" : collection, new QueryableCollectionQuery(), session.State.Revision),
        }, null, TestContext.Current.CancellationToken);
        Assert.Empty(query.Plan.Errors);
        if (bulk) Assert.Equal(100, query.Plan.Changes.Count);
        else Assert.Equal(document.Plan.Changes, query.Plan.Changes);
        Assert.All(query.Plan.Changes, effect =>
        {
            Assert.False(effect.Derived);
            Assert.True(effect.OldExists);
            Assert.True(effect.NewExists);
        });
        Assert.Equal(initialState, session.State);
        fixture.AssertBytes();

        var preview = session.PlanAction(actionId, parameters, selected, null, TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        var change = Assert.Single(preview.Plan.Changes);
        Assert.Equal(target.ValueId, change.ValueId);
        Assert.Equal(target.Value, change.OldValue);
        Assert.False(change.Derived);
        Assert.True(change.OldExists);
        Assert.True(change.NewExists);
        Assert.Equal(initialState, session.State);
        fixture.AssertBytes();
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(initialState.Revision + 1, session.State.Revision);
        Assert.True(session.State.CanUndo);
        var expectedCharacter = ExpectedCharacter(action);
        var expectedGlobal = bulk ? F13GroupSessionFixture.Global.Replace("\"volume\":50", "\"volume\":49", StringComparison.Ordinal) : F13GroupSessionFixture.Global;
        fixture.AssertBytes(expectedCharacter, expectedGlobal);
        var reviewState = session.State;
        var reviewed = Assert.Single(session.GetChanges(TestContext.Current.CancellationToken).Changes);
        Assert.Equal(change.ValueId, reviewed.ValueId);
        Assert.Equal(change.OldValue, reviewed.OriginalValue);
        Assert.Equal(change.NewValue, reviewed.CurrentValue);
        Assert.Equal(change.ValueId, reviewed.CanonicalValueId);
        Assert.Equal(bulk ? "global" : "character", reviewed.Member?.Role);
        Assert.Null(reviewed.Member?.Provenance);
        Assert.Equal(reviewState, session.State);
        fixture.AssertBytes(expectedCharacter, expectedGlobal);

        var noop = session.PlanAction(actionId, parameters, selected, null, TestContext.Current.CancellationToken);
        Assert.Empty(noop.Plan.Changes);
        Assert.NotNull(noop.PlanId);
        Assert.True(session.ApplyActionPlan(noop.PlanId!, noop.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(reviewState, session.State);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
        fixture.AssertBytes();
        Assert.True(session.Redo().Ok);
        fixture.AssertBytes(expectedCharacter, expectedGlobal);

        var beforeFailure = session.State;
        var malformed = session.PlanAction(actionId, parameters, new ActionScope(["global/value/0004"]), null, TestContext.Current.CancellationToken);
        Assert.Null(malformed.PlanId);
        Assert.NotEmpty(malformed.Plan.Errors);
        Assert.Equal(beforeFailure, session.State);
        fixture.AssertBytes(expectedCharacter, expectedGlobal);

        Assert.True(session.Undo().Ok);
        foreach (var queryMode in new[] { false, true })
        {
            var scope = queryMode ? new ActionScope
            {
                QueryScope = new ActionQueryScope(bulk ? "global/json/array/6" : collection, new QueryableCollectionQuery(), session.State.Revision),
            } : new ActionScope();
            var beforeScope = session.State;
            var scoped = session.PlanAction(actionId, parameters, scope, null, TestContext.Current.CancellationToken);
            Assert.NotNull(scoped.PlanId);
            Assert.Empty(scoped.Plan.Errors);
            Assert.Equal(bulk ? queryMode ? 100 : 104 : 1, scoped.Plan.Changes.Count);
            Assert.Equal(beforeScope, session.State);
            fixture.AssertBytes();
            Assert.True(session.ApplyActionPlan(scoped.PlanId!, scoped.Revision, TestContext.Current.CancellationToken).Ok);
            var scopedGlobal = bulk ? ExpectedGlobal(queryMode) : F13GroupSessionFixture.Global;
            fixture.AssertBytes(expectedCharacter, scopedGlobal);
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.CanUndo);
            fixture.AssertBytes();
            Assert.True(session.Redo().Ok);
            fixture.AssertBytes(expectedCharacter, scopedGlobal);
            Assert.True(session.Undo().Ok);
            fixture.AssertBytes();
        }

        using var readOnly = new F13GroupSessionFixture(xor, characterReadOnly: true);
        var readOnlyState = readOnly.Session.State;
        var rejected = readOnly.Session.PlanAction("character/" + action, parameters, new ActionScope(), null, TestContext.Current.CancellationToken);
        Assert.Null(rejected.PlanId);
        Assert.NotEmpty(rejected.Plan.Errors);
        Assert.Equal(readOnlyState, readOnly.Session.State);
        readOnly.AssertBytes();
    }

    private static IReadOnlyDictionary<string, JsonElement> Parameters(string action) => action switch
    {
        "json/bulk-set" => new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("49") },
        "character/set-attribute" => new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement("Emeralds"), ["value"] = JsonSerializer.SerializeToElement(2),
        },
        "character/set-stacks" => new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(2) },
        _ => new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement(action == "character/replace-item" ? "SW.Item.HoneyBoots_Unique" : "SW.Rarity.Rare"),
        },
    };

    private static string ExpectedCharacter(string action) => action switch
    {
        "character/set-attribute" => F13GroupSessionFixture.Character.Replace("\"CurrentValue\":1", "\"CurrentValue\":2", StringComparison.Ordinal),
        "character/set-stacks" => F13GroupSessionFixture.Character.Replace("\"None\"},\"StackCount\":1", "\"None\"},\"StackCount\":2", StringComparison.Ordinal),
        "character/replace-item" => F13GroupSessionFixture.Character.Replace("SW.Item.HoneyBoots\"", "SW.Item.HoneyBoots_Unique\"", StringComparison.Ordinal),
        "character/set-rarity" => F13GroupSessionFixture.Character.Replace("SW.Rarity.Common", "SW.Rarity.Rare", StringComparison.Ordinal),
        _ => F13GroupSessionFixture.Character,
    };

    private static string ExpectedGlobal(bool queryOnly)
    {
        var rows = string.Join(',', Enumerable.Repeat("49", 100));
        return queryOnly
            ? "{\"blobs\":[1,2],\"volume\":50,\"future\":1.2300E+02,\"rows\":[" + rows + "]}"
            : "{\"blobs\":[49,49],\"volume\":49,\"future\":49,\"rows\":[" + rows + "]}";
    }
}
