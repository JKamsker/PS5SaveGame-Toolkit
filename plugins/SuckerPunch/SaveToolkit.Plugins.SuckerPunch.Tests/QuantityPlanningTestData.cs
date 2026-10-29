using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

internal static class QuantityPlanningTestData
{
    internal static Dictionary<string, FieldValue> Target(long value) => new() { ["quantity"] = FieldValue.Of(value) };
}
