using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ReferenceMutationEffectivenessTests
{
    [Fact]
    public void ResurrectedActionsCanResolveTheirReferenceParametersAgain()
    {
        using var fixture = new MutationSessionFixture(new ReferenceProbe());
        fixture.Session.BuildView();
        var parameters = new Dictionary<string, System.Text.Json.JsonElement>();
        Assert.True(fixture.Session.InvokeAction("choose", parameters).Ok);
        Assert.True(fixture.Session.InvokeAction("toggle", parameters).Ok);
        Assert.Equal("unknown", fixture.Session.FindReference("item", "unknown", actionId: "choose").RawId);
    }

    [Fact]
    public void ReferenceParametersAreResolvedWithinTheRequestedAction()
    {
        using var fixture = new MutationSessionFixture(new ReferenceProbe());
        fixture.Session.BuildView();
        var candidate = fixture.Session.FindReference("item", "unknown", actionId: "choose");
        Assert.Equal("unknown", candidate.RawId);
        Assert.Throws<KeyNotFoundException>(() => fixture.Session.FindReference("missing", "unknown", actionId: "choose"));
    }

    [Fact]
    public void RetiredActionsRejectTheirReferenceParametersAsStale()
    {
        using var fixture = new MutationSessionFixture(new ReferenceProbe());
        fixture.Session.BuildView();
        Assert.True(fixture.Session.InvokeAction("choose", new Dictionary<string, System.Text.Json.JsonElement>()).Ok);
        Assert.Throws<StaleViewException>(() => fixture.Session.FindReference("item", "unknown", actionId: "choose"));
    }

    private sealed class ReferenceProbe : BoundSaveDocument
    {
        private bool _removed;
        public override string Title => "References";
        public override ViewNode BuildView() => new SectionNode(Title, _removed ? [new ActionNode("toggle", "Toggle")]
            : [new ActionNode("toggle", "Toggle"), new ActionNode("choose", "Choose")
            {
                Parameters = [new("text", "Text", new TextSpec(), FieldValue.Of("")),
                    new("item", "Item", new ReferenceSpec("items") { AllowCustom = true }, FieldValue.Of("unknown"))],
            }]);
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        { _removed = !_removed; return new() { StructureChanged = true }; }
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
