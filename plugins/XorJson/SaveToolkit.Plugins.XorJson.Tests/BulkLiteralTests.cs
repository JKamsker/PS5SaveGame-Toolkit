using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class BulkLiteralTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OversizedNumericLiteralCannotBecomeAStringWrite(bool xor, bool longMantissa)
    {
        var bytes = XorJsonTests.Encode("{\"text\":\"keep\",\"number\":0}", xor);
        using var document = XorJsonTests.Open(bytes);
        var text = XorJsonTests.Fields(document).Single(field => field.Label == "text");
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var literal = longMantissa ? "1" + new string('0', ExactJsonNumber.HardMaxLength) : "1e1000000001";
        var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(literal) };
        var scope = new ActionScope([text.ValueId]);

        var plan = planner.PlanAction("json/bulk-set", parameters, scope);

        Assert.NotEmpty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.False(planner.ApplyActionPlan("json/bulk-set", parameters, scope, plan).Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData("1e1000000000", "number")]
    [InlineData("\"1e1000000001\"", "text")]
    public void EditableNumbersAndExplicitStringsKeepTheirRequestedType(string literal, string label)
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("{\"text\":\"keep\",\"number\":0}"));
        var target = XorJsonTests.Fields(document).Single(field => field.Label == label);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(literal) };
        var scope = new ActionScope([target.ValueId]);
        var plan = planner.PlanAction("json/bulk-set", parameters, scope);

        Assert.Empty(plan.Errors);
        Assert.Single(plan.Changes);
        Assert.True(planner.ApplyActionPlan("json/bulk-set", parameters, scope, plan).Succeeded);
        var expected = label == "number" ? "{\"text\":\"keep\",\"number\":" + literal + "}"
            : "{\"text\":" + literal + ",\"number\":0}";
        Assert.Equal(XorJsonTests.Encode(expected), document.Serialize().Single().Content.ToArray());
    }
}
