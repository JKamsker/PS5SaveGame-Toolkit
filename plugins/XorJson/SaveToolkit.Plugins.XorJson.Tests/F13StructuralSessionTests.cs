using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class F13StructuralSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectStructuralActionsKeepMementoHistoryCurrentBindingsAndExactMembers(bool xor)
    {
        using var fixture = new F13GroupSessionFixture(xor);
        var session = fixture.Session;
        var initial = session.State;
        var operations = new[]
        {
            new StructuralCase("global/json/add/1", "3", "[1,2,3]", []),
            new StructuralCase("global/json/remove/2", null, "[2]", ["global/value/2"]),
            new StructuralCase("global/json/replace/1", "[3]", "[3]", ["global/value/2", "global/value/3"]),
        };
        foreach (var operation in operations)
        {
            var actions = ViewTraversal.Nodes(session).OfType<ActionNode>();
            Assert.False(Assert.Single(actions, action => action.ActionId == operation.Id).SupportsPlanning);
            var expected = F13GroupSessionFixture.Global.Replace("[1,2]", operation.Array, StringComparison.Ordinal);
            var result = session.InvokeAction(operation.Id, Parameters(operation.Json), null, TestContext.Current.CancellationToken);
            Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
            Assert.True(result.StructureChanged);
            Assert.True(session.State.CanUndo);
            fixture.AssertBytes(global: expected);
            var fields = ViewTraversal.Nodes(session).OfType<FieldNode>().ToArray();
            foreach (var retired in operation.Retired) Assert.DoesNotContain(fields, field => field.ValueId == retired);
            var introduced = fields.Where(field => field.Value == FieldValue.ExactNumber("3") && field.ValueId.StartsWith("global/", StringComparison.Ordinal)).ToArray();
            if (operation.Json is not null)
            {
                var added = Assert.Single(introduced);
                Assert.DoesNotContain(added.ValueId, operation.Retired);
            }
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.CanUndo); // Exactly one structural memento, not one step per scalar.
            Assert.False(session.State.IsDirty);
            fixture.AssertBytes();
            var restored = ViewTraversal.Nodes(session).OfType<FieldNode>().Single(field => field.ValueId == "global/value/2");
            Assert.Equal(FieldValue.ExactNumber("1"), restored.Value);
            Assert.True(session.Redo().Ok);
            fixture.AssertBytes(global: expected);
            Assert.True(session.Undo().Ok);
            fixture.AssertBytes();
        }

        var state = session.State;
        var failed = session.InvokeAction("global/json/add/1", Parameters("1,2"), null, TestContext.Current.CancellationToken);
        Assert.False(failed.Ok);
        Assert.Equal(state, session.State);
        fixture.AssertBytes();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.InvokeAction("global/json/add/1", Parameters("3"), null, canceled.Token));
        Assert.Equal(state, session.State);
        fixture.AssertBytes();

        // Restored metadata must bind the current SDK-backed scalar, with real current group context.
        var edit = session.ApplyEdits([Edit("global/value/2", 9)], null, TestContext.Current.CancellationToken);
        Assert.True(edit.Ok);
        fixture.AssertBytes(global: F13GroupSessionFixture.Global.Replace("[1,2]", "[9,2]", StringComparison.Ordinal));
        var review = Assert.Single(session.GetChanges(TestContext.Current.CancellationToken).Changes);
        Assert.Equal("global/value/2", review.CanonicalValueId);
        Assert.Equal(FieldValue.ExactNumber("1"), review.OriginalValue);
        Assert.Equal(FieldValue.ExactNumber("9"), review.CurrentValue);
        Assert.Equal("global", review.Member?.Role);
        Assert.Equal(F13GroupSessionFixture.GlobalPath, review.Member?.RelativePath);
        Assert.Null(review.Member?.Provenance);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        fixture.AssertBytes();
        Assert.True(session.State.Revision > initial.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyNamesAndRejectedMixedWritesPreserveHostStateAndEveryPayload(bool xor)
    {
        using var fixture = new F13GroupSessionFixture(xor);
        var session = fixture.Session;
        var fields = ViewTraversal.Nodes(session).OfType<FieldNode>().ToArray();
        var empty = fields.Where(field => field.Label.Contains("member(\"\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, empty.Length);
        Assert.NotEqual(empty[0].ValueId, empty[1].ValueId);
        var state = session.State;
        Assert.Throws<OperationCanceledException>(() => session.PlanAction("global/json/bulk-set",
            new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("49") },
            new ActionScope(["global/value/4"]), null, new CancellationToken(canceled: true)));
        Assert.Equal(state, session.State);
        fixture.AssertBytes();

        var online = fields.Single(field => field.Label == "IsOnline");
        var rejected = session.ApplyEdits([
            Edit("global/value/4", 49), new(online.ValueId, JsonSerializer.SerializeToElement(true)),
        ], null, TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.Equal(state, session.State);
        fixture.AssertBytes();
        var badIdentity = session.ApplyEdits([Edit("global/value/04", 49)], null, TestContext.Current.CancellationToken);
        Assert.False(badIdentity.Ok);
        Assert.Equal(state, session.State);
        fixture.AssertBytes();

        Assert.True(session.ApplyEdits([Edit(empty[0].ValueId, 3)], null, TestContext.Current.CancellationToken).Ok);
        fixture.AssertBytes(character: F13GroupSessionFixture.Character.Replace("\"\":1,\"\":2", "\"\":3,\"\":2", StringComparison.Ordinal));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        fixture.AssertBytes();
        Assert.True(session.Redo().Ok);
        fixture.AssertBytes(character: F13GroupSessionFixture.Character.Replace("\"\":1,\"\":2", "\"\":3,\"\":2", StringComparison.Ordinal));
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));
    private static IReadOnlyDictionary<string, JsonElement> Parameters(string? json) => json is null
        ? new Dictionary<string, JsonElement>()
        : new Dictionary<string, JsonElement> { ["json"] = JsonSerializer.SerializeToElement(json) };
    private sealed record StructuralCase(string Id, string? Json, string Array, IReadOnlyList<string> Retired);
}
