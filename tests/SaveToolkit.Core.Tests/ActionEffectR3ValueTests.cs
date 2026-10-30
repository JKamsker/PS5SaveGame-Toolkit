using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectR3ValueTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void ActualPreviewRejectsValueChangingSingleNormalizationBeforeTokenOrWriter(int wrapping, bool maximumOne)
    {
        using var fixture = new ActionEffectR2Fixture();
        var spec = FloatSpec.Single with { Max = maximumOne ? 1 : null };
        fixture.Document.Preview = () => Plan(spec, FieldValue.Of(1.0), FieldValue.Of(1.00000001), wrapping);

        var preview = fixture.Session.PlanAction("change", new Dictionary<string, JsonElement>());

        Assert.Null(preview.PlanId);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("Normalization would change", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Document.CaptureCalls);
        Assert.Equal(0, fixture.Document.WriterCalls);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void ActualPreviewRetainsDistinctDoubleAndExactlyRepresentableSingleValues(bool single, int wrapping)
    {
        using var fixture = new ActionEffectR2Fixture();
        var next = single ? 2.0 : 1.00000001;
        var plan = Plan(single ? FloatSpec.Single : FloatSpec.Double, FieldValue.Of(1.0), FieldValue.Of(next), wrapping);
        fixture.Document.Preview = () => plan;

        var preview = fixture.Session.PlanAction("change", new Dictionary<string, JsonElement>());

        Assert.NotNull(preview.PlanId);
        Assert.Equal(plan.Changes[0].NewValue, preview.Plan.Changes[0].NewValue);
        Assert.Equal(0, fixture.Document.CaptureCalls);
        Assert.Equal(0, fixture.Document.WriterCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimestampEffectsRejectChangedTextNormalizationBeforeTokenOrWriter(bool offset)
    {
        using var fixture = new ActionEffectR2Fixture();
        var oldValue = offset ? "2026-10-29T00:00:00.0000000+00:00" : "2026-10-29T00:00:00.0000000";
        var next = offset ? "2026-10-30T00:00:00Z" : "2026-10-30T00:00:00";
        fixture.Document.Preview = () => Plan(new DateTimeSpec { HasTimeZone = offset },
            FieldValue.Of(oldValue), FieldValue.Of(next), wrapping: 2);

        var preview = fixture.Session.PlanAction("change", new Dictionary<string, JsonElement>());

        Assert.Null(preview.PlanId);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("Normalization would change", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Document.CaptureCalls);
        Assert.Equal(0, fixture.Document.WriterCalls);
    }

    private static ActionPlan Plan(FieldSpec leaf, FieldValue oldValue, FieldValue newValue, int wrapping)
    {
        FieldSpec spec = leaf;
        if (wrapping is 2 or 3)
        {
            spec = new UnionSpec([new("selected", "Selected", spec), new("unused", "Unused", new TextSpec())]);
            oldValue = FieldValue.Union("selected", oldValue);
            newValue = FieldValue.Union("selected", newValue);
        }
        if (wrapping is 1 or 3) spec = new NullableSpec(spec);
        return new ActionPlan
        {
            Changes = [new PlannedChange("effect", "Effect", oldValue, newValue)
            { Effect = new(ActionEffectKind.Observation, spec with { ReadOnly = true }) }],
        };
    }
}
