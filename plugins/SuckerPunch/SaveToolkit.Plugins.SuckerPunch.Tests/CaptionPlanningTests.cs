using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CaptionPlanningTests
{
    [Theory]
    [InlineData("dis.dat", 47, 0x0204)]
    [InlineData("dis.dat", 48, 0x0004)]
    [InlineData("memory.dat", 49, 0x0204)]
    public void PreviewIsPureAndPublishesExactCanonicalValueAndLogicalCapacity(string role, int version, int format)
    {
        var save = new SyntheticSave(false);
        save.Write(12, (uint)version);
        save.Repair();
        var sfo = CaptionTestData.Sfo(format: (ushort)format);
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(sfo, save, role));
        Assert.Equal([CaptionTestData.SfoPath], ((IAdditionalOutputOwner)document).AdditionalOutputPaths);
        var action = Action(document);
        Assert.True(action.Mutates && action.SupportsPlanning);
        Assert.Null(action.SelectionTableId);
        var parameter = Assert.Single(action.Parameters);
        Assert.Equal(31, Assert.IsType<TextSpec>(parameter.Spec).MaxUtf8Bytes);
        Assert.Null(Assert.IsType<TextSpec>(parameter.Spec).MaxLength);
        Assert.Equal(CaptionTestData.Original, parameter.Value.AsString());
        var plan = CaptionTestData.Plan(document, "Night run α");
        Assert.Empty(plan.Errors);
        Assert.True(ActionPlans.ContentEquals(plan, CaptionTestData.Plan(document, "Night run α")));
        var change = Assert.Single(plan.Changes);
        Assert.Equal(CaptionTestData.ValueId, change.ValueId);
        Assert.Equal(CaptionTestData.Original, change.OldValue.AsString());
        Assert.Equal("Night run α", change.NewValue.AsString());
        Assert.False(change.Derived);
        Assert.True(change.OldExists && change.NewExists);
        Assert.Contains(plan.Warnings, warning => warning.Contains("native console acceptance is unverified", StringComparison.Ordinal));
        Assert.Equal(sfo, CaptionTestData.Metadata(document));
        Assert.Equal(save.Bytes, CaptionTestData.Primary(document));
        var descriptor = Assert.Single(((IFieldMetadataDocument)document).ResolveFieldMetadata([CaptionTestData.ValueId]));
        Assert.False(descriptor.Spec.ReadOnly);
        Assert.Equal(FieldBindingKind.Primary, descriptor.BindingKind);
        Assert.Equal(change.OldValue, document.GetValue(descriptor.ValueId));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("😀", true)]
    [InlineData("éé", true)]
    [InlineData("\uFEFFa", true)]
    [InlineData("abcde", false)]
    [InlineData("😀a", false)]
    [InlineData("a\0b", false)]
    [InlineData("\uD800", false)]
    [InlineData("\uDC00", false)]
    [InlineData("a\nb", false)]
    public void StrictInputValidationUsesUtf8BytesAndSeparateSingleLinePolicy(string text, bool valid)
    {
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo("a", 5)));
        var plan = CaptionTestData.Plan(document, text);
        Assert.Equal(valid, plan.Errors.Count == 0);
        if (valid)
        {
            Assert.True(CaptionTestData.Apply(document, text).Succeeded);
            Assert.Equal(text, CaptionTestData.Text(CaptionTestData.Metadata(document)));
        }
        else Assert.Equal("a", CaptionTestData.Text(CaptionTestData.Metadata(document)));
    }

    [Fact]
    public void TerminatorOnlyCapacityAcceptsEmptyValueAndRejectsPayload()
    {
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo("", 1)));
        Assert.Equal(0, Assert.IsType<TextSpec>(Assert.Single(Action(document).Parameters).Spec).MaxUtf8Bytes);
        Assert.Empty(CaptionTestData.Plan(document, "").Changes);
        Assert.NotEmpty(CaptionTestData.Plan(document, "a").Errors);
        Assert.Equal("", CaptionTestData.Text(CaptionTestData.Metadata(document)));
    }

    [Fact]
    public void InclusiveGlobalCapAndEmbeddedFeffAreEnforced()
    {
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo("a", 8192)));
        Assert.Equal(4095, Assert.IsType<TextSpec>(Assert.Single(Action(document).Parameters).Spec).MaxUtf8Bytes);
        Assert.Empty(CaptionTestData.Plan(document, new string('a', 4092) + "\uFEFF").Errors);
        Assert.NotEmpty(CaptionTestData.Plan(document, new string('a', 4093) + "\uFEFF").Errors);
        Assert.Empty(CaptionTestData.Plan(document, new string('a', 4095)).Errors);
        Assert.NotEmpty(CaptionTestData.Plan(document, new string('a', 4096)).Errors);
    }

    [Fact]
    public void UnsupportedScopesTypesAndForgedPlansRejectWithoutMutation()
    {
        var sfo = CaptionTestData.Sfo();
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(sfo));
        Assert.NotEmpty(CaptionTestData.Plan(document, "new", new([])).Errors);
        Assert.NotEmpty(CaptionTestData.Plan(document, "new", new(["Supplies"])).Errors);
        var planner = (IActionPlanningDocument)document;
        Assert.NotEmpty(planner.PlanAction(CaptionTestData.ActionId, new Dictionary<string, FieldValue> { ["caption"] = FieldValue.Of(1L) }, new()).Errors);
        var plan = CaptionTestData.Plan(document, "new");
        var forged = plan with { Changes = [plan.Changes[0] with { Derived = true }] };
        Assert.False(planner.ApplyActionPlan(CaptionTestData.ActionId, CaptionTestData.Parameters("new"), new(), forged).Succeeded);
        Assert.Equal(sfo, CaptionTestData.Metadata(document));
    }

    [Fact]
    public void SdkPatchRejectsChangedSourceAndForgedEntryIdentity()
    {
        var bytes = CaptionTestData.Sfo();
        var original = ParamSfoDocument.Parse(bytes);
        var plan = original.PlanExistingUtf8("SUBTITLE", "new");
        bytes[^1] ^= 0x01;
        Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(bytes).Apply(plan));
        Assert.Throws<ParamSfoPatchException>(() => original.Apply(plan with { OriginalEntrySha256 = new string('0', 64) }));
        Assert.Equal(CaptionTestData.Sfo(), original.OriginalBytes.ToArray());
    }

    [Fact]
    public void SameCaptionWithChangedReservedBytesMakesOldPreviewStale()
    {
        using var document = SuckerPunchTestData.Open(CaptionTestData.File(CaptionTestData.Sfo()));
        var oldPlan = CaptionTestData.Plan(document, "target");
        Assert.True(CaptionTestData.Apply(document, "Longer caption").Succeeded);
        Assert.True(CaptionTestData.Apply(document, CaptionTestData.Original).Succeeded);
        var before = CaptionTestData.Metadata(document);
        var result = ((IActionPlanningDocument)document).ApplyActionPlan(CaptionTestData.ActionId,
            CaptionTestData.Parameters("target"), new(), oldPlan);
        Assert.False(result.Succeeded);
        Assert.Equal(before, CaptionTestData.Metadata(document));
    }

    internal static ActionNode Action(ISaveDocument document) => Assert.Single(((TabsNode)document.BuildView()).Tabs[0].Children.OfType<ActionNode>());
}

