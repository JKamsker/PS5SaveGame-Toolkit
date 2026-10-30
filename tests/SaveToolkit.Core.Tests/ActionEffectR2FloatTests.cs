using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectR2FloatTests
{
    [Fact]
    public void OptionalFiniteFloatBoundsAndUnselectedUnboundedUnionBranchAreValid()
    {
        using var document = new ActionEffectR2Document();
        var index = new ViewIndex(document);
        foreach (var spec in new[] { FloatSpec.Double, FloatSpec.Single, new FloatSpec(),
            new FloatSpec { Min = 0 }, new FloatSpec { Max = 10 }, new FloatSpec { Min = 0, Max = 10 } })
        {
            var plan = Plan(spec with { ReadOnly = true }, FieldValue.Of(1.0), FieldValue.Of(2.0));
            Assert.Empty(FieldMetadataPlanning.Validate(index, document, plan));
        }
        var union = new UnionSpec([new("integer", "Integer", IntegerSpec.Int32), new("float", "Float", FloatSpec.Double)]) { ReadOnly = true };
        Assert.Empty(FieldMetadataPlanning.Validate(index, document,
            Plan(union, FieldValue.Union("integer", FieldValue.Of(1)), FieldValue.Union("integer", FieldValue.Of(2)))));
    }

    [Fact]
    public void NonfiniteOrInvertedFloatBoundsAreRejectedEvenWhenUnionBranchIsUnselected()
    {
        using var document = new ActionEffectR2Document();
        var index = new ViewIndex(document);
        foreach (var spec in new[] { new FloatSpec { Min = double.NaN }, new FloatSpec { Max = double.PositiveInfinity },
            new FloatSpec { Min = double.NegativeInfinity }, new FloatSpec { Min = 10, Max = 0 } })
        {
            var union = new UnionSpec([new("integer", "Integer", IntegerSpec.Int32), new("float", "Float", spec)]) { ReadOnly = true };
            Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document,
                Plan(union, FieldValue.Union("integer", FieldValue.Of(1)), FieldValue.Union("integer", FieldValue.Of(2)))));
        }
    }

    private static ActionPlan Plan(FieldSpec spec, FieldValue oldValue, FieldValue newValue) => new()
    {
        Changes = [new PlannedChange("effect", "Effect", oldValue, newValue) { Effect = new(ActionEffectKind.Observation, spec) }],
    };
}
