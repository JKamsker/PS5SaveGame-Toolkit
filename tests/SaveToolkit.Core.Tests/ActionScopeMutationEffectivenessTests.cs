using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ActionScopeMutationEffectivenessTests
{
    [Theory]
    [InlineData("query")]
    [InlineData("exclusions")]
    public void MissingQueryComponentsAreRejectedBeforeQuerying(string component)
    {
        var scope = new ActionQueryScope("items", component == "query" ? null! : new(), 0)
            { ExcludedRowKeys = component == "exclusions" ? null! : [] };
        var calls = 0;
        Assert.Throws<ArgumentNullException>(() => ActionQueryScopeResolver.Resolve(scope, 1,
            _ => { calls++; throw new InvalidOperationException("Must not query."); }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ExclusionsMayExactlyEqualTheSelectionBudget()
    {
        var scope = new ActionQueryScope("items", new(), 0) { ExcludedRowKeys = ["one"] };
        var result = ActionQueryScopeResolver.Resolve(scope, 1,
            query => new("items", [], [new TableRow("one", new Dictionary<string, ViewNode>())], 1, 1, 0, query.Limit));
        Assert.Empty(result.RowKeys!);
        Assert.Equal(["one"], result.QueryScope!.ExcludedRowKeys);
    }
}
