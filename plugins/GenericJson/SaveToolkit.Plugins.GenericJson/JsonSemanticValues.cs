using System.Globalization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonSemanticValues
{
    // Observed maximum across all supplied PPSA02343 generations; not an established game cap.
    internal const int ObservedCounterCeiling = 67;
    internal const int MaxTextLength = 4096;
    private const int MaxAdapterNumberLength = 128;

    public static FieldValue Number(string value) => FieldValue.Union(JsonScalar.Number, FieldValue.ExactNumber(value));
    public static FieldValue Text(string value) => FieldValue.Union(JsonScalar.Text, FieldValue.Of(value));
    public static string Raw(JsonValueToken node) => node.Original.ToString();
    public static int IndexCeiling(JsonValueToken node) => int.TryParse(Raw(node), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    public static int IndexCurrent(JsonValueToken node) => int.TryParse(node.Current.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public static JsonSemanticTarget? Primitive(JsonValueToken node, bool stringAdapters, bool profileCounters = false)
    {
        if (node.IsContainer || !node.Imported || node.Spec?.ReadOnly == true || node.Original.IsNull || node.Current.IsNull
            || JsonScalar.TypeOf(node.Original) != JsonScalar.TypeOf(node.Current)) return null;
        var type = JsonScalar.TypeOf(node.Original);
        var original = Raw(node);
        if (type == JsonScalar.Boolean || stringAdapters && original is "true" or "false" or "True" or "False" or "TRUE" or "FALSE")
        {
            var strings = type == JsonScalar.Text;
            return new(node, new BoolSpec(), FieldValue.Of(bool.TryParse(node.Current.ToString(), out var b) && b),
                v => strings ? Text(Spell(v.AsBool(), original)) : FieldValue.Union(JsonScalar.Boolean, v),
                v => v.Kind == FieldValueKind.Bool ? null : "Expected a boolean.") { IsFlag = strings };
        }
        if (stringAdapters && type == JsonScalar.Text && original.Length <= MaxAdapterNumberLength
            && original.All(char.IsAsciiDigit) && int.TryParse(original, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            var maximum = profileCounters ? Math.Max(ObservedCounterCeiling, count) : count;
            return new(node, new IntegerSpec(0, maximum),
                FieldValue.Of(int.TryParse(node.Current.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var current) ? current : count),
                v => Text(v.AsInteger().ToString(CultureInfo.InvariantCulture)),
                v => v.Kind == FieldValueKind.Integer && v.AsInteger() >= 0 && v.AsInteger() <= maximum ? null : $"Use an integer from 0 to {maximum}.")
            { IsCounter = profileCounters, Evidence = profileCounters
                ? $"String counter, unit unknown. Conservative ceiling {maximum}: max(imported value, sample-observed family maximum 67); not a proven cap."
                : $"Numeric-looking wrapper string, meaning unknown. Conservative range 0..{maximum}, retaining string storage; not a proven game cap." };
        }
        if (type == JsonScalar.Number)
        {
            var spec = new ExactNumberSpec { Min = original.StartsWith('-') ? original : "0", Max = original.StartsWith('-') ? "0" : original,
                MaxLength = MaxAdapterNumberLength };
            return Exact(node, spec, integer: !original.Contains('.') && !original.Contains('e', StringComparison.OrdinalIgnoreCase));
        }
        if (type != JsonScalar.Text) return null;
        var limit = Math.Min(MaxTextLength, original.Length);
        return new(node, new TextSpec { MaxLength = limit }, FieldValue.Of(node.Current.ToString()), v => Text(v.AsString()),
            v => v.Kind == FieldValueKind.Text && v.AsString().Length <= limit && JsonScalar.Validate(Text(v.AsString())) is null
                ? null : $"Use valid Unicode text of at most {limit} characters.");
    }

    public static JsonSemanticTarget Exact(JsonValueToken node, ExactNumberSpec spec, bool integer = false, decimal? step = null, decimal origin = 0) =>
        new(node, spec, node.Current.AsUnion().Value, v => FieldValue.Union(JsonScalar.Number, v), v =>
        {
            if (v.Kind != FieldValueKind.ExactNumber) return "Expected an exact JSON number.";
            var text = v.AsExactNumber();
            if (ExactJsonNumber.Validate(text, spec) is { } error) return error;
            if (integer && (!TryDecimal(text, out var n) || decimal.Truncate(n) != n))
                return "Expected an integral setting value.";
            var endpoint = spec.Min is { } min && ExactJsonNumber.Validate(text, new ExactNumberSpec { Min = min, Max = min }) is null
                || spec.Max is { } max && ExactJsonNumber.Validate(text, new ExactNumberSpec { Min = max, Max = max }) is null;
            if (step is { } increment && !endpoint && (!TryDecimal(text, out var number)
                || (number - origin) % increment != 0)) return "Value must follow the recorded step from the recorded minimum, or be a recorded endpoint.";
            return null;
        });

    public static bool TryDecimal(string text, out decimal value)
    {
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        var canonical = value.ToString(CultureInfo.InvariantCulture);
        // Decimal parsing can round. Require exact numeric equality before using decimal arithmetic.
        return ExactJsonNumber.Validate(text, new ExactNumberSpec { Min = canonical, Max = canonical }) is null;
    }

    private static string Spell(bool value, string original) => original is "True" or "False" ? value ? "True" : "False"
        : original is "TRUE" or "FALSE" ? value ? "TRUE" : "FALSE" : value ? "true" : "false";
}
