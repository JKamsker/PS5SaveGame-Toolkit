using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class HonestLabelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawStructuralControlsExplainConsoleRiskBeforeConfirmation(bool xor)
    {
        var bytes = XorJsonTests.Encode("{\"rows\":[1],\"other\":true}", xor);
        using var document = XorJsonTests.Open(bytes);
        var actions = StructuralTests.Nodes(document).OfType<ActionNode>()
            .Where(action => action.ActionId.StartsWith("json/add/", StringComparison.Ordinal) ||
                action.ActionId.StartsWith("json/remove/", StringComparison.Ordinal) ||
                action.ActionId.StartsWith("json/replace/", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            Assert.Contains("Experimental write", action.Description!);
            Assert.Contains("no checksum or console reload has been verified", action.Description!);
            Assert.Contains("game may reject, repair, or clamp", action.Description!);
        });
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
