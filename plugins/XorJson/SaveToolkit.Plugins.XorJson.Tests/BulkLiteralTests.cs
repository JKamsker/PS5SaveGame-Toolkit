using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class BulkLiteralTests
{
    public static TheoryData<string> OversizedNumericLiterals => new()
    {
        "1" + new string('0', ExactJsonNumber.HardMaxLength), "1e1000000001", "1e-1000000001",
        "1E+2147483648", "1e-2147483649", "1e" + new string('9', 1024),
    };

    [Theory]
    [MemberData(nameof(OversizedNumericLiterals))]
    public void OversizedNumericLiteralCannotBecomeAStringWrite(string literal)
    {
        foreach (var xor in new[] { false, true }) AssertRejectedLiteral(literal, xor);
    }

    private static void AssertRejectedLiteral(string literal, bool xor)
    {
        var bytes = XorJsonTests.Encode("{\"text\":\"keep\",\"number\":0}", xor);
        using var document = XorJsonTests.Open(bytes);
        var text = XorJsonTests.Fields(document).Single(field => field.Label == "text");
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
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
