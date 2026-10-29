using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldMetadataTests
{
    [Fact]
    public void RegisteredOnlyFieldIsResolvedWithoutRendering()
    {
        using var document = new MetadataDocument();
        var index = new ViewIndex(document);
        Assert.Equal(new IntegerSpec(0, 10), index.Spec("quantity"));
        Assert.Equal(0, document.Views);
        Assert.Equal("Quantity", index.Metadata("quantity")!.Label);
    }

    [Fact]
    public void AgreeingViewRetainsRegisteredSpecInstance()
    {
        using var document = new MetadataDocument();
        var index = new ViewIndex(document);
        var registered = index.Spec("quantity");
        index.Add(new FieldNode("quantity", "Projection", new IntegerSpec(0, 10), FieldValue.Of(2)));
        Assert.Same(registered, index.Spec("quantity"));
    }

    [Fact]
    public void ConflictingViewIsRejected()
    {
        using var document = new MetadataDocument();
        var index = new ViewIndex(document);
        Assert.Throws<InvalidOperationException>(() => index.Add(
            new FieldNode("quantity", "Projection", IntegerSpec.Int32, FieldValue.Of(2))));
        Assert.Equal(new IntegerSpec(0, 10), index.Spec("quantity"));
    }

    [Theory]
    [InlineData("quantity", 11)]
    [InlineData("total", 4)]
    [InlineData("unknown", 4)]
    public void RegisteredConstraintsAndDerivedPolicyRejectWrites(string id, int value)
    {
        using var fixture = new MetadataSessionFixture();
        Assert.False(fixture.Session.ApplyEdits([Edit(id, value)]).Ok);
        Assert.Equal(2, fixture.Document.Quantity);
        Assert.False(fixture.Session.State.IsDirty);
    }

    [Fact]
    public void UnloadedEditUndoAndRedoReplayOnlyPrimaryAndRestoreExactBytes()
    {
        using var fixture = new MetadataSessionFixture();
        var before = fixture.Document.Serialize().Single().Content.ToArray();
        Assert.True(fixture.Session.ApplyEdits([Edit("quantity", 7)]).Ok);
        Assert.Equal(14, fixture.Document.GetValue("total").AsInt32());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(before, fixture.Document.Serialize().Single().Content.ToArray());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(7, fixture.Document.Quantity);
        Assert.Equal(0, fixture.Document.Expansions);
        Assert.Equal(3, fixture.Document.Writes);
    }

    [Fact]
    public void UnloadedPlanIsValidatedAndUndoable()
    {
        using var fixture = new MetadataSessionFixture();
        var preview = fixture.Session.PlanAction("fill", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(7, fixture.Document.Quantity);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(2, fixture.Document.Quantity);
        Assert.Equal(0, fixture.Document.Expansions);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("derived")]
    [InlineData("range")]
    public void InvalidMetadataPlanCannotBeApplied(string failure)
    {
        using var fixture = new MetadataSessionFixture();
        fixture.Document.PlanFailure = failure;
        var preview = fixture.Session.PlanAction("fill", new Dictionary<string, JsonElement>());
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal(0, fixture.Document.Writes);
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));

}
