using System.Text.Json;
using SaveToolkit.Core.Sessions;

namespace SaveToolkit.Core.Tests;

public sealed class FieldMetadataLifecycleTests
{
    [Fact]
    public void PartiallyRegisteredDocumentStillEditsAndReplaysViewOnlyFields()
    {
        using var fixture = new MetadataSessionFixture();
        Assert.True(fixture.Session.ApplyEdits([new IncomingEdit("visible", JsonSerializer.SerializeToElement(3))]).Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(1, fixture.Document.GetValue("visible").AsInt32());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(3, fixture.Document.GetValue("visible").AsInt32());
    }

    [Fact]
    public void UnloadedPrimaryRetainsPluginValidation()
    {
        using var fixture = new MetadataSessionFixture();
        var result = fixture.Session.ApplyEdits([new IncomingEdit("quantity", JsonSerializer.SerializeToElement(6))]);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Message == "Custom validation rejected six.");
        Assert.Equal(0, fixture.Document.Writes);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyRevalidatesMetadataEvenWhenPlannedValuesAgree(bool changeLabel)
    {
        using var fixture = new MetadataSessionFixture();
        var preview = fixture.Session.PlanAction("fill", new Dictionary<string, JsonElement>());
        Assert.NotNull(preview.PlanId);
        if (changeLabel) fixture.Document.CanonicalLabel = "New label";
        else fixture.Document.Maximum = 9;
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
        Assert.Equal(0, fixture.Document.Writes);
        Assert.False(fixture.Session.State.IsDirty);
    }

    [Fact]
    public void StructuralUndoRebindsUnloadedFieldsBeforeValueReplay()
    {
        using var fixture = new MetadataSessionFixture();
        Assert.True(fixture.Session.ApplyEdits([new IncomingEdit("quantity", JsonSerializer.SerializeToElement(7))]).Ok);
        Assert.True(fixture.Session.InvokeAction("replace", new Dictionary<string, JsonElement>()).Ok);
        Assert.Equal(4, fixture.Document.Quantity);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(7, fixture.Document.Quantity);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(2, fixture.Document.Quantity);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(4, fixture.Document.Quantity);
        Assert.Equal(0, fixture.Document.Expansions);
    }
}
